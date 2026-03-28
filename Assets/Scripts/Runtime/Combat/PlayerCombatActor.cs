using System.Collections;
using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Combat
{
    public sealed class PlayerCombatActor : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color defaultColor = Color.white;
        [SerializeField] private Color hitColor = new Color(0.95f, 0.35f, 0.35f, 1f);
        [SerializeField] private float defaultMoveDurationSec = 0.18f;

        private CombatDuelRoot duelRoot;
        private Coroutine activeMoveRoutine;

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
            if (duelRoot == null)
            {
                return;
            }

            ApplyColor(hitColor);
            StartMove(duelRoot.ConvertDuelLocalToWorld(targetLocalPosition), durationSec > 0f ? durationSec : defaultMoveDurationSec);
        }

        private void StartMove(Vector3 targetPosition, float durationSec)
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
            }

            activeMoveRoutine = StartCoroutine(MoveRoutine(targetPosition, durationSec));
        }

        private IEnumerator MoveRoutine(Vector3 targetPosition, float durationSec)
        {
            Vector3 startPosition = transform.position;
            float safeDuration = Mathf.Max(0.0001f, durationSec);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                yield return null;
            }

            transform.position = targetPosition;
            ApplyColor(defaultColor);
            duelRoot?.RefreshActorFacing();
            activeMoveRoutine = null;
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

        private void OnValidate()
        {
            defaultMoveDurationSec = Mathf.Max(0.01f, defaultMoveDurationSec);
            TryAssignRenderer();
        }
    }
}
