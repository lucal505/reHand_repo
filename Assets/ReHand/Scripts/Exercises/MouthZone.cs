using UnityEngine;

namespace ReHand.Exercises
{
    /// <summary>
    /// The participant's mouth, estimated from the headset: a point below and slightly in front of the eyes.
    /// Used by the drinking task ("bring the glass to your mouth").
    /// </summary>
    public class MouthZone : MonoBehaviour
    {
        [Tooltip("Head (CenterEyeAnchor). Found on the OVRCameraRig automatically if empty.")]
        [SerializeField] private Transform _head;

        [Tooltip("Mouth position relative to the eyes (head space, metres): down and a little forward.")]
        [SerializeField] private Vector3 _offsetFromEyes = new Vector3(0f, -0.09f, 0.05f);

        [Tooltip("How close the glass rim must come to the mouth (m). Generous on purpose: patients may have limited range.")]
        [SerializeField, Min(0.01f)] private float _radius = 0.10f;

        public float Radius { get => _radius; set => _radius = Mathf.Max(0.01f, value); }

        public Vector3 Position
        {
            get
            {
                EnsureHead();
                return _head != null ? _head.TransformPoint(_offsetFromEyes) : transform.position;
            }
        }

        public bool Contains(Vector3 point) => Vector3.Distance(point, Position) <= _radius;

        public float DistanceTo(Vector3 point) => Vector3.Distance(point, Position);

        private void EnsureHead()
        {
            if (_head != null) return;
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) _head = rig.centerEyeAnchor;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.5f, 0.8f);
            Gizmos.DrawWireSphere(Position, _radius);
        }
    }
}
