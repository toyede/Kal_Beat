using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Combat
{
    public sealed class BoarMonsterActor : MonsterCombatActor
    {
        [SerializeField] private Color poseColor = new Color(0.95f, 0.65f, 0.2f, 1f);
        [SerializeField] private Color dashColor = new Color(0.95f, 0.3f, 0.3f, 1f);
        [SerializeField] private float dashUnits = 4f;
        [SerializeField] private float dashDurationSec = 0.18f;

        protected override void HandleReady(ChartEventData eventData)
        {
            if (eventData.Kind == CombatEventKind.PlayerDashAttack)
            {
                ShowReadyMark();
                return;
            }

            if (eventData.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                eventData.Kind != CombatEventKind.MeleeThreatCounterable)
            {
                return;
            }

            if (eventData.ReadyCueType == ReadyCueType.Pose)
            {
                SetVisualColor(poseColor);
            }
        }

        protected override void HandleTiming(ChartEventData eventData)
        {
            if (eventData.Kind == CombatEventKind.PlayerDashAttack)
            {
                return;
            }

            if (eventData.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                eventData.Kind != CombatEventKind.MeleeThreatCounterable)
            {
                return;
            }

            SetVisualColor(dashColor);
            MoveToLockedPlayerPosition(dashDurationSec);
        }

        protected override void HandleResolvedReady(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep.Kind == CombatEventKind.PlayerDashAttack)
            {
                ShowReadyMark();
                return;
            }

            if (resolvedStep.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                resolvedStep.Kind != CombatEventKind.MeleeThreatCounterable)
            {
                return;
            }

            switch (resolvedStep.ReadyCueType)
            {
                case ReadyCueType.Pose:
                    SetVisualColor(poseColor);
                    break;

                case ReadyCueType.Move:
                    RestoreDefaultColor();
                    if (DuelRoot != null)
                    {
                        MoveToWorldPosition(DuelRoot.ConvertDuelLocalToWorld(resolvedStep.EndMonsterLocalPos), resolvedStep.MonsterDurationSec);
                    }
                    break;
            }
        }

        protected override void HandleResolvedTiming(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep.Kind == CombatEventKind.PlayerDashAttack)
            {
                return;
            }

            if (resolvedStep.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                resolvedStep.Kind != CombatEventKind.MeleeThreatCounterable)
            {
                return;
            }

            SetVisualColor(dashColor);
            if (DuelRoot != null)
            {
                Vector2 targetLocalPosition = resolvedStep.Kind == CombatEventKind.MeleeThreatCounterable
                    ? resolvedStep.MonsterApproachLocalPos
                    : resolvedStep.EndMonsterLocalPos;
                MoveToWorldPosition(DuelRoot.ConvertDuelLocalToWorld(targetLocalPosition), resolvedStep.MonsterDurationSec);
            }
        }

        protected override void HandleResolvedJudgementResult(ResolvedCombatStep resolvedStep, CombatJudgementGrade grade)
        {
            if ((resolvedStep.Kind != CombatEventKind.MeleeThreatCounterable &&
                 resolvedStep.Kind != CombatEventKind.PlayerDashAttack) ||
                DuelRoot == null)
            {
                return;
            }

            SetVisualColor(dashColor);
            float reactionDurationSec = resolvedStep.MonsterDurationSec > 0f ? resolvedStep.MonsterDurationSec : dashDurationSec;
            MoveToWorldPosition(DuelRoot.ConvertDuelLocalToWorld(resolvedStep.EndMonsterLocalPos), reactionDurationSec);
        }

        protected override void OnMoveCompleted()
        {
            RestoreDefaultColor();
        }

        public override void ResetCombatState()
        {
            base.ResetCombatState();
            RestoreDefaultColor();
        }

        private void OnValidate()
        {
            dashUnits = Mathf.Max(0f, dashUnits);
            dashDurationSec = Mathf.Max(0.01f, dashDurationSec);
        }
    }
}
