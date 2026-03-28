using System.Collections.Generic;
using UnityEngine;

namespace KalBeat.Charting
{
    [CreateAssetMenu(fileName = "NewChartData", menuName = "KalBeat/Chart Data")]
    public sealed class ChartData : ScriptableObject
    {
        [SerializeField] private ChartMeta meta = new ChartMeta();
        [SerializeField] private List<ChartEventData> events = new List<ChartEventData>();
        [SerializeField, HideInInspector] private ChartResolvedTimeline resolvedTimeline = new ChartResolvedTimeline();

        public ChartMeta Meta => meta;
        public List<ChartEventData> Events => events;
        public ChartResolvedTimeline ResolvedTimeline => resolvedTimeline;

        public void InvalidateResolvedTimeline(string reason)
        {
            resolvedTimeline ??= new ChartResolvedTimeline();
            resolvedTimeline.SourceSignature = string.Empty;
            resolvedTimeline.SetInvalid(reason);
        }

        private void OnValidate()
        {
            if (meta == null)
            {
                meta = new ChartMeta();
            }

            meta.SyncLaneBindingsWithLaneCount();

            if (events == null)
            {
                events = new List<ChartEventData>();
            }

            resolvedTimeline ??= new ChartResolvedTimeline();
        }
    }
}
