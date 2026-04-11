using System.Collections;
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
        [SerializeField] private AnimationCurve missArcCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.35f, 1f),
            new Keyframe(1f, 0f));

        private CombatDuelRoot duelRoot;
        private Coroutine activeMoveRoutine;

        public bool IsMoving => activeMoveRoutine != null;

        public void InitializeCombat(CombatDuelRoot root)
        {
            duelRoot = root;
            TryAssignRenderer();
            ApplyColor(defaultColor);
        }

        public void ResetCombatState()
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
                activeMoveRoutine = null;
            }

            if (duelRoot != null)
            {
                transform.position = duelRoot.GetPlayerBaseWorldPosition();
            }

            ApplyColor(defaultColor);
            duelRoot?.RefreshActorFacing();
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

            StartMove(targetWorldPosition, resolvedDuration, missArcHeight);
        }

        public void PlayResolvedApproachMove(Vector2 targetLocalPosition, float durationSec)
        {
            if (duelRoot == null)
            {
                return;
            }

            ApplyColor(defaultColor);
            StartMove(
                duelRoot.ConvertDuelLocalToWorld(targetLocalPosition),
                durationSec > 0f ? durationSec : defaultMoveDurationSec,
                0f);
        }

        private void StartMove(Vector3 targetPosition, float durationSec, float arcHeight = 0f)
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
            }

            activeMoveRoutine = StartCoroutine(MoveRoutine(targetPosition, durationSec, arcHeight));
        }

        private IEnumerator MoveRoutine(Vector3 targetPosition, float durationSec, float arcHeight)
        {
            Vector3 startPosition = transform.position;
            float safeDuration = Mathf.Max(0.0001f, durationSec);
            float elapsed = 0f;
            Vector3 arcUp = duelRoot != null ? duelRoot.transform.up : Vector3.up;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                Vector3 movePosition = Vector3.Lerp(startPosition, targetPosition, t);
                if (arcHeight > 0f)
                {
                    // 미스 연출은 도착점은 유지하되 상하 오프셋만 더해 피격 포물선처럼 보이게 만든다.
                    float arcOffset = Mathf.Max(0f, missArcCurve.Evaluate(t)) * arcHeight;
                    movePosition += arcUp * arcOffset;
                }

                transform.position = movePosition;
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

        private void TryAssignRenderer()
        {
            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<Renderer>();
            }
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
            missArcCurve ??= new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 0f));
            TryAssignRenderer();
        }
    }
}
