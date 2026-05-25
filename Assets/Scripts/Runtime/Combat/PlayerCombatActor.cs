using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Combat
{
    public sealed class PlayerCombatActor : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color defaultColor = Color.white;
        [SerializeField] private Color perfectColor = new Color(0.35f, 0.6f, 0.95f, 1f);
        [SerializeField] private Color goodColor = new Color(0.35f, 0.85f, 0.45f, 1f);
        [SerializeField] private Color hitColor = new Color(0.95f, 0.35f, 0.35f, 1f);
        [SerializeField] private float defaultMoveDurationSec = 0.18f;
        [SerializeField] private float missArcHeightBase = 0.35f;
        [SerializeField] private float missArcHeightPerUnit = 0.18f;
        [SerializeField] private float missArcHeightMin = 0.35f;
        [SerializeField] private float missArcHeightMax = 1.4f;
        [SerializeField] private Transform shootPoint;
        [SerializeField] private Animator visualAnimator;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private string attackTriggerName = "Attack";
        [SerializeField] private string dodgeTriggerName = "Dodge";
        [SerializeField] private string hitTriggerName = "Hit";
        [SerializeField] private float dodgeVisualYawDegrees = 90f;
        [SerializeField] private float dodgeVisualRestoreDelaySec = 1.5f;
        [SerializeField] private AnimationCurve missArcCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 0f));

        private CombatDuelRoot duelRoot;
        private CombatRhythmClock rhythmClock;
        private Coroutine activeMoveRoutine;
        private int attackTriggerHash;
        private int dodgeTriggerHash;
        private int hitTriggerHash;
        private bool hasVisualDefaultLocalRotation;
        private Quaternion visualDefaultLocalRotation = Quaternion.identity;
        private Coroutine activeVisualFacingRoutine;
        private readonly List<GameObject> activeRangedProjectiles = new List<GameObject>();
        private readonly List<Coroutine> activeRangedProjectileRoutines = new List<Coroutine>();

        public bool IsMoving => activeMoveRoutine != null;
        public Transform ShootPoint
        {
            get
            {
                TryAssignShootPoint();
                return shootPoint;
            }
        }

        public void InitializeCombat(CombatDuelRoot root, CombatRhythmClock clock = null)
        {
            duelRoot = root;
            rhythmClock = clock;
            TryAssignRenderer();
            TryAssignShootPoint();
            TryAssignAnimator();
            TryAssignVisualRoot();
            CacheVisualDefaultLocalRotation();
            RefreshAnimatorHashes();
            ApplyColor(defaultColor);
        }

        public void ResetCombatState()
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
                activeMoveRoutine = null;
            }

            DestroyActiveRangedProjectiles();
            RestoreVisualFacingImmediate();

            if (duelRoot != null)
            {
                transform.position = duelRoot.GetPlayerBaseWorldPosition();
            }

            ApplyColor(defaultColor);
            duelRoot?.RefreshActorFacing();
        }

        public void PlayAttackAnimation()
        {
            TryAssignAnimator();
            TryAssignVisualRoot();
            RefreshAnimatorHashes();
            RestoreVisualFacingImmediate();
            PlayVisualTrigger(attackTriggerHash);
        }

        public void PlayDodgeAnimation(PlayerRelativeDirection directionHint)
        {
            TryAssignAnimator();
            TryAssignVisualRoot();
            CacheVisualDefaultLocalRotation();
            RefreshAnimatorHashes();
            ApplyDodgeVisualFacing(directionHint);
            PlayVisualTrigger(dodgeTriggerHash);
            StartVisualFacingRestore();
        }

        public void PlayHitAnimation()
        {
            TryAssignAnimator();
            TryAssignVisualRoot();
            RefreshAnimatorHashes();
            RestoreVisualFacingImmediate();
            PlayVisualTrigger(hitTriggerHash);
        }

        public void FireRangedAttack(PlayerAttackDefinition attackDefinition, Transform targetPoint)
        {
            if (attackDefinition == null)
            {
                Debug.LogWarning("Player ranged attack definition is missing.", this);
                return;
            }

            if (attackDefinition.ProjectilePrefab == null)
            {
                Debug.LogWarning($"Player attack '{attackDefinition.DisplayName}' is missing ProjectilePrefab.", this);
                return;
            }

            Transform resolvedShootPoint = ShootPoint;
            if (resolvedShootPoint == null)
            {
                Debug.LogWarning($"{name} is missing ShootPoint. Add a child GameObject named ShootPoint under the player.", this);
                return;
            }

            if (targetPoint == null)
            {
                Debug.LogWarning("Player ranged attack target is missing. Add a child GameObject named HitPoint under the monster.", this);
                return;
            }

            Vector3 spawnPosition = resolvedShootPoint.position;
            Vector3 contactPosition = targetPoint.position;
            Vector3 direction = contactPosition - spawnPosition;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                Debug.LogWarning("Player ranged attack has no travel distance.", this);
                return;
            }

            direction.Normalize();
            GameObject projectileObject = Instantiate(attackDefinition.ProjectilePrefab, spawnPosition, Quaternion.identity);
            projectileObject.transform.forward = direction;

            Transform frontTip = FindChildByName(projectileObject.transform, attackDefinition.ProjectileFrontTipName);
            if (frontTip == null)
            {
                Debug.LogWarning(
                    $"Player projectile prefab '{attackDefinition.ProjectilePrefab.name}' must contain '{attackDefinition.ProjectileFrontTipName}'.",
                    this);
                Destroy(projectileObject);
                return;
            }

            float frontOffsetWorld = Vector3.Dot(frontTip.position - projectileObject.transform.position, direction);
            if (frontOffsetWorld <= 0.0001f)
            {
                Debug.LogWarning(
                    $"Player projectile prefab '{attackDefinition.ProjectilePrefab.name}' FrontTip must be placed in front of the projectile root.",
                    this);
                Destroy(projectileObject);
                return;
            }

            Vector3 targetPivotPosition = contactPosition - (direction * frontOffsetWorld);
            float unitSize = duelRoot != null ? Mathf.Max(0.0001f, duelRoot.UnitSize) : 1f;
            float travelUnits = Vector3.Distance(spawnPosition, targetPivotPosition) / unitSize;
            float durationSec = Mathf.Max(0.0001f, attackDefinition.ResolveTravelDurationSec(travelUnits));
            float startSec = rhythmClock != null && rhythmClock.HasTimelineTime ? rhythmClock.NowSec : 0f;
            Coroutine projectileRoutine = StartCoroutine(RangedProjectileRoutine(projectileObject, spawnPosition, targetPivotPosition, startSec, durationSec));
            activeRangedProjectiles.Add(projectileObject);
            activeRangedProjectileRoutines.Add(projectileRoutine);
        }

        public void PlayHitKnockback(PlayerRelativeDirection directionHint, float moveUnits, float durationSec = -1f)
        {
            if (duelRoot == null)
            {
                return;
            }

            float resolvedDuration = durationSec > 0f ? durationSec : defaultMoveDurationSec;
            Vector3 targetPosition = duelRoot.GetPlayerResolvedWorldPosition(directionHint, moveUnits);
            ApplyColor(hitColor);
            StartMove(targetPosition, resolvedDuration);
        }

        public void PlayResolvedHitMove(Vector2 targetLocalPosition, float durationSec)
        {
            PlayResolvedReactionMove(targetLocalPosition, durationSec, CombatJudgementGrade.Miss);
        }

        public void PlayResolvedReactionMove(Vector2 targetLocalPosition, float durationSec, CombatJudgementGrade grade)
        {
            float startSec = rhythmClock != null && rhythmClock.HasTimelineTime ? rhythmClock.NowSec : 0f;
            PlayResolvedReactionMove(targetLocalPosition, startSec, durationSec, grade);
        }

        public void PlayResolvedReactionMove(Vector2 targetLocalPosition, float startSec, float durationSec, CombatJudgementGrade grade)
        {
            if (duelRoot == null)
            {
                return;
            }

            ApplyColor(GetReactionColor(grade));
            Vector3 targetWorldPosition = duelRoot.ConvertDuelLocalToWorld(targetLocalPosition);
            float resolvedDuration = durationSec > 0f ? durationSec : defaultMoveDurationSec;
            float missArcHeight = grade == CombatJudgementGrade.Miss
                ? ResolveMissArcHeight(transform.position, targetWorldPosition)
                : 0f;

            StartMove(targetWorldPosition, startSec, resolvedDuration, missArcHeight);
        }

        public void PlayResolvedApproachMove(Vector2 targetLocalPosition, float durationSec)
        {
            float startSec = rhythmClock != null && rhythmClock.HasTimelineTime ? rhythmClock.NowSec : 0f;
            PlayResolvedApproachMove(targetLocalPosition, startSec, durationSec);
        }

        public void PlayResolvedApproachMove(Vector2 targetLocalPosition, float startSec, float durationSec)
        {
            if (duelRoot == null)
            {
                return;
            }

            ApplyColor(defaultColor);
            StartMove(
                duelRoot.ConvertDuelLocalToWorld(targetLocalPosition),
                startSec,
                durationSec > 0f ? durationSec : defaultMoveDurationSec,
                0f);
        }

        private void StartMove(Vector3 targetPosition, float durationSec, float arcHeight = 0f)
        {
            float startSec = rhythmClock != null && rhythmClock.HasTimelineTime ? rhythmClock.NowSec : 0f;
            StartMove(targetPosition, startSec, durationSec, arcHeight);
        }

        private void StartMove(Vector3 targetPosition, float startSec, float durationSec, float arcHeight = 0f)
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
            }

            activeMoveRoutine = StartCoroutine(MoveRoutine(targetPosition, startSec, durationSec, arcHeight));
        }

        private IEnumerator RangedProjectileRoutine(
            GameObject projectileObject,
            Vector3 startPosition,
            Vector3 targetPosition,
            float startSec,
            float durationSec)
        {
            float elapsed = 0f;
            while (true)
            {
                if (projectileObject == null)
                {
                    yield break;
                }

                float t;
                if (rhythmClock != null && rhythmClock.HasTimelineTime)
                {
                    t = Mathf.Clamp01((rhythmClock.NowSec - startSec) / durationSec);
                }
                else
                {
                    elapsed += Time.deltaTime;
                    t = Mathf.Clamp01(elapsed / durationSec);
                }

                projectileObject.transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                if (t >= 1f)
                {
                    break;
                }

                yield return null;
            }

            if (projectileObject != null)
            {
                projectileObject.transform.position = targetPosition;
                activeRangedProjectiles.Remove(projectileObject);
                Destroy(projectileObject);
            }
        }

        private IEnumerator MoveRoutine(Vector3 targetPosition, float startSec, float durationSec, float arcHeight)
        {
            Vector3 startPosition = transform.position;
            float safeDuration = Mathf.Max(0.0001f, durationSec);
            float elapsed = 0f;
            Vector3 arcUp = duelRoot != null ? duelRoot.transform.up : Vector3.up;

            while (true)
            {
                float t;
                if (rhythmClock != null && rhythmClock.HasTimelineTime)
                {
                    t = Mathf.Clamp01((rhythmClock.NowSec - startSec) / safeDuration);
                }
                else
                {
                    elapsed += Time.deltaTime;
                    t = Mathf.Clamp01(elapsed / safeDuration);
                }

                Vector3 movePosition = Vector3.Lerp(startPosition, targetPosition, t);
                if (arcHeight > 0f)
                {
                    // 미스 연출은 도착점은 유지하되 상하 오프셋만 더해 피격 포물선처럼 보이게 만든다.
                    float arcOffset = Mathf.Max(0f, missArcCurve.Evaluate(t)) * arcHeight;
                    movePosition += arcUp * arcOffset;
                }

                transform.position = movePosition;
                if (t >= 1f)
                {
                    break;
                }

                yield return null;
            }

            transform.position = targetPosition;
            ApplyColor(defaultColor);
            duelRoot?.RefreshActorFacing();
            activeMoveRoutine = null;
        }

        private float ResolveMissArcHeight(Vector3 startPosition, Vector3 targetPosition)
        {
            Vector3 arcUp = duelRoot != null ? duelRoot.transform.up : Vector3.up;
            float travelDistance = Vector3.ProjectOnPlane(targetPosition - startPosition, arcUp).magnitude;
            float resolvedArcHeight = missArcHeightBase + (travelDistance * missArcHeightPerUnit);
            return Mathf.Clamp(resolvedArcHeight, missArcHeightMin, missArcHeightMax);
        }

        private void DestroyActiveRangedProjectiles()
        {
            for (int i = 0; i < activeRangedProjectileRoutines.Count; i++)
            {
                Coroutine routine = activeRangedProjectileRoutines[i];
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            activeRangedProjectileRoutines.Clear();

            for (int i = 0; i < activeRangedProjectiles.Count; i++)
            {
                GameObject projectileObject = activeRangedProjectiles[i];
                if (projectileObject != null)
                {
                    Destroy(projectileObject);
                }
            }

            activeRangedProjectiles.Clear();
        }

        private void TryAssignRenderer()
        {
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }
        }

        private void TryAssignShootPoint()
        {
            if (shootPoint == null)
            {
                Transform foundPoint = transform.Find("ShootPoint");
                if (foundPoint != null)
                {
                    shootPoint = foundPoint;
                }
            }
        }

        private void TryAssignAnimator()
        {
            if (visualAnimator == null)
            {
                visualAnimator = GetComponentInChildren<Animator>();
            }
        }

        private void TryAssignVisualRoot()
        {
            if (visualRoot == null && visualAnimator != null)
            {
                visualRoot = visualAnimator.transform;
            }
        }

        private void CacheVisualDefaultLocalRotation()
        {
            if (!hasVisualDefaultLocalRotation && visualRoot != null)
            {
                visualDefaultLocalRotation = visualRoot.localRotation;
                hasVisualDefaultLocalRotation = true;
            }
        }

        private void RefreshAnimatorHashes()
        {
            attackTriggerHash = string.IsNullOrWhiteSpace(attackTriggerName)
                ? 0
                : Animator.StringToHash(attackTriggerName);
            dodgeTriggerHash = string.IsNullOrWhiteSpace(dodgeTriggerName)
                ? 0
                : Animator.StringToHash(dodgeTriggerName);
            hitTriggerHash = string.IsNullOrWhiteSpace(hitTriggerName)
                ? 0
                : Animator.StringToHash(hitTriggerName);
        }

        private void PlayVisualTrigger(int triggerHash)
        {
            if (visualAnimator == null || triggerHash == 0)
            {
                return;
            }

            ResetVisualTriggers();
            visualAnimator.SetTrigger(triggerHash);
        }

        private void ResetVisualTriggers()
        {
            ResetVisualTrigger(attackTriggerHash);
            ResetVisualTrigger(dodgeTriggerHash);
            ResetVisualTrigger(hitTriggerHash);
        }

        private void ResetVisualTrigger(int triggerHash)
        {
            if (triggerHash != 0)
            {
                visualAnimator.ResetTrigger(triggerHash);
            }
        }

        private void ApplyDodgeVisualFacing(PlayerRelativeDirection directionHint)
        {
            if (visualRoot == null)
            {
                return;
            }

            StopVisualFacingRestore();
            float yawSign = directionHint == PlayerRelativeDirection.Left ? -1f : 1f;
            visualRoot.localRotation = visualDefaultLocalRotation *
                Quaternion.Euler(0f, yawSign * dodgeVisualYawDegrees, 0f);
        }

        private void StartVisualFacingRestore()
        {
            if (visualRoot == null || dodgeVisualRestoreDelaySec <= 0f)
            {
                return;
            }

            activeVisualFacingRoutine = StartCoroutine(RestoreVisualFacingRoutine());
        }

        private IEnumerator RestoreVisualFacingRoutine()
        {
            yield return new WaitForSeconds(dodgeVisualRestoreDelaySec);
            activeVisualFacingRoutine = null;
            RestoreVisualFacingRotation();
        }

        private void RestoreVisualFacingImmediate()
        {
            StopVisualFacingRestore();
            RestoreVisualFacingRotation();
        }

        private void RestoreVisualFacingRotation()
        {
            if (visualRoot != null && hasVisualDefaultLocalRotation)
            {
                visualRoot.localRotation = visualDefaultLocalRotation;
            }
        }

        private void StopVisualFacingRestore()
        {
            if (activeVisualFacingRoutine != null)
            {
                StopCoroutine(activeVisualFacingRoutine);
                activeVisualFacingRoutine = null;
            }
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && string.Equals(children[i].name, childName, System.StringComparison.Ordinal))
                {
                    return children[i];
                }
            }

            return null;
        }

        private void ApplyColor(Color color)
        {
            if (bodyRenderer == null)
            {
                return;
            }

            bodyRenderer.material.color = color;
        }

        private Color GetReactionColor(CombatJudgementGrade grade)
        {
            switch (grade)
            {
                case CombatJudgementGrade.Perfect:
                    return perfectColor;
                case CombatJudgementGrade.Good:
                    return goodColor;
                case CombatJudgementGrade.Miss:
                    return hitColor;
                default:
                    return defaultColor;
            }
        }

        private void OnValidate()
        {
            defaultMoveDurationSec = Mathf.Max(0.01f, defaultMoveDurationSec);
            missArcHeightBase = Mathf.Max(0f, missArcHeightBase);
            missArcHeightPerUnit = Mathf.Max(0f, missArcHeightPerUnit);
            missArcHeightMin = Mathf.Max(0f, missArcHeightMin);
            missArcHeightMax = Mathf.Max(missArcHeightMin, missArcHeightMax);
            dodgeVisualYawDegrees = Mathf.Max(0f, dodgeVisualYawDegrees);
            dodgeVisualRestoreDelaySec = Mathf.Max(0f, dodgeVisualRestoreDelaySec);
            missArcCurve ??= new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 0f));
            TryAssignRenderer();
            TryAssignShootPoint();
            TryAssignAnimator();
            TryAssignVisualRoot();
            CacheVisualDefaultLocalRotation();
            RefreshAnimatorHashes();
        }
    }
}
