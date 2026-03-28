using System.Collections;
using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Combat
{
    public abstract class MonsterCombatActor : MonoBehaviour
    {
        [SerializeField] private MonsterDefinition definition;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Color defaultColor = Color.white;

        private CombatDuelRoot duelRoot;
        private Coroutine activeMoveRoutine;

        public MonsterDefinition Definition => definition;
        protected CombatDuelRoot DuelRoot => duelRoot;

        public void InitializeCombat(CombatDuelRoot root)
        {
            duelRoot = root;
            TryAssignRenderer();
            ApplyColor(defaultColor);
        }

        public virtual void ResetCombatState()
        {
            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
                activeMoveRoutine = null;
            }

            if (duelRoot != null)
            {
                transform.position = duelRoot.GetMonsterBaseWorldPosition();
            }

            ApplyColor(defaultColor);
            duelRoot?.RefreshActorFacing();
        }

        public void HandleChartEvent(ChartEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            if (eventData.Phase == CombatEventPhase.Ready)
            {
                HandleReady(eventData);
                return;
            }

            HandleTiming(eventData);
        }

        public void HandleResolvedStepStart(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep == null)
            {
                return;
            }

            if (resolvedStep.Phase == CombatEventPhase.Ready)
            {
                HandleResolvedReady(resolvedStep);
                return;
            }

            HandleResolvedTiming(resolvedStep);
        }

        protected abstract void HandleReady(ChartEventData eventData);
        protected abstract void HandleTiming(ChartEventData eventData);
        protected abstract void HandleResolvedReady(ResolvedCombatStep resolvedStep);
        protected abstract void HandleResolvedTiming(ResolvedCombatStep resolvedStep);
        protected virtual void OnMoveCompleted() { }

        protected void SetVisualColor(Color color)
        {
            ApplyColor(color);
        }

        protected void RestoreDefaultColor()
        {
            ApplyColor(defaultColor);
        }

        protected void MoveForwardUnits(float units, float durationSec)
        {
            if (duelRoot == null)
            {
                return;
            }

            Vector3 targetPosition = duelRoot.GetMonsterAdvanceWorldPosition(units);
            StartMove(targetPosition, durationSec);
        }

        protected void MoveToLockedPlayerPosition(float durationSec)
        {
            if (duelRoot == null)
            {
                return;
            }

            Vector3 targetPosition = duelRoot.GetLockedPlayerWorldPositionForAttack();
            StartMove(targetPosition, durationSec);
        }

        protected void MoveToWorldPosition(Vector3 targetPosition, float durationSec)
        {
            StartMove(targetPosition, durationSec);
        }

        protected void SnapToBasePosition()
        {
            if (duelRoot == null)
            {
                return;
            }

            if (activeMoveRoutine != null)
            {
                StopCoroutine(activeMoveRoutine);
                activeMoveRoutine = null;
            }

            transform.position = duelRoot.GetMonsterBaseWorldPosition();
            duelRoot.RefreshActorFacing();
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
            duelRoot?.RefreshActorFacing();
            OnMoveCompleted();
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
            TryAssignRenderer();
        }
    }
}
