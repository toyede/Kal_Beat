using System;
using System.Collections.Generic;
using System.Text;
using KalBeat.Combat;
using UnityEngine;

namespace KalBeat.Charting
{
    public static class ChartResolvedTimelineBuilder
    {
        private const string ResolverVersion = "ranged-dodge-pattern-v1";
        private const float DefaultMeleeDodgeResolveUnits = 4f;
        private const float DefaultRangedDodgeMoveUnits = 4f;
        private const float DefaultRangedDodgeBandUnits = 4f;
        private const float DefaultRangedResponseOffsetBeats = 4f;

        public static string BuildSourceSignature(ChartData chart)
        {
            if (chart == null || chart.Meta == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(2048);
            ChartMeta meta = chart.Meta;
            builder.Append(ResolverVersion).Append('|')
                .Append(meta.SongId).Append('|')
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
                        .Append(eventData.MonsterKnockbackUnits.ToString("R")).Append(':')
                        .Append(eventData.AppliedPatternAssetGuid).Append(':')
                        .Append(eventData.AppliedPatternId).Append(':')
                        .Append(eventData.AppliedPatternSignature);

                    if (eventData.Kind == CombatEventKind.RangedThreatDodgeOnly)
                    {
                        builder.Append(':')
                            .Append(eventData.RangedPhraseId).Append(':')
                            .Append(eventData.RangedCueIndex).Append(':')
                            .Append(eventData.RangedAnchorIndex);
                    }

                    builder.Append('|');
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
            Dictionary<string, RangedPhraseTiming> rangedPhraseTimings = BuildRangedPhraseTimings(orderedEvents);
            Dictionary<string, RangedPhraseFrame> rangedPhraseFrames =
                new Dictionary<string, RangedPhraseFrame>(StringComparer.Ordinal);

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
                    PlayerApproachLocalPos = currentPlayerLocalPosition,
                    LockedPlayerLocalPos = currentPlayerLocalPosition,
                    MonsterApproachLocalPos = currentMonsterLocalPosition,
                    EntryBandUnits = currentBandUnits,
                    PlayerKnockbackUnits = eventData.PlayerKnockbackUnits,
                    MonsterKnockbackUnits = eventData.MonsterKnockbackUnits,
                    RangedPhraseId = eventData.RangedPhraseId,
                    RangedCueIndex = eventData.RangedCueIndex,
                    RangedAnchorIndex = eventData.RangedAnchorIndex
                };

                Vector2 endPlayerLocalPosition = currentPlayerLocalPosition;
                Vector2 endMonsterLocalPosition = currentMonsterLocalPosition;
                float monsterDurationSec = 0f;
                float playerStartSec = hitSec;
                float playerDurationSec = 0f;
                RangedDodgeSettings rangedDodgeSettings = ResolveRangedDodgeSettings(monsterDefinition, eventData);
                RangedPhraseFrame rangedPhraseFrame = default;
                bool isNewRangedPhraseFrame = false;
                bool hasRangedPhraseFrame = eventData.Kind == CombatEventKind.RangedThreatDodgeOnly &&
                                            TryGetRangedPhraseFrame(
                                                rangedPhraseFrames,
                                                eventData,
                                                currentPlayerLocalPosition,
                                                currentMonsterLocalPosition,
                                                rangedDodgeSettings,
                                                out isNewRangedPhraseFrame,
                                                out rangedPhraseFrame);

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
                        else if (eventData.Kind == CombatEventKind.RangedThreatDodgeOnly &&
                                 hasRangedPhraseFrame &&
                                 isNewRangedPhraseFrame &&
                                 rangedDodgeSettings.AutoMovePlayerToResponseBand)
                        {
                            endPlayerLocalPosition = rangedPhraseFrame.ResponsePlayerLocalPosition;
                            resolvedStep.PlayerApproachLocalPos = endPlayerLocalPosition;

                            if (!Approximately(currentPlayerLocalPosition, endPlayerLocalPosition))
                            {
                                playerDurationSec = ResolveRangedResponseLeadDurationSec(
                                    chart.Meta,
                                    eventData,
                                    rangedPhraseTimings,
                                    rangedDodgeSettings);
                            }
                        }

                        break;

                    case CombatEventPhase.Timing:
                        if (eventData.Kind != CombatEventKind.MeleeThreatDodgeOnly &&
                            eventData.Kind != CombatEventKind.MeleeThreatCounterable &&
                            eventData.Kind != CombatEventKind.PlayerDashAttack &&
                            eventData.Kind != CombatEventKind.RangedThreatDodgeOnly)
                        {
                            errorMessage =
                                $"ResolvedTimeline currently supports only {CombatEventKind.MeleeThreatDodgeOnly}, {CombatEventKind.MeleeThreatCounterable}, {CombatEventKind.PlayerDashAttack}, and {CombatEventKind.RangedThreatDodgeOnly}.";
                            chart.ResolvedTimeline.SetInvalid(errorMessage);
                            return false;
                        }

                        if (eventData.Kind == CombatEventKind.PlayerDashAttack)
                        {
                            Vector2 playerApproachLocalPosition = ResolvePlayerDashApproachTarget(
                                currentPlayerLocalPosition,
                                currentMonsterLocalPosition,
                                eventData.TargetBandUnits);
                            float playerApproachUnits = CombatDuelMath.GetBandUnits(currentPlayerLocalPosition, playerApproachLocalPosition);
                            if (!TryResolveApproachDurationSec(monsterDefinition, playerApproachUnits, out playerDurationSec))
                            {
                                errorMessage =
                                    $"Monster '{monsterDefinition.DisplayName}' does not define TimingApproachDuration for {playerApproachUnits:0.###} units.";
                                chart.ResolvedTimeline.SetInvalid(errorMessage);
                                return false;
                            }

                            resolvedStep.PlayerApproachLocalPos = playerApproachLocalPosition;
                            CombatDuelMath.ResolveCounterKnockbackTargets(
                                playerApproachLocalPosition,
                                currentMonsterLocalPosition,
                                eventData.PlayerKnockbackUnits,
                                eventData.MonsterKnockbackUnits,
                                out endPlayerLocalPosition,
                                out endMonsterLocalPosition);

                            playerStartSec = hitSec - playerDurationSec;
                            monsterDurationSec = playerDurationSec;
                        }
                        else if (eventData.Kind == CombatEventKind.RangedThreatDodgeOnly)
                        {
                            Vector2 referencePlayerLocalPosition = hasRangedPhraseFrame
                                ? rangedPhraseFrame.ResponsePlayerLocalPosition
                                : currentPlayerLocalPosition;
                            Vector2 referenceMonsterLocalPosition = hasRangedPhraseFrame
                                ? rangedPhraseFrame.ResponseMonsterLocalPosition
                                : currentMonsterLocalPosition;
                            float responseBandUnits = hasRangedPhraseFrame
                                ? rangedPhraseFrame.ResponseBandUnits
                                : CombatDuelMath.GetBandUnits(referencePlayerLocalPosition, referenceMonsterLocalPosition);

                            endPlayerLocalPosition = CombatDuelMath.ResolvePlayerDirectionTargetOnBand(
                                referencePlayerLocalPosition,
                                referenceMonsterLocalPosition,
                                eventData.DirectionHint == PlayerRelativeDirection.None ? PlayerRelativeDirection.Right : eventData.DirectionHint,
                                responseBandUnits,
                                rangedDodgeSettings.DodgeMoveUnits);
                        }
                        else
                        {
                            Vector2 approachTargetLocalPosition = ResolveTimingApproachTarget(
                                currentPlayerLocalPosition,
                                currentMonsterLocalPosition,
                                eventData.TargetBandUnits);
                            float approachUnits = CombatDuelMath.GetBandUnits(currentMonsterLocalPosition, approachTargetLocalPosition);
                            if (!TryResolveApproachDurationSec(monsterDefinition, approachUnits, out monsterDurationSec))
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
                        }
                        break;

                    default:
                        errorMessage = $"Unsupported phase {eventData.Phase}.";
                        chart.ResolvedTimeline.SetInvalid(errorMessage);
                        return false;
                }

                resolvedStep.MonsterDurationSec = monsterDurationSec;
                resolvedStep.MonsterStartSec = eventData.Kind == CombatEventKind.PlayerDashAttack &&
                                               eventData.Phase == CombatEventPhase.Timing
                    ? hitSec
                    : hitSec - monsterDurationSec;
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

                if (resolvedStep.PlayerStartSec < 0f)
                {
                    errorMessage =
                        $"Player action at beat {eventData.Beat:0.###} must start before time 0. Increase chart offset or reduce movement duration.";
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

        private static Vector2 ResolvePlayerDashApproachTarget(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            float targetBandUnits)
        {
            if (targetBandUnits <= 0f)
            {
                return monsterLocalPosition;
            }

            return CombatDuelMath.ResolvePlayerApproachToBand(playerLocalPosition, monsterLocalPosition, targetBandUnits);
        }

        private static bool TryResolveApproachDurationSec(MonsterDefinition monsterDefinition, float approachUnits, out float durationSec)
        {
            durationSec = 0f;
            if (approachUnits <= 0.0001f)
            {
                return true;
            }

            return monsterDefinition.TryGetTimingApproachDurationSec(approachUnits, out durationSec);
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

        private static bool TryGetRangedPhraseFrame(
            Dictionary<string, RangedPhraseFrame> rangedPhraseFrames,
            ChartEventData eventData,
            Vector2 currentPlayerLocalPosition,
            Vector2 currentMonsterLocalPosition,
            RangedDodgeSettings rangedDodgeSettings,
            out bool isNewFrame,
            out RangedPhraseFrame phraseFrame)
        {
            phraseFrame = default;
            isNewFrame = false;
            if (rangedPhraseFrames == null || eventData == null)
            {
                return false;
            }

            string key = BuildRangedPhraseFrameKey(eventData);
            if (rangedPhraseFrames.TryGetValue(key, out phraseFrame))
            {
                return true;
            }

            float currentBandUnits = CombatDuelMath.GetBandUnits(currentPlayerLocalPosition, currentMonsterLocalPosition);
            float responseBandUnits = rangedDodgeSettings.AutoMovePlayerToResponseBand
                ? rangedDodgeSettings.ResponseBandUnits
                : currentBandUnits;
            if (responseBandUnits <= 0.0001f)
            {
                responseBandUnits = DefaultRangedDodgeBandUnits;
            }

            Vector2 responsePlayerLocalPosition = ResolvePlayerOnBand(
                currentPlayerLocalPosition,
                currentMonsterLocalPosition,
                responseBandUnits);

            phraseFrame = new RangedPhraseFrame(
                responsePlayerLocalPosition,
                currentMonsterLocalPosition,
                responseBandUnits);
            rangedPhraseFrames.Add(key, phraseFrame);
            isNewFrame = true;
            return true;
        }

        private static Dictionary<string, RangedPhraseTiming> BuildRangedPhraseTimings(List<ChartEventData> orderedEvents)
        {
            Dictionary<string, RangedPhraseTiming> timings = new Dictionary<string, RangedPhraseTiming>(StringComparer.Ordinal);
            if (orderedEvents == null)
            {
                return timings;
            }

            for (int i = 0; i < orderedEvents.Count; i++)
            {
                ChartEventData eventData = orderedEvents[i];
                if (eventData == null || eventData.Kind != CombatEventKind.RangedThreatDodgeOnly)
                {
                    continue;
                }

                string key = BuildRangedPhraseFrameKey(eventData);
                if (!timings.TryGetValue(key, out RangedPhraseTiming timing))
                {
                    timing = new RangedPhraseTiming();
                }

                if (eventData.Phase == CombatEventPhase.Ready)
                {
                    if (!timing.HasFirstReadyBeat || eventData.Beat < timing.FirstReadyBeat)
                    {
                        timing.HasFirstReadyBeat = true;
                        timing.FirstReadyBeat = eventData.Beat;
                    }
                }
                else if (eventData.Phase == CombatEventPhase.Timing)
                {
                    if (!timing.HasFirstTimingBeat || eventData.Beat < timing.FirstTimingBeat)
                    {
                        timing.HasFirstTimingBeat = true;
                        timing.FirstTimingBeat = eventData.Beat;
                    }
                }

                timings[key] = timing;
            }

            return timings;
        }

        private static float ResolveRangedResponseLeadDurationSec(
            ChartMeta meta,
            ChartEventData eventData,
            Dictionary<string, RangedPhraseTiming> rangedPhraseTimings,
            RangedDodgeSettings rangedDodgeSettings)
        {
            float durationBeats = rangedDodgeSettings.ResponseOffsetBeats;
            if (eventData != null && rangedPhraseTimings != null)
            {
                string key = BuildRangedPhraseFrameKey(eventData);
                if (rangedPhraseTimings.TryGetValue(key, out RangedPhraseTiming timing) &&
                    timing.HasFirstTimingBeat &&
                    timing.FirstTimingBeat > eventData.Beat)
                {
                    durationBeats = timing.FirstTimingBeat - eventData.Beat;
                }
            }

            float bpm = meta != null && meta.Bpm > 0f ? meta.Bpm : 120f;
            return Mathf.Max(0f, durationBeats * (60f / bpm));
        }

        private static Vector2 ResolvePlayerOnBand(
            Vector2 currentPlayerLocalPosition,
            Vector2 currentMonsterLocalPosition,
            float responseBandUnits)
        {
            Vector2 radialFromMonster = currentPlayerLocalPosition - currentMonsterLocalPosition;
            if (radialFromMonster.sqrMagnitude <= 0.0001f)
            {
                radialFromMonster = -CombatDuelMath.GetDuelForward(currentPlayerLocalPosition, currentMonsterLocalPosition);
            }

            return currentMonsterLocalPosition + (radialFromMonster.normalized * responseBandUnits);
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return (left - right).sqrMagnitude <= 0.0001f;
        }

        private static RangedDodgeSettings ResolveRangedDodgeSettings(MonsterDefinition monsterDefinition, ChartEventData eventData)
        {
            PatternDefinition patternDefinition = ResolveRangedPatternDefinition(monsterDefinition, eventData);
            if (patternDefinition == null)
            {
                return RangedDodgeSettings.Default;
            }

            return new RangedDodgeSettings(
                patternDefinition.RangedDodgeMoveUnits,
                patternDefinition.RangedResponseBandUnits,
                patternDefinition.RangedResponseOffsetBeats,
                patternDefinition.AutoMovePlayerToResponseBand);
        }

        private static PatternDefinition ResolveRangedPatternDefinition(MonsterDefinition monsterDefinition, ChartEventData eventData)
        {
            if (monsterDefinition == null || monsterDefinition.SupportedPatterns == null || eventData == null ||
                eventData.Kind != CombatEventKind.RangedThreatDodgeOnly)
            {
                return null;
            }

            PatternDefinition fallbackPattern = null;
            int fallbackCount = 0;
            for (int i = 0; i < monsterDefinition.SupportedPatterns.Count; i++)
            {
                PatternDefinition patternDefinition = monsterDefinition.SupportedPatterns[i];
                if (patternDefinition == null || patternDefinition.CombatKind != CombatEventKind.RangedThreatDodgeOnly)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(eventData.AppliedPatternId) &&
                    string.Equals(patternDefinition.PatternId, eventData.AppliedPatternId, StringComparison.Ordinal))
                {
                    return patternDefinition;
                }

                fallbackPattern = patternDefinition;
                fallbackCount++;
            }

            return string.IsNullOrWhiteSpace(eventData.AppliedPatternId) && fallbackCount == 1
                ? fallbackPattern
                : null;
        }

        private static string BuildRangedPhraseFrameKey(ChartEventData eventData)
        {
            string phraseId = string.IsNullOrWhiteSpace(eventData.RangedPhraseId)
                ? $"step:{eventData.StepIndex}"
                : eventData.RangedPhraseId;
            return $"{eventData.Lane}:{phraseId}";
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

        private readonly struct RangedPhraseFrame
        {
            public RangedPhraseFrame(
                Vector2 responsePlayerLocalPosition,
                Vector2 responseMonsterLocalPosition,
                float responseBandUnits)
            {
                ResponsePlayerLocalPosition = responsePlayerLocalPosition;
                ResponseMonsterLocalPosition = responseMonsterLocalPosition;
                ResponseBandUnits = responseBandUnits;
            }

            public Vector2 ResponsePlayerLocalPosition { get; }
            public Vector2 ResponseMonsterLocalPosition { get; }
            public float ResponseBandUnits { get; }
        }

        private struct RangedPhraseTiming
        {
            public bool HasFirstReadyBeat;
            public float FirstReadyBeat;
            public bool HasFirstTimingBeat;
            public float FirstTimingBeat;
        }

        private readonly struct RangedDodgeSettings
        {
            public static readonly RangedDodgeSettings Default = new RangedDodgeSettings(
                DefaultRangedDodgeMoveUnits,
                DefaultRangedDodgeBandUnits,
                DefaultRangedResponseOffsetBeats,
                true);

            public RangedDodgeSettings(
                float dodgeMoveUnits,
                float responseBandUnits,
                float responseOffsetBeats,
                bool autoMovePlayerToResponseBand)
            {
                DodgeMoveUnits = Mathf.Max(0f, dodgeMoveUnits);
                ResponseBandUnits = Mathf.Max(0.001f, responseBandUnits);
                ResponseOffsetBeats = Mathf.Max(0.001f, responseOffsetBeats);
                AutoMovePlayerToResponseBand = autoMovePlayerToResponseBand;
            }

            public float DodgeMoveUnits { get; }
            public float ResponseBandUnits { get; }
            public float ResponseOffsetBeats { get; }
            public bool AutoMovePlayerToResponseBand { get; }
        }
    }
}
