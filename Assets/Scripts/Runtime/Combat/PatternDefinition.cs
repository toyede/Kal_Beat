using System.Collections.Generic;
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
    }
}
