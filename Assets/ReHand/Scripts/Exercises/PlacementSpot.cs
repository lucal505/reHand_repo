using UnityEngine;

namespace ReHand.Exercises
{
    /// <summary>
    /// A marked spot an object has to be placed on (coaster, plate, shelf spot…).
    /// Its transform is the pose the object snaps to: position = where the object's pivot
    /// (its base) ends up, rotation = how it stands.
    /// </summary>
    public class PlacementSpot : MonoBehaviour
    {
        [Tooltip("Name written to the logs, e.g. 'coaster'.")]
        [SerializeField] private string _spotId = "spot";

        [Tooltip("Horizontal distance within which a released object snaps onto the spot (m).")]
        [SerializeField, Min(0.005f)] private float _snapRadius = 0.07f;

        [Tooltip("How far above (or below) the spot the object may be released and still snap (m).")]
        [SerializeField, Min(0.005f)] private float _snapHeight = 0.12f;

        [Tooltip("Optional object shown while the held object is close enough to snap (e.g. a glowing ring).")]
        [SerializeField] private GameObject _highlight;

        public string SpotId => _spotId;
        public float SnapRadius { get => _snapRadius; set => _snapRadius = Mathf.Max(0.005f, value); }
        public Pose Pose => new Pose(transform.position, transform.rotation);

        /// <summary>Straight-line distance from a point (the object's pivot) to the spot, in metres.</summary>
        public float DistanceTo(Vector3 point) => Vector3.Distance(point, transform.position);

        /// <summary>True if an object released with its pivot at <paramref name="point"/> should snap.</summary>
        public bool CanSnap(Vector3 point)
        {
            Vector3 d = point - transform.position;
            float vertical = Vector3.Dot(d, transform.up);
            Vector3 horizontal = d - transform.up * vertical;
            return horizontal.magnitude <= _snapRadius && Mathf.Abs(vertical) <= _snapHeight;
        }

        public void SetHighlight(bool on)
        {
            if (_highlight != null && _highlight.activeSelf != on) _highlight.SetActive(on);
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(1f, 0.001f, 1f));
            Gizmos.DrawWireSphere(Vector3.zero, _snapRadius);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
