using System;
using System.Collections.Generic;
using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Charting.Editor
{
    internal static class JsonChartImportUtility
    {
        private static readonly string[] TimingRoleLaneOrder =
        {
            "pulse",
            "offbeat",
            "subdivision",
            "thirtySecond",
            "triplet",
            "freeAccent"
        };

        public static bool TryCreateImportData(TextAsset jsonAsset, out JsonChartImportData importData, out string errorMessage)
        {
            importData = null;
            errorMessage = string.Empty;

            if (jsonAsset == null)
            {
                errorMessage = "JSON chart asset is not assigned.";
                return false;
            }

            JsonChartImportRoot root;
            try
            {
                root = JsonUtility.FromJson<JsonChartImportRoot>(jsonAsset.text);
            }
            catch (Exception exception)
            {
                errorMessage = $"JSON parse failed: {exception.Message}";
                return false;
            }

            if (root == null)
            {
                errorMessage = "JSON chart could not be parsed.";
                return false;
            }

            if (root.globalBpm <= 0f || float.IsNaN(root.globalBpm) || float.IsInfinity(root.globalBpm))
            {
                errorMessage = "JSON globalBpm must be greater than 0.";
                return false;
            }

            if (root.selectedEvents == null)
            {
                errorMessage = "JSON selectedEvents is missing.";
                return false;
            }

            Dictionary<string, int> laneMap = BuildLaneMap(root.selectedEvents);
            List<ChartEventData> importedEvents = new List<ChartEventData>(root.selectedEvents.Length);

            for (int i = 0; i < root.selectedEvents.Length; i++)
            {
                JsonChartImportEvent sourceEvent = root.selectedEvents[i];
                string normalizedRole = NormalizeTimingRole(sourceEvent.timingRole);
                int lane = laneMap.TryGetValue(normalizedRole, out int mappedLane) ? mappedLane : 0;
                float beat = Mathf.Max(0f, (sourceEvent.timeSec - root.offsetSec) * root.globalBpm / 60f);

                ChartEventData chartEvent = new ChartEventData
                {
                    Beat = beat,
                    Lane = lane,
                    EventType = ChartEventType.None,
                    MonsterActionKind = MonsterActionKind.None,
                    CameraCueKind = CameraCueKind.None,
                    CameraMovingKind = CameraMovingKind.None,
                    NudgeMs = 0f
                };

                importedEvents.Add(chartEvent);
            }

            importData = new JsonChartImportData(
                root.globalBpm,
                root.offsetSec,
                root.songLengthSec,
                Mathf.Max(4, laneMap.Count),
                importedEvents);

            return true;
        }

        private static Dictionary<string, int> BuildLaneMap(JsonChartImportEvent[] sourceEvents)
        {
            Dictionary<string, int> laneMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sourceEvents.Length; i++)
            {
                string normalizedRole = NormalizeTimingRole(sourceEvents[i].timingRole);
                if (!string.IsNullOrEmpty(normalizedRole))
                {
                    usedRoles.Add(normalizedRole);
                }
            }

            int laneIndex = 0;
            for (int i = 0; i < TimingRoleLaneOrder.Length; i++)
            {
                string role = TimingRoleLaneOrder[i];
                if (usedRoles.Contains(role))
                {
                    laneMap[role] = laneIndex;
                    laneIndex++;
                }
            }

            for (int i = 0; i < sourceEvents.Length; i++)
            {
                string normalizedRole = NormalizeTimingRole(sourceEvents[i].timingRole);
                if (string.IsNullOrEmpty(normalizedRole) || laneMap.ContainsKey(normalizedRole))
                {
                    continue;
                }

                laneMap[normalizedRole] = laneIndex;
                laneIndex++;
            }

            return laneMap;
        }

        private static string NormalizeTimingRole(string timingRole)
        {
            return string.IsNullOrWhiteSpace(timingRole) ? string.Empty : timingRole.Trim();
        }

        internal sealed class JsonChartImportData
        {
            public JsonChartImportData(float bpm, float offsetSec, float songLengthSec, int laneCount, List<ChartEventData> events)
            {
                Bpm = bpm;
                OffsetSec = offsetSec;
                SongLengthSec = songLengthSec;
                LaneCount = laneCount;
                Events = events ?? new List<ChartEventData>();
            }

            public float Bpm { get; }

            public float OffsetSec { get; }

            public float SongLengthSec { get; }

            public int LaneCount { get; }

            public List<ChartEventData> Events { get; }
        }

        [Serializable]
        private sealed class JsonChartImportRoot
        {
            public float globalBpm;
            public float offsetSec;
            public float songLengthSec;
            public JsonChartImportEvent[] selectedEvents;
        }

        [Serializable]
        private sealed class JsonChartImportEvent
        {
            public float timeSec;
            public string timingRole;
        }
    }
}
