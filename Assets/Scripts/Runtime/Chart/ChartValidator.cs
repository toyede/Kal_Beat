using System;
using System.Collections.Generic;

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
        private const string ErrorCodeSlotOverflow = "event.slot_overflow";
        private const string WarningCodeEventTypeNone = "event.type.none";
        private const string WarningCodeMonsterActionKindNone = "event.monster_action_kind.none";
        private const string WarningCodeCameraCueKindNone = "event.camera_cue_kind.none";
        private const string WarningCodeCameraMovingKindNone = "event.camera_moving_kind.none";
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

                if (eventData.EventType == ChartEventType.None)
                {
                    result.AddWarning(WarningCodeEventTypeNone, "Event type is None. This event will be ignored at runtime.", i);
                }
                else if (eventData.EventType == ChartEventType.MonsterAction && eventData.MonsterActionKind == MonsterActionKind.None)
                {
                    result.AddWarning(WarningCodeMonsterActionKindNone, "MonsterAction event should set MonsterActionKind.", i);
                }
                else if (eventData.EventType == ChartEventType.CameraCue && eventData.CameraCueKind == CameraCueKind.None)
                {
                    result.AddWarning(WarningCodeCameraCueKindNone, "CameraCue event should set CameraCueKind.", i);
                }
                else if (eventData.EventType == ChartEventType.CameraMoving && eventData.CameraMovingKind == CameraMovingKind.None)
                {
                    result.AddWarning(WarningCodeCameraMovingKindNone, "CameraMoving event should set CameraMovingKind.", i);
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

            return result;
        }

        private static int GetSlotIndex(float beat, int gridDivision)
        {
            double scaled = beat * gridDivision;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }
    }
}
