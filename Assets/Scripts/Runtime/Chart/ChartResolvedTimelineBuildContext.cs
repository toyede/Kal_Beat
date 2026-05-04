using System;
using System.Collections.Generic;
using UnityEngine;

namespace KalBeat.Charting
{
    public sealed class ChartResolvedTimelineBuildContext
    {
        private readonly Dictionary<int, Vector3> rangedAnchorLocalOffsets = new Dictionary<int, Vector3>();
        private readonly List<string> warnings = new List<string>();

        public float UnitSize { get; set; } = 1f;
        public float GoodWindowSec { get; set; } = 0.08f;
        public bool HasPlayerHitAnchor { get; set; }
        public Vector3 PlayerHitAnchorLocalOffsetUnits { get; set; }
        public IReadOnlyList<string> Warnings => warnings;

        public void SetRangedAnchorLocalOffsets(IList<Vector3> offsets)
        {
            rangedAnchorLocalOffsets.Clear();
            if (offsets == null)
            {
                return;
            }

            for (int i = 0; i < offsets.Count; i++)
            {
                rangedAnchorLocalOffsets[i] = offsets[i];
            }
        }

        public bool TryGetRangedAnchorLocalOffset(int anchorIndex, out Vector3 offset)
        {
            return rangedAnchorLocalOffsets.TryGetValue(anchorIndex, out offset);
        }

        public void AddWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
            {
                return;
            }

            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        public string BuildSignature()
        {
            var builder = new System.Text.StringBuilder(256);
            builder.Append(UnitSize.ToString("R")).Append('|')
                .Append(GoodWindowSec.ToString("R")).Append('|')
                .Append(HasPlayerHitAnchor).Append('|')
                .Append(PlayerHitAnchorLocalOffsetUnits.x.ToString("R")).Append(':')
                .Append(PlayerHitAnchorLocalOffsetUnits.y.ToString("R")).Append(':')
                .Append(PlayerHitAnchorLocalOffsetUnits.z.ToString("R")).Append('|');

            foreach (KeyValuePair<int, Vector3> pair in rangedAnchorLocalOffsets)
            {
                Vector3 value = pair.Value;
                builder.Append(pair.Key).Append(':')
                    .Append(value.x.ToString("R")).Append(':')
                    .Append(value.y.ToString("R")).Append(':')
                    .Append(value.z.ToString("R")).Append('|');
            }

            return Hash128.Compute(builder.ToString()).ToString();
        }
    }
}
