using UnityEngine;

namespace ReHand.Core
{
    /// <summary>
    /// Marks where the participant sits at a station: place it on the floor where the
    /// participant's head should be (seen from above) and rotate it so its blue arrow
    /// (forward, Z) points at the table / exercise area. SeatAligner moves the player here.
    /// </summary>
    public class SeatAnchor : MonoBehaviour
    {
        [Tooltip("Station identifier written to the logs, e.g. 'dining_table', 'kitchen', 'hallway'.")]
        [SerializeField] private string _stationId = "station";

        [Tooltip("Typical seated eye height, used only for the gizmo.")]
        [SerializeField] private float _gizmoEyeHeight = 1.2f;

        public string StationId => _stationId;

        /// <summary>Floor position under the head, facing direction flattened to the horizontal plane.</summary>
        public Pose FloorPose
        {
            get
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
                return new Pose(transform.position, Quaternion.LookRotation(forward.normalized, Vector3.up));
            }
        }

        protected virtual void OnDrawGizmos()
        {
            Pose p = FloorPose;
            Vector3 eye = p.position + Vector3.up * _gizmoEyeHeight;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(p.position, 0.25f);
            Gizmos.DrawLine(p.position, eye);
            Gizmos.DrawWireSphere(eye, 0.1f);

            Vector3 f = p.rotation * Vector3.forward;
            Vector3 tip = eye + f * 0.5f;
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(eye, tip);
            Gizmos.DrawLine(tip, tip - f * 0.12f + p.rotation * Vector3.right * 0.06f);
            Gizmos.DrawLine(tip, tip - f * 0.12f - p.rotation * Vector3.right * 0.06f);
        }
    }
}
