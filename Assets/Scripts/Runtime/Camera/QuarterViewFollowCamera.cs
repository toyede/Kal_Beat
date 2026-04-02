using KalBeat.Combat;
using UnityEngine;

namespace KalBeat.CameraSystem
{
    public sealed class QuarterViewFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PlayerCombatActor trackedPlayerActor;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 6f, -8f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private float positionLerpSpeed = 10f;
        [SerializeField] private float rotationLerpSpeed = 10f;
        [SerializeField] private float rotationStartDelaySec = 0.08f;
        [SerializeField] private float rotationCompleteAngleDeg = 1f;
        [SerializeField] private bool snapOnEnable = true;

        private Vector3 currentPlanarForward = Vector3.forward;
        private bool wasTargetMoving;
        private bool isRotatingToTargetForward;
        private float rotationStartDelayRemaining;

        private void OnEnable()
        {
            TryAssignTrackedPlayerActor();
            RefreshPlanarForward(true);
            if (snapOnEnable)
            {
                SnapToTarget();
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            bool isTargetMoving = trackedPlayerActor != null && trackedPlayerActor.IsMoving;
            if (wasTargetMoving && !isTargetMoving)
            {
                rotationStartDelayRemaining = rotationStartDelaySec;
            }

            wasTargetMoving = isTargetMoving;
            UpdateRotationState(isTargetMoving);
            RefreshPlanarForward(isTargetMoving);

            Vector3 desiredPosition = GetDesiredWorldPosition();
            float positionT = 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionT);

            if (isRotatingToTargetForward)
            {
                Quaternion desiredRotation = GetDesiredWorldRotation(desiredPosition);
                float rotationT = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
            }
        }

        public void SetTarget(Transform nextTarget, bool snapImmediately = true)
        {
            target = nextTarget;
            TryAssignTrackedPlayerActor();
            RefreshPlanarForward(true);
            if (snapImmediately)
            {
                SnapToTarget();
            }
        }

        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            RefreshPlanarForward(true);
            Vector3 desiredPosition = GetDesiredWorldPosition();
            transform.position = desiredPosition;
            transform.rotation = GetDesiredWorldRotation(desiredPosition);
            isRotatingToTargetForward = false;
            rotationStartDelayRemaining = 0f;
            wasTargetMoving = trackedPlayerActor != null && trackedPlayerActor.IsMoving;
        }

        private Vector3 GetDesiredWorldPosition()
        {
            Vector3 planarRight = GetCurrentPlanarRight();
            return target.position +
                   (planarRight * followOffset.x) +
                   (Vector3.up * followOffset.y) +
                   (currentPlanarForward * followOffset.z);
        }

        private Quaternion GetDesiredWorldRotation(Vector3 cameraPosition)
        {
            Vector3 planarRight = GetCurrentPlanarRight();
            Vector3 lookTarget = target.position +
                                 (planarRight * lookOffset.x) +
                                 (Vector3.up * lookOffset.y) +
                                 (currentPlanarForward * lookOffset.z);
            Vector3 lookDirection = lookTarget - cameraPosition;
            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                return transform.rotation;
            }

            return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }

        private void RefreshPlanarForward(bool lockRotationBecauseTargetMoving)
        {
            if (target == null)
            {
                return;
            }

            if (lockRotationBecauseTargetMoving)
            {
                return;
            }

            if (!isRotatingToTargetForward)
            {
                return;
            }

            Vector3 desiredPlanarForward = GetTargetPlanarForward();
            if (desiredPlanarForward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (currentPlanarForward.sqrMagnitude <= 0.0001f)
            {
                currentPlanarForward = desiredPlanarForward;
            }
            else
            {
                float yawT = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);
                currentPlanarForward = Vector3.Slerp(currentPlanarForward, desiredPlanarForward, yawT).normalized;
            }

            if (Vector3.Angle(currentPlanarForward, desiredPlanarForward) <= rotationCompleteAngleDeg)
            {
                currentPlanarForward = desiredPlanarForward;
                isRotatingToTargetForward = false;
            }
        }

        private Vector3 GetTargetPlanarForward()
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            if (planarForward.sqrMagnitude > 0.0001f)
            {
                return planarForward;
            }

            return currentPlanarForward.sqrMagnitude > 0.0001f ? currentPlanarForward : Vector3.forward;
        }

        private Vector3 GetCurrentPlanarRight()
        {
            Vector3 planarRight = Vector3.Cross(Vector3.up, currentPlanarForward).normalized;
            if (planarRight.sqrMagnitude > 0.0001f)
            {
                return planarRight;
            }

            return Vector3.right;
        }

        private void TryAssignTrackedPlayerActor()
        {
            if (trackedPlayerActor == null && target != null)
            {
                trackedPlayerActor = target.GetComponent<PlayerCombatActor>();
            }
        }

        private void UpdateRotationState(bool isTargetMoving)
        {
            if (isTargetMoving)
            {
                rotationStartDelayRemaining = 0f;
                isRotatingToTargetForward = false;
                return;
            }

            if (rotationStartDelayRemaining <= 0f)
            {
                return;
            }

            rotationStartDelayRemaining = Mathf.Max(0f, rotationStartDelayRemaining - Time.deltaTime);
            if (rotationStartDelayRemaining <= 0f)
            {
                isRotatingToTargetForward = true;
            }
        }

        private void OnValidate()
        {
            positionLerpSpeed = Mathf.Max(0f, positionLerpSpeed);
            rotationLerpSpeed = Mathf.Max(0f, rotationLerpSpeed);
            rotationStartDelaySec = Mathf.Max(0f, rotationStartDelaySec);
            rotationCompleteAngleDeg = Mathf.Clamp(rotationCompleteAngleDeg, 0.1f, 45f);
        }
    }
}
