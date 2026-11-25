using System;
using System.Collections;
using Oculus.Interaction.Locomotion;
using UnityEngine;

namespace ReHand.Core
{
    /// <summary>
    /// Moves the player so their real head ends up on a SeatAnchor, facing its direction,
    /// whatever spot of the play area they are actually sitting in.
    ///
    /// Runs at start (once head tracking is up), whenever the user recenters the view
    /// (long-press of the Meta button), and whenever AlignTo is called (StationManager will call
    /// it after each teleport). It goes through the rig's FirstPersonLocomotor as an absolute
    /// teleport, so the Interaction SDK locomotion stays in sync; without one it moves the rig directly.
    /// </summary>
    public class SeatAligner : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig _cameraRig;
        [Tooltip("Optional; found under the rig automatically.")]
        [SerializeField] private FirstPersonLocomotor _locomotor;

        [SerializeField] private SeatAnchor _startSeat;
        [SerializeField] private bool _alignOnStart = true;
        [SerializeField] private bool _realignOnRecenter = true;

        [Tooltip("How long to wait for a valid head pose at start before aligning anyway (e.g. in the Editor without a headset).")]
        [SerializeField, Min(0f)] private float _headWaitTimeout = 3f;

        // Identifies our locomotion events (the Interaction SDK only uses it to tell sources apart).
        private const int LocomotionEventId = 0x5EA7;

        public SeatAnchor CurrentSeat { get; private set; }
        public event Action<SeatAnchor> Aligned;

        private bool _subscribedToRecenter;

        protected virtual void Awake()
        {
            if (_cameraRig == null) _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            if (_locomotor == null && _cameraRig != null) _locomotor = _cameraRig.GetComponentInChildren<FirstPersonLocomotor>(true);
        }

        protected virtual IEnumerator Start()
        {
            TrySubscribeRecenter();
            if (!_alignOnStart || _startSeat == null) yield break;

            // Wait until the headset reports a head pose (localPosition stays zero until tracking starts).
            float deadline = Time.unscaledTime + _headWaitTimeout;
            while (Time.unscaledTime < deadline && !HasHeadPose()) yield return null;
            // Let the locomotor settle its character on the first tracked frames.
            yield return null;
            yield return null;

            TrySubscribeRecenter();
            AlignTo(_startSeat);
        }

        protected virtual void OnDestroy()
        {
            if (_subscribedToRecenter && OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        /// <summary>Moves the player onto the seat. Also sets the current station in SessionContext.</summary>
        public void AlignTo(SeatAnchor seat)
        {
            if (seat == null || _cameraRig == null) return;
            CurrentSeat = seat;
            Pose target = seat.FloorPose;

            if (_locomotor != null && _locomotor.isActiveAndEnabled)
            {
                // Absolute feet position + absolute facing: the locomotor puts the character (which follows
                // the head) on the target and rotates the player around it.
                _locomotor.HandleLocomotionEvent(new LocomotionEvent(LocomotionEventId, target,
                    LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.Absolute));
            }
            else
            {
                AlignRigDirectly(target);
            }

            SessionContext.SetStation(seat.StationId);
            DataLogger.Instance?.LogEvent("seat_aligned", item: seat.StationId,
                details: $"seat=({target.position.x:F3};{target.position.y:F3};{target.position.z:F3}) yaw={target.rotation.eulerAngles.y:F1}");
            Aligned?.Invoke(seat);
        }

        private void AlignRigDirectly(Pose target)
        {
            Transform rig = _cameraRig.transform;
            Transform head = _cameraRig.centerEyeAnchor;

            // Rotate around the head so it faces the seat direction (yaw only).
            Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (headForward.sqrMagnitude > 1e-6f)
            {
                float yaw = Vector3.SignedAngle(headForward, target.rotation * Vector3.forward, Vector3.up);
                rig.RotateAround(head.position, Vector3.up, yaw);
            }

            // Put the head above the seat; keep the rig origin (the real floor) on the seat's floor height.
            Vector3 offset = target.position - head.position;
            offset.y = 0f;
            rig.position += offset;
            rig.position = new Vector3(rig.position.x, target.position.y, rig.position.z);
        }

        private bool HasHeadPose() =>
            _cameraRig != null && _cameraRig.centerEyeAnchor != null
            && _cameraRig.centerEyeAnchor.localPosition.sqrMagnitude > 1e-6f;

        private void TrySubscribeRecenter()
        {
            if (_subscribedToRecenter || !_realignOnRecenter || OVRManager.display == null) return;
            OVRManager.display.RecenteredPose += OnRecentered;
            _subscribedToRecenter = true;
        }

        private void OnRecentered()
        {
            if (CurrentSeat != null) StartCoroutine(RealignNextFrame());
        }

        private IEnumerator RealignNextFrame()
        {
            yield return null; // the new head pose is applied on the next frame
            AlignTo(CurrentSeat);
        }
    }
}
