using System.Collections.Generic;
using UnityEngine;

namespace KalBeat.Charting
{
    [CreateAssetMenu(fileName = "NewChartData", menuName = "KalBeat/Chart Data")]
    public sealed class ChartData : ScriptableObject
    {
        [SerializeField] private ChartMeta meta = new ChartMeta();
        [SerializeField] private List<ChartEventData> events = new List<ChartEventData>();

        public ChartMeta Meta => meta;
        public List<ChartEventData> Events => events;

        private void OnValidate()
        {
            if (meta == null)
            {
                meta = new ChartMeta();
            }

            if (events == null)
            {
                events = new List<ChartEventData>();
            }
        }
    }
}
