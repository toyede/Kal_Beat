using KalBeat.Charting;

namespace KalBeat.Combat
{
    public readonly struct CombatJudgementResult
    {
        public CombatJudgementResult(
            CombatJudgementGrade grade,
            float deltaMs,
            bool hasDelta,
            bool isAutoMiss,
            ResolvedCombatStep resolvedStep)
        {
            Grade = grade;
            DeltaMs = deltaMs;
            HasDelta = hasDelta;
            IsAutoMiss = isAutoMiss;
            ResolvedStep = resolvedStep;
        }

        public CombatJudgementGrade Grade { get; }
        public float DeltaMs { get; }
        public bool HasDelta { get; }
        public bool IsAutoMiss { get; }
        public ResolvedCombatStep ResolvedStep { get; }
    }
}
