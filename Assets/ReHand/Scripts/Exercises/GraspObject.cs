using System;
using System.Collections;
using Oculus.Interaction;
using UnityEngine;

namespace ReHand.Exercises
{
    /// <summary>
    /// Wraps an Interaction SDK Grabbable (made with "Add Grab Interaction") for the exercises:
    /// reports grab / release, can switch grabbing on and off, and moves the object back to a pose.
    /// Put it on the same GameObject as the Grabbable.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class GraspObject : MonoBehaviour
    {
        [Tooltip("Name written to the logs, e.g. 'glass_water'.")]
        [SerializeField] private string _itemId = "object";

        [Tooltip("Point used to check 'at the mouth' (e.g. the glass rim), in this object's local space.")]
        [SerializeField] private Vector3 _usePointLocal = new Vector3(0f, 0.18f, 0f);

        [Tooltip("Seconds to glide back when returned to a pose (0 = jump).")]
        [SerializeField, Min(0f)] private float _returnDuration = 0.6f;

        public string ItemId => _itemId;
        public bool IsHeld => _grabbable != null && _grabbable.SelectingPointsCount > 0;
        public bool IsMoving => _moveRoutine != null;
        public Vector3 UsePoint => transform.TransformPoint(_usePointLocal);

        public event Action Grabbed;
        public event Action Released;

        private Grabbable _grabbable;
        private Rigidbody _rigidbody;
        private MonoBehaviour[] _interactables;
        private Coroutine _moveRoutine;
        private bool _wasHeld;

        protected virtual void Awake()
        {
            _grabbable = GetComponent<Grabbable>();
            _rigidbody = GetComponent<Rigidbody>();
            // HandGrabInteractable / GrabInteractable / DistanceGrabInteractable… created by the grab wizard.
            var found = new System.Collections.Generic.List<MonoBehaviour>();
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IInteractable) found.Add(mb);
            _interactables = found.ToArray();
        }

        protected virtual void OnEnable() => _grabbable.WhenPointerEventRaised += HandlePointerEvent;
        protected virtual void OnDisable() => _grabbable.WhenPointerEventRaised -= HandlePointerEvent;

        /// <summary>Allows or blocks grabbing. Blocking also drops the object if it is held.</summary>
        public void SetGrabbable(bool canGrab)
        {
            foreach (var mb in _interactables) if (mb != null) mb.enabled = canGrab;
        }

        /// <summary>Moves the object to a pose (glides unless instant). Ignored while held.</summary>
        public void MoveTo(Pose pose, bool instant = false)
        {
            if (_moveRoutine != null) StopCoroutine(_moveRoutine);
            _moveRoutine = null;
            if (instant || _returnDuration <= 0f || !isActiveAndEnabled)
            {
                SetPose(pose);
                return;
            }
            _moveRoutine = StartCoroutine(Glide(pose));
        }

        private IEnumerator Glide(Pose target)
        {
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            bool wasKinematic = _rigidbody != null && _rigidbody.isKinematic;
            if (_rigidbody != null) _rigidbody.isKinematic = true;

            for (float t = 0f; t < 1f; t += Time.deltaTime / _returnDuration)
            {
                if (IsHeld) break; // the user grabbed it mid-way: let go of it
                float k = Mathf.SmoothStep(0f, 1f, t);
                SetPose(new Pose(Vector3.Lerp(startPos, target.position, k), Quaternion.Slerp(startRot, target.rotation, k)));
                yield return null;
            }
            if (!IsHeld) SetPose(target);

            if (_rigidbody != null) _rigidbody.isKinematic = wasKinematic;
            _moveRoutine = null;
        }

        private void SetPose(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (_rigidbody == null) return;
            _rigidbody.position = pose.position;
            _rigidbody.rotation = pose.rotation;
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            bool held = IsHeld;
            if (held == _wasHeld) return;
            _wasHeld = held;

            if (held)
            {
                if (_moveRoutine != null) { StopCoroutine(_moveRoutine); _moveRoutine = null; }
                Grabbed?.Invoke();
            }
            else
            {
                Released?.Invoke();
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(_usePointLocal), 0.015f);
        }
    }
}
