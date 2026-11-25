using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReHand.Core
{
    /// <summary>
    /// Knows whether the participant is using hands or controllers, enforces the mode
    /// required by the current block, and watches hand-tracking quality.
    ///
    /// - Detects the active mode from the headset (OVRInput.GetActiveController).
    /// - When a block mode is set, disables the other modality's Interaction SDK interactors,
    ///   so picking up a controller during a hands block can't complete an exercise.
    /// - Raises ModeMismatchChanged so the UI can ask the user to pick up / put down the controllers.
    /// - Debounces per-hand tracking (tracked / low confidence / lost) and raises HandsLostWarningChanged
    ///   when a trained hand has been lost for a while.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class InputModeManager : MonoBehaviour
    {
        [SerializeField] private TrackedEffectors _effectors;

        [Header("Mode enforcement")]
        [Tooltip("Disable the other modality's interactors while a block mode is set.")]
        [SerializeField] private bool _enforceBlockMode = true;

        [Tooltip("Interactor groups (under ComprehensiveInteractorsLeft/Right/Interactors) used only with hands.")]
        [SerializeField] private string[] _handOnlyGroups = { "Hand", "Hand and No Controller" };

        [Tooltip("Interactor groups used only with controllers.")]
        [SerializeField] private string[] _controllerOnlyGroups = { "Controller", "Controller and No Hand", "Controller and Hand" };

        [Header("Hand tracking")]
        [Tooltip("A tracking state must last this long before it is reported (filters flicker).")]
        [SerializeField, Min(0f)] private float _trackingDebounceSeconds = 0.25f;

        [Tooltip("A trained hand must be lost this long before the 'keep your hands in front of you' warning is raised.")]
        [SerializeField, Min(0f)] private float _lostWarningDelaySeconds = 1.0f;

        /// <summary>Mode the headset currently reports.</summary>
        public InputMode ActiveMode { get; private set; } = InputMode.None;

        /// <summary>True when a block mode is set and the active mode is different (e.g. controllers picked up in a hands block).</summary>
        public bool IsModeMismatch { get; private set; }

        /// <summary>True while a trained hand has been lost longer than the warning delay (hands blocks only).</summary>
        public bool IsHandsLostWarning { get; private set; }

        /// <summary>Increments every time a hand becomes lost or low-confidence. Exercises compare it at the start and end of a repetition.</summary>
        public int TrackingIssueCounter { get; private set; }

        public HandTrackingState LeftState => _left.Confirmed;
        public HandTrackingState RightState => _right.Confirmed;

        public event Action<InputMode> ActiveModeChanged;
        public event Action<bool> ModeMismatchChanged;
        public event Action<bool> HandsLostWarningChanged;
        /// <summary>(isLeft, newState)</summary>
        public event Action<bool, HandTrackingState> HandTrackingStateChanged;

        private class HandTracker
        {
            public HandTrackingState Confirmed = HandTrackingState.Lost;
            public HandTrackingState Pending = HandTrackingState.Lost;
            public float PendingSince;
            // Time the hand became lost; +infinity while it is tracked.
            public float LostSince = 0f;
        }

        private readonly HandTracker _left = new HandTracker();
        private readonly HandTracker _right = new HandTracker();
        private readonly List<GameObject> _handOnlyObjects = new List<GameObject>();
        private readonly List<GameObject> _controllerOnlyObjects = new List<GameObject>();
        private InputMode _appliedEnforcement = (InputMode)(-1);

        protected virtual void Awake()
        {
            if (_effectors == null) _effectors = FindAnyObjectByType<TrackedEffectors>();
            CollectInteractorGroups();
        }

        protected virtual void OnEnable()
        {
            SessionContext.Changed += ApplyEnforcement;
        }

        protected virtual void OnDisable()
        {
            SessionContext.Changed -= ApplyEnforcement;
        }

        protected virtual void Start()
        {
            ApplyEnforcement();
        }

        protected virtual void Update()
        {
            UpdateActiveMode();
            UpdateMismatch();
            UpdateHandTracking(isLeft: true, _left);
            UpdateHandTracking(isLeft: false, _right);
            UpdateLostWarning();
        }

        /// <summary>Sets the mode required by the current block (None = both allowed).</summary>
        public void SetBlockMode(InputMode mode)
        {
            SessionContext.SetBlockMode(mode);
            DataLogger.Instance?.LogEvent("block_mode_set", details: mode.ToString());
        }

        private void UpdateActiveMode()
        {
            OVRInput.Controller active = OVRInput.GetActiveController();
            InputMode mode = InputMode.None;
            if ((active & OVRInput.Controller.Hands) != 0) mode = InputMode.Hands;
            else if ((active & OVRInput.Controller.Touch) != 0) mode = InputMode.Controllers;

            if (mode == ActiveMode) return;
            ActiveMode = mode;
            SessionContext.SetActiveMode(mode);
            DataLogger.Instance?.LogEvent("active_mode_changed", details: mode.ToString());
            ActiveModeChanged?.Invoke(mode);
        }

        private void UpdateMismatch()
        {
            InputMode block = SessionContext.BlockMode;
            bool mismatch = block != InputMode.None && ActiveMode != InputMode.None && ActiveMode != block;
            if (mismatch == IsModeMismatch) return;
            IsModeMismatch = mismatch;
            DataLogger.Instance?.LogEvent(mismatch ? "mode_mismatch_start" : "mode_mismatch_end",
                details: $"block={block} active={ActiveMode}");
            ModeMismatchChanged?.Invoke(mismatch);
        }

        private void UpdateHandTracking(bool isLeft, HandTracker tracker)
        {
            HandTrackingState raw = HandTrackingState.Lost;
            var hand = _effectors != null ? (isLeft ? _effectors.LeftHand : _effectors.RightHand) : null;
            // Only meaningful while hands are the active input; with controllers the hand data is synthetic.
            if (ActiveMode == InputMode.Hands && hand != null && hand.IsTrackedDataValid)
                raw = hand.IsHighConfidence ? HandTrackingState.Tracked : HandTrackingState.LowConfidence;

            float now = Time.unscaledTime;
            if (raw != tracker.Pending)
            {
                tracker.Pending = raw;
                tracker.PendingSince = now;
            }

            if (tracker.Pending == tracker.Confirmed || now - tracker.PendingSince < _trackingDebounceSeconds) return;

            tracker.Confirmed = tracker.Pending;
            tracker.LostSince = tracker.Confirmed == HandTrackingState.Lost ? tracker.PendingSince : float.PositiveInfinity;

            if (ActiveMode == InputMode.Hands && tracker.Confirmed != HandTrackingState.Tracked) TrackingIssueCounter++;
            if (ActiveMode == InputMode.Hands)
                DataLogger.Instance?.LogEvent("hand_tracking_" + tracker.Confirmed.ToString().ToLowerInvariant(),
                    item: isLeft ? "left_hand" : "right_hand");
            HandTrackingStateChanged?.Invoke(isLeft, tracker.Confirmed);
        }

        private void UpdateLostWarning()
        {
            bool handsExpected = SessionContext.BlockMode == InputMode.Hands
                || (SessionContext.BlockMode == InputMode.None && ActiveMode == InputMode.Hands);
            float now = Time.unscaledTime;
            bool warn = handsExpected && !IsModeMismatch
                && ((SessionContext.IsTrained(true) && now - _left.LostSince >= _lostWarningDelaySeconds)
                    || (SessionContext.IsTrained(false) && now - _right.LostSince >= _lostWarningDelaySeconds));

            if (warn == IsHandsLostWarning) return;
            IsHandsLostWarning = warn;
            DataLogger.Instance?.LogEvent(warn ? "hands_lost_warning_start" : "hands_lost_warning_end");
            HandsLostWarningChanged?.Invoke(warn);
        }

        private void CollectInteractorGroups()
        {
            _handOnlyObjects.Clear();
            _controllerOnlyObjects.Clear();
            var rig = _effectors != null && _effectors.CameraRig != null ? _effectors.CameraRig : FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) return;

            foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Interactors" || t.parent == null || !t.parent.name.StartsWith("ComprehensiveInteractors")) continue;
                foreach (Transform group in t)
                {
                    if (Array.IndexOf(_handOnlyGroups, group.name) >= 0) _handOnlyObjects.Add(group.gameObject);
                    else if (Array.IndexOf(_controllerOnlyGroups, group.name) >= 0) _controllerOnlyObjects.Add(group.gameObject);
                }
            }

            if (_handOnlyObjects.Count == 0 || _controllerOnlyObjects.Count == 0)
                Debug.LogWarning("[ReHand] InputModeManager: interactor groups not found; mode enforcement is disabled.", this);
        }

        private void ApplyEnforcement()
        {
            InputMode target = _enforceBlockMode ? SessionContext.BlockMode : InputMode.None;
            if (target == _appliedEnforcement) return;
            _appliedEnforcement = target;

            bool handsOn = target != InputMode.Controllers;
            bool controllersOn = target != InputMode.Hands;
            foreach (var go in _handOnlyObjects) if (go != null) go.SetActive(handsOn);
            foreach (var go in _controllerOnlyObjects) if (go != null) go.SetActive(controllersOn);
        }
    }
}
