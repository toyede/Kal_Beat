using System;
using System.Collections.Generic;
using System.Text;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    [CreateAssetMenu(fileName = "NewPatternDefinition", menuName = "KalBeat/Combat/Pattern Definition")]
    public sealed class PatternDefinition : ScriptableObject
    {
        [SerializeField] private string patternId = "pattern_1";
        [SerializeField] private string displayName = "Pattern 1";
        [SerializeField] private CombatEventKind combatKind = CombatEventKind.MeleeThreatDodgeOnly;
        [SerializeField] private float entryBandUnits = 4f;
        [SerializeField] private List<PatternStepDefinition> steps = new List<PatternStepDefinition>();

        public string PatternId
        {
            get => patternId;
            set => patternId = value;
        }

        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public CombatEventKind CombatKind
        {
            get => combatKind;
            set => combatKind = value;
        }

        public float EntryBandUnits
        {
            get => entryBandUnits;
            set => entryBandUnits = value;
        }

        public List<PatternStepDefinition> Steps => steps;

        public string BuildSyncSignature()
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append((int)combatKind).Append('|')
                .Append(entryBandUnits.ToString("R")).Append('|');

            List<PatternStepDefinition> orderedSteps = new List<PatternStepDefinition>();
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i] != null)
                    {
                        orderedSteps.Add(steps[i]);
                    }
                }
            }

            orderedSteps.Sort(CompareSteps);

            for (int i = 0; i < orderedSteps.Count; i++)
            {
                PatternStepDefinition step = orderedSteps[i];
                builder.Append(step.BeatOffsetFromTiming.ToString("R")).Append(':')
                    .Append((int)step.Phase).Append(':')
                    .Append((int)step.ReadyCueType).Append(':')
                    .Append((int)step.DirectionHint).Append(':')
                    .Append((int)step.MovementResolveMode).Append(':')
                    .Append(step.MoveUnits.ToString("R")).Append(':')
                    .Append(step.TargetBandUnits.ToString("R")).Append(':')
                    .Append((int)step.MovementDirectionHint).Append(':')
                    .Append(step.PlayerKnockbackUnits.ToString("R")).Append(':')
                    .Append(step.MonsterKnockbackUnits.ToString("R"))
                    .Append('|');
            }

            return Hash128.Compute(builder.ToString()).ToString();
        }

        private void OnValidate()
        {
            steps ??= new List<PatternStepDefinition>();
            steps.RemoveAll(step => step == null);
            entryBandUnits = Mathf.Max(0f, entryBandUnits);

            for (int i = 0; i < steps.Count; i++)
            {
                PatternStepDefinition step = steps[i];
                if (step == null)
                {
                    continue;
                }

                step.MoveUnits = Mathf.Max(0f, step.MoveUnits);
                step.TargetBandUnits = Mathf.Max(0f, step.TargetBandUnits);
                step.PlayerKnockbackUnits = Mathf.Max(0f, step.PlayerKnockbackUnits);
                step.MonsterKnockbackUnits = Mathf.Max(0f, step.MonsterKnockbackUnits);

                if (step.Phase == CombatEventPhase.Timing)
                {
                    step.ReadyCueType = ReadyCueType.None;
                    step.MovementResolveMode = MovementResolveMode.None;
                    step.MoveUnits = 0f;
                    step.MovementDirectionHint = MovementDirectionHint.None;
                    continue;
                }

                if (combatKind == CombatEventKind.PlayerDashAttack)
                {
                    step.ReadyCueType = ReadyCueType.None;
                }

                if (step.ReadyCueType != ReadyCueType.Move)
                {
                    step.MovementResolveMode = MovementResolveMode.None;
                    step.MoveUnits = 0f;
                    step.TargetBandUnits = 0f;
                    step.MovementDirectionHint = MovementDirectionHint.None;
                }

                step.PlayerKnockbackUnits = 0f;
                step.MonsterKnockbackUnits = 0f;
            }
        }

        private static int CompareSteps(PatternStepDefinition left, PatternStepDefinition right)
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

            int beatCompare = left.BeatOffsetFromTiming.CompareTo(right.BeatOffsetFromTiming);
            if (beatCompare != 0)
            {
                return beatCompare;
            }

            return left.Phase.CompareTo(right.Phase);
        }
    }
}
