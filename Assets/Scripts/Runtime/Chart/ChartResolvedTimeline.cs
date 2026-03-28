using System;
using System.Collections.Generic;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class ChartResolvedTimeline
    {
        [SerializeField] private int cacheVersion = 1;
        [SerializeField] private string sourceSignature = string.Empty;
        [SerializeField] private bool isValid;
        [SerializeField] private string invalidReason = string.Empty;
        [SerializeField] private List<ResolvedCombatStep> steps = new List<ResolvedCombatStep>();

        public int CacheVersion => cacheVersion;

        public string SourceSignature
        {
            get => sourceSignature;
            set => sourceSignature = value;
        }

        public bool IsValid
        {
            get => isValid;
            set => isValid = value;
        }

        public string InvalidReason
        {
            get => invalidReason;
            set => invalidReason = value;
        }

        public List<ResolvedCombatStep> Steps => steps;

        public void SetResolvedSteps(string signature, List<ResolvedCombatStep> resolvedSteps)
        {
            sourceSignature = signature ?? string.Empty;
            isValid = true;
            invalidReason = string.Empty;
            steps = resolvedSteps ?? new List<ResolvedCombatStep>();
        }

        public void SetInvalid(string reason)
        {
            isValid = false;
            invalidReason = reason ?? string.Empty;
            steps ??= new List<ResolvedCombatStep>();
            steps.Clear();
        }
    }
}
