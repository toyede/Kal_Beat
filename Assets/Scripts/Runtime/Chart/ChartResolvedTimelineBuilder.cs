using System;
using System.Collections.Generic;
using System.Text;
using KalBeat.Combat;
using UnityEngine;

namespace KalBeat.Charting
{
    public static class ChartResolvedTimelineBuilder
    {
        private const float DefaultMeleeDodgeResolveUnits = 4f;

        public static string BuildSourceSignature(ChartData chart)
        {
            if (chart == null || chart.Meta == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(2048);
            ChartMeta meta = chart.Meta;
            builder.Append(meta.SongId).Append('|')
                .Append(meta.SongName).Append('|')
                .Append(meta.FmodEventPath).Append('|')
                .Append(meta.Bpm.ToString("R")).Append('|')
                .Append(meta.OffsetSec.ToString("R")).Append('|')
                .Append(meta.SongLengthSec.ToString("R")).Append('|')
                .Append(meta.GridDivision).Append('|')
                .Append(meta.LaneCount).Append('|')
                .Append(meta.MaxEventsPerSlot).Append('|');

            List<LaneBindingData> laneBindings = meta.LaneBindings;
            if (laneBindings != null)
            {
                for (int i = 0; i < laneBindings.Count; i++)
                {
                    LaneBindingData laneBinding = laneBindings[i];
                    if (laneBinding == null)
                    {
                        builder.Append("lane:null|");
                        continue;
                    }

                    builder.Append(i).Append(':')
                        .Append((int)laneBinding.BindingType).Append(':')
                        .Append(laneBinding.BindingId)
                        .Append('|');
                }
            }

            if (chart.Events != null)
            {
                for (int i = 0; i < chart.Events.Count; i++)
                {
                    ChartEventData eventData = chart.Events[i];
                    if (eventData == null)
                    {
                        builder.Append("event:null|");
                        continue;
                    }

                    builder.Append(eventData.Beat.ToString("R")).Append(':')
                        .Append(eventData.Lane).Append(':')
                        .Append((int)eventData.Kind).Append(':')
                        .Append((int)eventData.Phase).Append(':')
                        .Append(eventData.StepIndex).Append(':')
                        .Append((int)eventData.ReadyCueType).Append(':')
                        .Append((int)eventData.DirectionHint).Append(':')
                        .Append(eventData.NudgeMs.ToString("R")).Append(':')
                        .Append((int)eventData.MovementResolveMode).Append(':')
                        .Append(eventData.MoveUnits.ToString("R")).Append(':')
                        .Append(eventData.TargetBandUnits.ToString("R")).Append(':')
                        .Append((int)eventData.MovementDirectionHint).Append(':')
                        .Append(eventData.EntryBandUnits.ToString("R")).Append(':')
                        .Append(eventData.PlayerKnockbackUnits.ToString("R")).Append(':')
                        .Append(eventData.MonsterKnockbackUnits.ToString("R"))
                        .Append('|');
                }
            }

            return Hash128.Compute(builder.ToString()).ToString();
        }

        public static bool TryGenerate(
            ChartData chart,
            Func<string, MonsterDefinition> monsterResolver,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (chart == null)
            {
                errorMessage = "ChartData is missing.";
                return false;
            }

            if (chart.ResolvedTimeline == null)
            {
                errorMessage = "ResolvedTimeline container is missing.";
                return false;
            }

            string sourceSignature = BuildSourceSignature(chart);
            if (chart.Events == null || chart.Meta == null)
            {
                chart.ResolvedTimeline.SetInvalid("Chart data is incomplete.");
                errorMessage = chart.ResolvedTimeline.InvalidReason;
                return false;
            }

            List<ChartEventData> orderedEvents = BuildOrderedEvents(chart.Events);
            if (orderedEvents.Count == 0)
            {
                chart.ResolvedTimeline.SetResolvedSteps(sourceSignature, new List<ResolvedCombatStep>());
                return true;
            }

            int activeLane = GetSingleActiveLane(orderedEvents, out errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                chart.ResolvedTimeline.SetInvalid(errorMessage);
                return false;
            }

            LaneBindingData laneBinding = GetLaneBinding(chart.Meta, activeLane);
            if (laneBinding == null || laneBinding.BindingType != LaneBindingType.Monster || string.IsNullOrWhiteSpace(laneBinding.BindingId))
            {
                errorMessage = $"Lane {activeLane + 1} must be bound to a Monster to build ResolvedTimeline.";
                chart.ResolvedTimeline.SetInvalid(errorMessage);
                return false;
            }

            MonsterDefinition monsterDefinition = monsterResolver != null ? monsterResolver(laneBinding.BindingId) : null;
            if (monsterDefinition == null)
            {
                errorMessage = $"MonsterDefinition '{laneBinding.BindingId}' could not be resolved.";
                chart.ResolvedTimeline.SetInvalid(errorMessage);
                return false;
            }

            string monsterSignature = monsterDefinition.BuildRuntimeSignature();
            List<ResolvedCombatStep> resolvedSteps = new List<ResolvedCombatStep>(orderedEvents.Count);

            bool stateInitialized = false;
            Vector2 currentPlayerLocalPosition = Vector2.zero;
            Vector2 currentMonsterLocalPosition = Vector2.zero;

            for (int i = 0; i < orderedEvents.Count; i++)
            {
                ChartEventData eventData = orderedEvents[i];
                if (eventData == null || eventData.Kind == CombatEventKind.None)
                {
                    continue;
                }

                if (!stateInitialized)
                {
                    if (eventData.StepIndex != 0)
                    {
                        errorMessage = "The first runtime event must start at StepIndex 0.";
                        chart.ResolvedTimeline.SetInvalid(errorMessage);
                        return false;
                    }

                    if (eventData.EntryBandUnits <= 0f)
                    {
                        errorMessage = "The first pattern sentence must define EntryBandUnits greater than 0.";
                        chart.ResolvedTimeline.SetInvalid(errorMessage);
                        return false;
                    }

                    currentMonsterLocalPosition = new Vector2(0f, eventData.EntryBandUnits);
                    stateInitialized = true;
                }

                float currentBandUnits = CombatDuelMath.GetBandUnits(currentPlayerLocalPosition, currentMonsterLocalPosition);
                if (eventData.StepIndex == 0 && !Mathf.Approximately(eventData.EntryBandUnits, currentBandUnits))
                {
                    errorMessage =
                        $"EntryBandUnits mismatch at beat {eventData.Beat:0.###}. Expected {currentBandUnits:0.###}, but event requires {eventData.EntryBandUnits:0.###}.";
                    chart.ResolvedTimeline.SetInvalid(errorMessage);
                    return false;
                }

                float hitSec = GetEventTimeSec(chart.Meta, eventData);
                ResolvedCombatStep resolvedStep = new ResolvedCombatStep
                {
                    MonsterId = monsterDefinition.MonsterId,
                    MonsterDefinitionSignature = monsterSignature,
                    SourceBeat = eventData.Beat,
                    Lane = eventData.Lane,
                    Kind = eventData.Kind,
                    Phase = eventData.Phase,
                    StepIndex = eventData.StepIndex,
                    ReadyCueType = eventData.ReadyCueType,
                    PlayerDirectionHint = eventData.DirectionHint,
                    MovementResolveMode = eventData.MovementResolveMode,
                    MoveUnits = eventData.MoveUnits,
                    TargetBandUnits = eventData.TargetBandUnits,
                    MovementDirectionHint = eventData.MovementDirectionHint,
                    HitSec = hitSec,
                    StartPlayerLocalPos = currentPlayerLocalPosition,
                    StartMonsterLocalPos = currentMonsterLocalPosition,
                    LockedPlayerLocalPos = currentPlayerLocalPosition,
                    MonsterApproachLocalPos = currentMonsterLocalPosition,
                    EntryBandUnits = currentBandUnits,
                    PlayerKnockbackUnits = eventData.PlayerKnockbackUnits,
                    MonsterKnockbackUnits = eventData.MonsterKnockbackUnits
                };

                Vector2 endPlayerLocalPosition = currentPlayerLocalPosition;
                Vector2 endMonsterLocalPosition = currentMonsterLocalPosition;
                float monsterDurationSec = 0f;
                float playerStartSec = hitSec;
                float playerDurationSec = 0f;

                switch (eventData.Phase)
                {
                    case CombatEventPhase.Ready:
                        if (eventData.ReadyCueType == ReadyCueType.Move)
                        {
                            if (!monsterDefinition.TryGetReadyMoveDurationSec(eventData.MoveUnits, out monsterDurationSec))
                            {
                                errorMessage =
                                    $"Monster '{monsterDefinition.DisplayName}' does not define ReadyMoveDuration for {eventData.MoveUnits:0.###} units.";
                                chart.ResolvedTimeline.SetInvalid(errorMessage);
                                return false;
                            }

                            if (!CombatDuelMath.TryResolveReadyMoveTarget(
                                    currentPlayerLocalPosition,
                                    currentMonsterLocalPosition,
                                    eventData.MovementResolveMode,
                                    eventData.MoveUnits,
                                    eventData.TargetBandUnits,
                                    eventData.MovementDirectionHint,
                                    out endMonsterLocalPosition))
                            {
                                errorMessage =
                                    $"Ready Move target could not be resolved at beat {eventData.Beat:0.###} on lane {eventData.Lane + 1}.";
                                chart.ResolvedTimeline.SetInvalid(errorMessage);
                                return false;
                            }
                        }

                        break;

                    case CombatEventPhase.Timing:
                        if (eventData.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                            eventData.Kind != CombatEventKind.MeleeThreatCounterable)
                        {
                            errorMessage =
                                $"ResolvedTimeline currently supports only {CombatEventKind.MeleeThreatDodgeOnly} and {CombatEventKind.MeleeThreatCounterable}.";
                            chart.ResolvedTimeline.SetInvalid(errorMessage);
                            return false;
                        }

                        Vector2 approachTargetLocalPosition = ResolveTimingApproachTarget(
                            currentPlayerLocalPosition,
                            currentMonsterLocalPosition,
                            eventData.TargetBandUnits);
                        float approachUnits = CombatDuelMath.GetBandUnits(currentMonsterLocalPosition, approachTargetLocalPosition);
                        if (!monsterDefinition.TryGetTimingApproachDurationSec(approachUnits, out monsterDurationSec))
                        {
                            errorMessage =
                                $"Monster '{monsterDefinition.DisplayName}' does not define TimingApproachDuration for {approachUnits:0.###} units.";
                            chart.ResolvedTimeline.SetInvalid(errorMessage);
                            return false;
                        }

                        resolvedStep.MonsterApproachLocalPos = approachTargetLocalPosition;

                        if (eventData.Kind == CombatEventKind.MeleeThreatDodgeOnly)
                        {
                            endMonsterLocalPosition = approachTargetLocalPosition;
                            endPlayerLocalPosition = CombatDuelMath.ResolvePlayerDirectionTarget(
                                currentPlayerLocalPosition,
                                currentMonsterLocalPosition,
                                eventData.DirectionHint == PlayerRelativeDirection.None ? PlayerRelativeDirection.Right : eventData.DirectionHint,
                                DefaultMeleeDodgeResolveUnits);
                        }
                        else
                        {
                            CombatDuelMath.ResolveCounterKnockbackTargets(
                                currentPlayerLocalPosition,
                                approachTargetLocalPosition,
                                eventData.PlayerKnockbackUnits,
                                eventData.MonsterKnockbackUnits,
                                out endPlayerLocalPosition,
                                out endMonsterLocalPosition);
                        }

                        playerDurationSec = monsterDurationSec;
                        break;

                    default:
                        errorMessage = $"Unsupported phase {eventData.Phase}.";
                        chart.ResolvedTimeline.SetInvalid(errorMessage);
                        return false;
                }

                resolvedStep.MonsterDurationSec = monsterDurationSec;
                resolvedStep.MonsterStartSec = hitSec - monsterDurationSec;
                resolvedStep.PlayerStartSec = playerStartSec;
                resolvedStep.PlayerDurationSec = playerDurationSec;
                resolvedStep.EndPlayerLocalPos = endPlayerLocalPosition;
                resolvedStep.EndMonsterLocalPos = endMonsterLocalPosition;
                resolvedStep.ExitBandUnits = CombatDuelMath.GetBandUnits(endPlayerLocalPosition, endMonsterLocalPosition);

                if (resolvedStep.MonsterStartSec < 0f)
                {
                    errorMessage =
                        $"Step at beat {eventData.Beat:0.###} must start before time 0. Increase chart offset or reduce movement duration.";
                    chart.ResolvedTimeline.SetInvalid(errorMessage);
                    return false;
                }

                resolvedSteps.Add(resolvedStep);
                currentPlayerLocalPosition = endPlayerLocalPosition;
                currentMonsterLocalPosition = endMonsterLocalPosition;
            }

            chart.ResolvedTimeline.SetResolvedSteps(sourceSignature, resolvedSteps);
            return true;
        }

        private static float GetEventTimeSec(ChartMeta meta, ChartEventData eventData)
        {
            float beatDurationSec = 60f / meta.Bpm;
            return meta.OffsetSec + (eventData.Beat * beatDurationSec) + (eventData.NudgeMs * 0.001f);
        }

        private static Vector2 ResolveTimingApproachTarget(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            float targetBandUnits)
        {
            if (targetBandUnits <= 0f)
            {
                return playerLocalPosition;
            }

            return CombatDuelMath.ResolveForwardToBand(playerLocalPosition, monsterLocalPosition, targetBandUnits);
        }

        private static List<ChartEventData> BuildOrderedEvents(List<ChartEventData> sourceEvents)
        {
            List<ChartEventData> orderedEvents = new List<ChartEventData>(sourceEvents.Count);
            for (int i = 0; i < sourceEvents.Count; i++)
            {
                ChartEventData eventData = sourceEvents[i];
                if (eventData != null && eventData.Kind != CombatEventKind.None)
                {
                    orderedEvents.Add(eventData);
                }
            }

            orderedEvents.Sort(CompareEvents);
            return orderedEvents;
        }

        private static int GetSingleActiveLane(List<ChartEventData> orderedEvents, out string errorMessage)
        {
            errorMessage = string.Empty;
            int activeLane = -1;

            for (int i = 0; i < orderedEvents.Count; i++)
            {
                ChartEventData eventData = orderedEvents[i];
                if (eventData == null || eventData.Kind == CombatEventKind.None)
                {
                    continue;
                }

                if (activeLane < 0)
                {
                    activeLane = eventData.Lane;
                    continue;
                }

                if (activeLane != eventData.Lane)
                {
                    errorMessage = "ResolvedTimeline currently supports a single active monster lane.";
                    return -1;
                }
            }

            return activeLane;
        }

        private static LaneBindingData GetLaneBinding(ChartMeta meta, int lane)
        {
            if (meta == null || meta.LaneBindings == null || lane < 0 || lane >= meta.LaneBindings.Count)
            {
                return null;
            }

            return meta.LaneBindings[lane];
        }

        private static int CompareEvents(ChartEventData left, ChartEventData right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int beatCompare = left.Beat.CompareTo(right.Beat);
            if (beatCompare != 0)
            {
                return beatCompare;
            }

            int laneCompare = left.Lane.CompareTo(right.Lane);
            if (laneCompare != 0)
            {
                return laneCompare;
            }

            int phaseCompare = left.Phase.CompareTo(right.Phase);
            if (phaseCompare != 0)
            {
                return phaseCompare;
            }

            return left.StepIndex.CompareTo(right.StepIndex);
        }
    }
}
