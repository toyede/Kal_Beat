using System.Collections.Generic;
using System.Text;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    [CreateAssetMenu(fileName = "NewMonsterDefinition", menuName = "KalBeat/Combat/Monster Definition")]
    public sealed class MonsterDefinition : ScriptableObject
    {
        [SerializeField] private string monsterId = "monster_1";
        [SerializeField] private string displayName = "Monster 1";
        [SerializeField] private MonsterArchetype archetype = MonsterArchetype.Melee;
        [SerializeField] private GameObject prefab;
        [SerializeField] private bool supportsReadyMove;
        [SerializeField] private List<MovementDurationEntry> readyMoveDurationByUnits = new List<MovementDurationEntry>();
        [SerializeField] private List<MovementDurationEntry> timingApproachDurationByUnits = new List<MovementDurationEntry>();
        [SerializeField] private List<PatternDefinition> supportedPatterns = new List<PatternDefinition>();

        public string MonsterId
        {
            get => monsterId;
            set => monsterId = value;
        }

        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public MonsterArchetype Archetype
        {
            get => archetype;
            set => archetype = value;
        }

        public GameObject Prefab
        {
            get => prefab;
            set => prefab = value;
        }

        public bool SupportsReadyMove
        {
            get => supportsReadyMove;
            set => supportsReadyMove = value;
        }

        public List<MovementDurationEntry> ReadyMoveDurationByUnits => readyMoveDurationByUnits;
        public List<MovementDurationEntry> TimingApproachDurationByUnits => timingApproachDurationByUnits;
        public List<PatternDefinition> SupportedPatterns => supportedPatterns;

        public bool SupportsReadyCueType(ReadyCueType readyCueType)
        {
            if (readyCueType != ReadyCueType.Move)
            {
                return true;
            }

            return supportsReadyMove;
        }

        public bool SupportsPattern(PatternDefinition patternDefinition)
        {
            if (patternDefinition == null || patternDefinition.Steps == null)
            {
                return false;
            }

            for (int i = 0; i < patternDefinition.Steps.Count; i++)
            {
                PatternStepDefinition stepDefinition = patternDefinition.Steps[i];
                if (stepDefinition == null)
                {
                    continue;
                }

                if (stepDefinition.Phase == CombatEventPhase.Ready &&
                    !SupportsReadyCueType(stepDefinition.ReadyCueType))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetReadyMoveDurationSec(float units, out float durationSec)
        {
            return TryGetDurationSec(readyMoveDurationByUnits, units, out durationSec);
        }

        public bool TryGetTimingApproachDurationSec(float units, out float durationSec)
        {
            return TryGetDurationSec(timingApproachDurationByUnits, units, out durationSec);
        }

        public string BuildRuntimeSignature()
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append(monsterId).Append('|')
                .Append(displayName).Append('|')
                .Append((int)archetype).Append('|')
                .Append(supportsReadyMove).Append('|');

            AppendDurationEntries(builder, readyMoveDurationByUnits);
            AppendDurationEntries(builder, timingApproachDurationByUnits);
            return Hash128.Compute(builder.ToString()).ToString();
        }

        private void OnValidate()
        {
            readyMoveDurationByUnits ??= new List<MovementDurationEntry>();
            timingApproachDurationByUnits ??= new List<MovementDurationEntry>();
            supportedPatterns ??= new List<PatternDefinition>();
            SanitizeDurationEntries(readyMoveDurationByUnits);
            SanitizeDurationEntries(timingApproachDurationByUnits);
        }

        private static bool TryGetDurationSec(List<MovementDurationEntry> entries, float units, out float durationSec)
        {
            durationSec = 0f;
            if (entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                MovementDurationEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (Mathf.Approximately(entry.Units, units))
                {
                    durationSec = Mathf.Max(0.01f, entry.DurationSec);
                    return true;
                }
            }

            return false;
        }

        private static void SanitizeDurationEntries(List<MovementDurationEntry> entries)
        {
            entries.RemoveAll(entry => entry == null);

            for (int i = 0; i < entries.Count; i++)
            {
                MovementDurationEntry entry = entries[i];
                entry.Units = Mathf.Max(0f, entry.Units);
                entry.DurationSec = Mathf.Max(0.01f, entry.DurationSec);
            }
        }

        private static void AppendDurationEntries(StringBuilder builder, List<MovementDurationEntry> entries)
        {
            if (entries == null)
            {
                builder.Append("null|");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                MovementDurationEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                builder.Append(entry.Units.ToString("R")).Append(':')
                    .Append(entry.DurationSec.ToString("R"))
                    .Append('|');
            }
        }
    }
}
