using UnityEngine;

namespace ReHand.Core
{
    /// <summary>
    /// Records the head and both effectors (index fingertip or controller) every frame
    /// into the trajectory CSV, for the movement-smoothness analysis.
    /// Runs late in the frame so the poses are the ones that were rendered.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class TrajectoryRecorder : MonoBehaviour
    {
        [SerializeField] private TrackedEffectors _effectors;

        [Tooltip("Record continuously while a session is active. Turn off to record only when an exercise calls SetRecording(true).")]
        [SerializeField] private bool _recordAlways = true;

        private bool _recordingRequested;

        protected virtual void Awake()
        {
            if (_effectors == null) _effectors = FindAnyObjectByType<TrackedEffectors>();
        }

        /// <summary>Starts/stops recording when "record always" is off.</summary>
        public void SetRecording(bool recording) => _recordingRequested = recording;

        protected virtual void LateUpdate()
        {
            var logger = DataLogger.Instance;
            if (logger == null || !logger.IsSessionActive || _effectors == null) return;
            if (!_recordAlways && !_recordingRequested) return;

            InputMode mode = SessionContext.ActiveMode;
            var sample = new FrameSample
            {
                Head = _effectors.GetHeadTrackingPose(),
                Left = _effectors.GetEffector(isLeft: true, mode),
                Right = _effectors.GetEffector(isLeft: false, mode),
            };
            logger.LogFrame(sample);
        }
    }
}
