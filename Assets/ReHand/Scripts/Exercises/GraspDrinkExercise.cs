using UnityEngine;
using UnityEngine.Events;

namespace ReHand.Exercises
{
    /// <summary>
    /// Dining-table task: pick up the glass of water, bring it to the mouth and hold it there
    /// for a moment (a sip), then put it back on the coaster. That is one repetition.
    ///
    /// - Released somewhere other than the coaster: soft feedback, the glass glides back to the
    ///   coaster and the repetition continues (the drop is counted), so a weak grip is not punished.
    /// - Put back on the coaster without drinking: it stays there and the repetition continues.
    /// - Not finished before the timeout: missed.
    /// Accuracy = distance between the glass base and the coaster centre at the final release.
    /// </summary>
    public class GraspDrinkExercise : ExerciseBase
    {
        [Header("Scene objects")]
        [SerializeField] private GraspObject _glass;
        [SerializeField] private PlacementSpot _coaster;
        [Tooltip("Found or created automatically if empty.")]
        [SerializeField] private MouthZone _mouth;

        [Header("Drinking")]
        [Tooltip("How long the glass rim must stay at the mouth to count as a sip.")]
        [SerializeField, Min(0.1f)] private float _drinkHoldSeconds = 1f;
        [Tooltip("If off, the repetition is only 'lift and put back on the coaster' (simpler variant).")]
        [SerializeField] private bool _requireDrink = true;

        [Header("Events")]
        [Tooltip("Raised when the sip is done (e.g. lower the water level).")]
        public UnityEvent OnDrink = new UnityEvent();
        [Tooltip("Raised at the start of every repetition (e.g. refill the glass).")]
        public UnityEvent OnRefill = new UnityEvent();

        /// <summary>0..1 progress of the current sip, for a fill indicator.</summary>
        public float DrinkProgress => _drinkHoldSeconds > 0f ? Mathf.Clamp01(_mouthTimer / _drinkHoldSeconds) : 0f;
        public bool HasDrunk => _drunk;

        private bool _drunk;
        private float _mouthTimer;
        private int _grabCount;
        private int _dropCount;

        protected override string CurrentItem => _glass != null ? _glass.ItemId : "glass";
        protected override Vector3 CurrentTarget => _coaster != null ? _coaster.transform.position : Vector3.zero;

        protected override void Awake()
        {
            base.Awake();
            if (_mouth == null) _mouth = FindAnyObjectByType<MouthZone>();
            if (_mouth == null) _mouth = gameObject.AddComponent<MouthZone>();
            if (_glass == null || _coaster == null)
                Debug.LogError("[ReHand] GraspDrinkExercise: assign the glass (GraspObject) and the coaster (PlacementSpot).", this);
        }

        protected virtual void OnEnable()
        {
            if (_glass == null) return;
            _glass.Grabbed += HandleGrabbed;
            _glass.Released += HandleReleased;
        }

        protected virtual void OnDisable()
        {
            if (_glass == null) return;
            _glass.Grabbed -= HandleGrabbed;
            _glass.Released -= HandleReleased;
        }

        protected override void OnExerciseStart()
        {
            // No grabbing between repetitions: only while one is active.
            _glass.SetGrabbable(false);
        }

        protected override void OnRepetitionBegin(int repIndex)
        {
            _drunk = !_requireDrink;
            _mouthTimer = 0f;
            _grabCount = 0;
            _dropCount = 0;

            _glass.MoveTo(_coaster.Pose, instant: repIndex == 0);
            _glass.SetGrabbable(true);
            OnRefill.Invoke();
        }

        protected override void OnRepetitionUpdate()
        {
            if (!_glass.IsHeld) return;

            if (!_drunk)
            {
                if (_mouth.Contains(_glass.UsePoint))
                {
                    _mouthTimer += Time.deltaTime;
                    if (_mouthTimer >= _drinkHoldSeconds)
                    {
                        _drunk = true;
                        LogRepEvent("drink", details: $"t={RepetitionElapsed:F2}");
                        Feedback?.PlayDrink(_glass.UsePoint);
                        OnDrink.Invoke();
                    }
                }
                else
                {
                    _mouthTimer = 0f;
                }
            }

            // Show the coaster highlight once the glass may be put back.
            _coaster.SetHighlight(_drunk && _coaster.CanSnap(_glass.transform.position));
        }

        private void HandleGrabbed()
        {
            if (!IsRepetitionActive) return;
            _grabCount++;
            LogRepEvent("grab", details: $"n={_grabCount} t={RepetitionElapsed:F2}");
            Feedback?.PlayGrab(_glass.transform.position);
        }

        private void HandleReleased()
        {
            if (!IsRepetitionActive) return;
            _coaster.SetHighlight(false);
            _mouthTimer = 0f;

            Vector3 basePos = _glass.transform.position;
            float distance = _coaster.DistanceTo(basePos);
            bool onCoaster = _coaster.CanSnap(basePos);
            LogRepEvent("release", details: $"dist={distance:F3} on_coaster={onCoaster} drunk={_drunk} t={RepetitionElapsed:F2}");

            if (onCoaster && _drunk)
            {
                _glass.MoveTo(_coaster.Pose);
                Feedback?.PlaySuccess(_coaster.transform.position + Vector3.up * 0.1f);
                CompleteRepetition(true, CurrentItem, CurrentTarget, distance, $"grabs={_grabCount} drops={_dropCount}");
            }
            else if (onCoaster)
            {
                // Put back before drinking: no penalty, it stays on the coaster and can be picked up again.
                _glass.MoveTo(_coaster.Pose);
            }
            else
            {
                _dropCount++;
                Feedback?.PlayMiss(basePos);
                _glass.MoveTo(_coaster.Pose);
            }
        }

        protected override void OnRepetitionTimeout()
        {
            float distance = _coaster.DistanceTo(_glass.transform.position);
            Feedback?.PlayMiss(_glass.transform.position);
            CompleteRepetition(false, CurrentItem, CurrentTarget, distance,
                $"timeout drunk={_drunk} grabs={_grabCount} drops={_dropCount}");
        }

        protected override void OnRepetitionEnd(bool success)
        {
            _coaster.SetHighlight(false);
            _glass.SetGrabbable(false); // also drops it if still held (timeout / stop)
            if (!success) _glass.MoveTo(_coaster.Pose);
        }

        protected override void OnPauseChanged(bool paused)
        {
            if (IsRepetitionActive) _glass.SetGrabbable(!paused);
        }

        protected override void OnExerciseEnd()
        {
            _coaster.SetHighlight(false);
            _glass.SetGrabbable(true); // free to play with once the exercise is over (nothing is logged)
        }
    }
}
