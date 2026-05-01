using System;
using System.Collections.Generic;
using KalBeat.Combat;

namespace KalBeat.Charting
{
    public static class ChartValidator
    {
        private const string ErrorCodeChartNull = "chart.null";
        private const string ErrorCodeMetaNull = "meta.null";
        private const string ErrorCodeEventsNull = "events.null";
        private const string ErrorCodeBpmInvalid = "meta.bpm.invalid";
        private const string ErrorCodeSongLengthInvalid = "meta.song_length.invalid";
        private const string ErrorCodeGridDivisionInvalid = "meta.grid_division.invalid";
        private const string ErrorCodeLaneCountInvalid = "meta.lane_count.invalid";
        private const string ErrorCodeMaxEventsPerSlotInvalid = "meta.max_events_per_slot.invalid";
        private const string ErrorCodeFmodEventPathMissing = "meta.fmod_event_path.missing";
        private const string ErrorCodeEventNull = "event.null";
        private const string ErrorCodeEventBeatInvalid = "event.beat.invalid";
        private const string ErrorCodeEventLaneInvalid = "event.lane.invalid";
        private const string ErrorCodeEventStepIndexInvalid = "event.step_index.invalid";
        private const string ErrorCodeEntryBandMissing = "event.entry_band.missing";
        private const string ErrorCodeMoveResolveModeMissing = "event.move.resolve_mode.missing";
        private const string ErrorCodeMoveUnitsInvalid = "event.move.units.invalid";
        private const string ErrorCodeTargetBandInvalid = "event.move.target_band.invalid";
        private const string ErrorCodeMoveDirectionMissing = "event.move.direction.missing";
        private const string ErrorCodePlayerDashReadyCueInvalid = "event.player_dash.ready_cue.invalid";
        private const string ErrorCodeCounterPlayerKnockbackInvalid = "event.counter.player_knockback.invalid";
        private const string ErrorCodeCounterMonsterKnockbackInvalid = "event.counter.monster_knockback.invalid";
        private const string ErrorCodeRangedPhraseMissing = "event.ranged.phrase.missing";
        private const string ErrorCodeRangedCueIndexInvalid = "event.ranged.cue_index.invalid";
        private const string ErrorCodeRangedAnchorIndexInvalid = "event.ranged.anchor_index.invalid";
        private const string ErrorCodeRangedReadyCueInvalid = "event.ranged.ready_cue.invalid";
        private const string ErrorCodeRangedPairInvalid = "event.ranged.pair.invalid";
        private const string ErrorCodeRangedPairAnchorMismatch = "event.ranged.pair.anchor_mismatch";
        private const string ErrorCodeSlotOverflow = "event.slot_overflow";
        private const string WarningCodeEventKindNone = "event.kind.none";
        private const string WarningCodeReadyCueMissing = "event.ready_cue.missing";
        private const string WarningCodeDirectionMissing = "event.direction.missing";
        private const string WarningCodeNudgeInvalid = "event.nudge.invalid";

        public static ChartValidationResult Validate(ChartData chart)
        {
            var result = new ChartValidationResult();

            if (chart == null)
            {
                result.AddError(ErrorCodeChartNull, "ChartData is null.");
                return result;
            }

            var meta = chart.Meta;
            if (meta == null)
            {
                result.AddError(ErrorCodeMetaNull, "ChartData meta is null.");
                return result;
            }

            meta.SyncLaneBindingsWithLaneCount();

            if (meta.Bpm <= 0f || float.IsNaN(meta.Bpm) || float.IsInfinity(meta.Bpm))
            {
                result.AddError(ErrorCodeBpmInvalid, "BPM must be greater than 0.");
            }

            if (meta.SongLengthSec <= 0f || float.IsNaN(meta.SongLengthSec) || float.IsInfinity(meta.SongLengthSec))
            {
                result.AddError(ErrorCodeSongLengthInvalid, "SongLengthSec must be greater than 0.");
            }

            if (meta.GridDivision != 4 && meta.GridDivision != 8)
            {
                result.AddError(ErrorCodeGridDivisionInvalid, "GridDivision must be 4 or 8 in MVP.");
            }

            if (meta.LaneCount <= 0)
            {
                result.AddError(ErrorCodeLaneCountInvalid, "LaneCount must be greater than 0.");
            }

            if (meta.MaxEventsPerSlot <= 0)
            {
                result.AddError(ErrorCodeMaxEventsPerSlotInvalid, "MaxEventsPerSlot must be greater than 0.");
            }

            if (string.IsNullOrWhiteSpace(meta.FmodEventPath))
            {
                result.AddError(ErrorCodeFmodEventPathMissing, "FMOD event path is required.");
            }

            var events = chart.Events;
            if (events == null)
            {
                result.AddError(ErrorCodeEventsNull, "Events list is null.");
                return result;
            }

            int safeGridDivision = meta.GridDivision > 0 ? meta.GridDivision : 1;
            int safeLaneCount = meta.LaneCount > 0 ? meta.LaneCount : 1;
            int safeMaxEventsPerSlot = meta.MaxEventsPerSlot > 0 ? meta.MaxEventsPerSlot : 1;
            var slotCounter = new Dictionary<int, int>();
            var rangedPairs = new Dictionary<string, RangedPairValidationState>(StringComparer.Ordinal);

            for (int i = 0; i < events.Count; i++)
            {
                ChartEventData eventData = events[i];
                if (eventData == null)
                {
                    result.AddError(ErrorCodeEventNull, "Event item is null.", i);
                    continue;
                }

                if (float.IsNaN(eventData.Beat) || float.IsInfinity(eventData.Beat) || eventData.Beat < 0f)
                {
                    result.AddError(ErrorCodeEventBeatInvalid, "Event beat must be a finite value greater than or equal to 0.", i);
                }

                if (eventData.Lane < 0 || eventData.Lane >= safeLaneCount)
                {
                    result.AddError(
                        ErrorCodeEventLaneInvalid,
                        $"Event lane must be in range [0, {safeLaneCount - 1}].",
                        i);
                }

                if (eventData.StepIndex < 0)
                {
                    result.AddError(ErrorCodeEventStepIndexInvalid, "StepIndex must be greater than or equal to 0.", i);
                }

                if (eventData.Kind != CombatEventKind.None &&
                    eventData.StepIndex == 0 &&
                    eventData.EntryBandUnits <= 0f)
                {
                    result.AddError(ErrorCodeEntryBandMissing, "StepIndex 0 event must set EntryBandUnits greater than 0.", i);
                }

                if (eventData.Kind == CombatEventKind.None)
                {
                    result.AddWarning(WarningCodeEventKindNone, "Event kind is None. This event will be ignored at runtime.", i);
                }

                if (eventData.Phase == CombatEventPhase.Ready &&
                    eventData.ReadyCueType == ReadyCueType.None &&
                    eventData.Kind != CombatEventKind.PlayerDashAttack &&
                    eventData.Kind != CombatEventKind.RangedThreatDodgeOnly)
                {
                    result.AddWarning(WarningCodeReadyCueMissing, "Ready phase event should set ReadyCueType.", i);
                }

                if (eventData.Phase == CombatEventPhase.Ready &&
                    eventData.Kind == CombatEventKind.PlayerDashAttack &&
                    eventData.ReadyCueType != ReadyCueType.None)
                {
                    result.AddError(ErrorCodePlayerDashReadyCueInvalid, "PlayerDashAttack Ready event is treated as Ready Mark and must not set ReadyCueType.", i);
                }

                if (eventData.Phase == CombatEventPhase.Ready && eventData.ReadyCueType == ReadyCueType.Move)
                {
                    if (eventData.MovementResolveMode == MovementResolveMode.None)
                    {
                        result.AddError(ErrorCodeMoveResolveModeMissing, "Ready Move event must set MovementResolveMode.", i);
                    }

                    if (eventData.MoveUnits <= 0f)
                    {
                        result.AddError(ErrorCodeMoveUnitsInvalid, "Ready Move event must set MoveUnits greater than 0.", i);
                    }

                    if (eventData.TargetBandUnits < 0f)
                    {
                        result.AddError(ErrorCodeTargetBandInvalid, "Ready Move event must set TargetBandUnits greater than or equal to 0.", i);
                    }

                    if (eventData.MovementResolveMode == MovementResolveMode.SideMoveOnBand &&
                        eventData.MovementDirectionHint == MovementDirectionHint.None)
                    {
                        result.AddError(ErrorCodeMoveDirectionMissing, "SideMoveOnBand event must set MovementDirectionHint.", i);
                    }
                }

                if (eventData.Phase == CombatEventPhase.Timing &&
                    (eventData.Kind == CombatEventKind.MeleeThreatCounterable ||
                     eventData.Kind == CombatEventKind.PlayerDashAttack))
                {
                    if (eventData.TargetBandUnits < 0f)
                    {
                        result.AddError(ErrorCodeTargetBandInvalid, "Timing event must set TargetBandUnits greater than or equal to 0.", i);
                    }

                    if (eventData.PlayerKnockbackUnits < 0f)
                    {
                        result.AddError(ErrorCodeCounterPlayerKnockbackInvalid, "Timing event must set PlayerKnockbackUnits greater than or equal to 0.", i);
                    }

                    if (eventData.MonsterKnockbackUnits < 0f)
                    {
                        result.AddError(ErrorCodeCounterMonsterKnockbackInvalid, "Timing event must set MonsterKnockbackUnits greater than or equal to 0.", i);
                    }
                }

                if (eventData.Kind == CombatEventKind.RangedThreatDodgeOnly)
                {
                    ValidateRangedDodgeEvent(result, rangedPairs, eventData, i);
                }

                if (RequiresDirectionHint(eventData) && eventData.DirectionHint == PlayerRelativeDirection.None)
                {
                    if (eventData.Kind == CombatEventKind.RangedThreatDodgeOnly)
                    {
                        result.AddError(WarningCodeDirectionMissing, "RangedThreatDodgeOnly Timing event must set DirectionHint.", i);
                    }
                    else
                    {
                        result.AddWarning(WarningCodeDirectionMissing, "MeleeThreatDodgeOnly should set DirectionHint.", i);
                    }
                }

                if (float.IsNaN(eventData.NudgeMs) || float.IsInfinity(eventData.NudgeMs))
                {
                    result.AddWarning(WarningCodeNudgeInvalid, "NudgeMs should be a finite value.", i);
                }

                int slotIndex = GetSlotIndex(eventData.Beat, safeGridDivision);
                int updatedCount = 1;
                if (slotCounter.TryGetValue(slotIndex, out int currentCount))
                {
                    updatedCount = currentCount + 1;
                }

                slotCounter[slotIndex] = updatedCount;

                if (updatedCount > safeMaxEventsPerSlot)
                {
                    result.AddError(
                        ErrorCodeSlotOverflow,
                        $"More than {safeMaxEventsPerSlot} events are placed in one time slot.",
                        i);
                }
            }

            foreach (RangedPairValidationState pairState in rangedPairs.Values)
            {
                if (pairState.ReadyCount != 1 || pairState.TimingCount != 1)
                {
                    result.AddError(
                        ErrorCodeRangedPairInvalid,
                        "Each RangedThreatDodgeOnly cue must contain exactly one Ready call and one Timing response.",
                        pairState.FirstEventIndex);
                }
            }

            return result;
        }

        private static int GetSlotIndex(float beat, int gridDivision)
        {
            double scaled = beat * gridDivision;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static bool RequiresDirectionHint(ChartEventData eventData)
        {
            if (eventData == null || eventData.Kind == CombatEventKind.None)
            {
                return false;
            }

            return eventData.Phase == CombatEventPhase.Timing &&
                   (eventData.Kind == CombatEventKind.MeleeThreatDodgeOnly ||
                    eventData.Kind == CombatEventKind.RangedThreatDodgeOnly);
        }

        private static void ValidateRangedDodgeEvent(
            ChartValidationResult result,
            Dictionary<string, RangedPairValidationState> rangedPairs,
            ChartEventData eventData,
            int eventIndex)
        {
            if (string.IsNullOrWhiteSpace(eventData.RangedPhraseId))
            {
                result.AddError(ErrorCodeRangedPhraseMissing, "RangedThreatDodgeOnly event must set RangedPhraseId.", eventIndex);
            }

            if (eventData.RangedCueIndex < 0)
            {
                result.AddError(ErrorCodeRangedCueIndexInvalid, "RangedThreatDodgeOnly event must set RangedCueIndex greater than or equal to 0.", eventIndex);
            }

            if (eventData.RangedAnchorIndex < 0)
            {
                result.AddError(ErrorCodeRangedAnchorIndexInvalid, "RangedThreatDodgeOnly event must set RangedAnchorIndex greater than or equal to 0.", eventIndex);
            }

            if (eventData.Phase == CombatEventPhase.Ready && eventData.ReadyCueType != ReadyCueType.None)
            {
                result.AddError(ErrorCodeRangedReadyCueInvalid, "RangedThreatDodgeOnly Ready event is treated as a magic circle call and must not set ReadyCueType.", eventIndex);
            }

            if (string.IsNullOrWhiteSpace(eventData.RangedPhraseId) || eventData.RangedCueIndex < 0)
            {
                return;
            }

            string key = $"{eventData.Lane}:{eventData.RangedPhraseId}:{eventData.RangedCueIndex}";
            if (!rangedPairs.TryGetValue(key, out RangedPairValidationState pairState))
            {
                pairState = new RangedPairValidationState(eventIndex, eventData.RangedAnchorIndex);
                rangedPairs.Add(key, pairState);
            }
            else if (pairState.AnchorIndex != eventData.RangedAnchorIndex)
            {
                result.AddError(
                    ErrorCodeRangedPairAnchorMismatch,
                    "RangedThreatDodgeOnly Ready and Timing events with the same phrase/cue must use the same RangedAnchorIndex.",
                    eventIndex);
            }

            if (eventData.Phase == CombatEventPhase.Ready)
            {
                pairState.ReadyCount++;
            }
            else if (eventData.Phase == CombatEventPhase.Timing)
            {
                pairState.TimingCount++;
            }
        }

        private sealed class RangedPairValidationState
        {
            public RangedPairValidationState(int firstEventIndex, int anchorIndex)
            {
                FirstEventIndex = firstEventIndex;
                AnchorIndex = anchorIndex;
            }

            public int FirstEventIndex { get; }
            public int AnchorIndex { get; }
            public int ReadyCount { get; set; }
            public int TimingCount { get; set; }
        }
    }
}
