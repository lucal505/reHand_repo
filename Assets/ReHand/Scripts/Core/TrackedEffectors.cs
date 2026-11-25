using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace ReHand.Core
{
    /// <summary>Position of one effector (index fingertip or controller) in one frame.</summary>
    public struct EffectorSample
    {
        /// <summary>"hand", "controller" or "none".</summary>
        public string Source;
        public bool Valid;
        public bool HighConfidence;
        /// <summary>World-space position.</summary>
        public Vector3 WorldPosition;
        /// <summary>Position relative to the rig's tracking space (unaffected by teleports).</summary>
        public Vector3 TrackingPosition;
    }

    /// <summary>
    /// Single place to read the head, hands and controllers from the Interaction SDK rig.
    /// The effector is the index fingertip for hands and the controller pose for controllers.
    /// If the references are left empty it finds them under OVRCameraRig automatically.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class TrackedEffectors : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig _cameraRig;

        [SerializeField, Interface(typeof(IHand))] private Object _leftHand;
        [SerializeField, Interface(typeof(IHand))] private Object _rightHand;
        [SerializeField, Interface(typeof(IController))] private Object _leftController;
        [SerializeField, Interface(typeof(IController))] private Object _rightController;

        [Tooltip("Offset from the controller pose to the point used as the 'controller tip', in controller space.")]
        [SerializeField] private Vector3 _controllerTipOffset = Vector3.zero;

        public OVRCameraRig CameraRig => _cameraRig;
        public IHand LeftHand { get; private set; }
        public IHand RightHand { get; private set; }
        public IController LeftController { get; private set; }
        public IController RightController { get; private set; }

        public Transform Head => _cameraRig != null ? _cameraRig.centerEyeAnchor : null;
        public Transform TrackingSpace => _cameraRig != null ? _cameraRig.trackingSpace : null;

        protected virtual void Awake()
        {
            if (_cameraRig == null) _cameraRig = FindAnyObjectByType<OVRCameraRig>();

            LeftHand = _leftHand as IHand ?? FindOnSide<IHand>("Left");
            RightHand = _rightHand as IHand ?? FindOnSide<IHand>("Right");
            LeftController = _leftController as IController ?? FindOnSide<IController>("Left");
            RightController = _rightController as IController ?? FindOnSide<IController>("Right");

            if (_cameraRig == null) Debug.LogError("[ReHand] TrackedEffectors: no OVRCameraRig in the scene.", this);
            if (LeftHand == null || RightHand == null || LeftController == null || RightController == null)
                Debug.LogWarning("[ReHand] TrackedEffectors: some hand/controller references were not found; assign them in the Inspector.", this);
        }

        /// <summary>Head pose in tracking space.</summary>
        public Pose GetHeadTrackingPose()
        {
            var head = Head;
            if (head == null) return Pose.identity;
            return new Pose(head.localPosition, head.localRotation);
        }

        /// <summary>
        /// Effector for one side. Uses the controller when <paramref name="mode"/> is Controllers,
        /// the hand when it is Hands, and whichever is valid (hand first) when it is None.
        /// </summary>
        public EffectorSample GetEffector(bool isLeft, InputMode mode)
        {
            IHand hand = isLeft ? LeftHand : RightHand;
            IController controller = isLeft ? LeftController : RightController;

            if (mode != InputMode.Controllers && TryGetHandSample(hand, out var handSample)) return handSample;
            if (mode != InputMode.Hands && TryGetControllerSample(controller, out var controllerSample)) return controllerSample;

            return new EffectorSample { Source = "none" };
        }

        private bool TryGetHandSample(IHand hand, out EffectorSample sample)
        {
            sample = default;
            if (hand == null || !hand.IsTrackedDataValid) return false;
            if (!hand.GetJointPose(HandJointId.HandIndexTip, out Pose tip)) return false;

            sample = new EffectorSample
            {
                Source = "hand",
                Valid = true,
                HighConfidence = hand.IsHighConfidence,
                WorldPosition = tip.position,
                TrackingPosition = ToTracking(tip.position),
            };
            return true;
        }

        private bool TryGetControllerSample(IController controller, out EffectorSample sample)
        {
            sample = default;
            if (controller == null || !controller.IsConnected || !controller.TryGetPose(out Pose pose)) return false;

            Vector3 tip = pose.position + pose.rotation * _controllerTipOffset;
            sample = new EffectorSample
            {
                Source = "controller",
                Valid = controller.IsPoseValid,
                HighConfidence = true,
                WorldPosition = tip,
                TrackingPosition = ToTracking(tip),
            };
            return true;
        }

        private Vector3 ToTracking(Vector3 world)
        {
            var space = TrackingSpace;
            return space != null ? space.InverseTransformPoint(world) : world;
        }

        // The Interaction SDK rig puts the per-side Hand/Controller on
        // OVRComprehensiveInteractionRig/ComprehensiveInteractorsLeft|Right.
        private T FindOnSide<T>(string side) where T : class
        {
            if (_cameraRig == null) return null;
            foreach (Transform t in _cameraRig.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "ComprehensiveInteractors" + side) continue;
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                {
                    if (mb is T found) return found;
                }
            }
            return null;
        }
    }
}
