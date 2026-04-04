using System;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    [Serializable]
    public sealed class PatternStepDefinition
    {
        [SerializeField] private float beatOffsetFromTiming;
        [SerializeField] private CombatEventPhase phase = CombatEventPhase.Ready;
        [SerializeField] private ReadyCueType readyCueType = ReadyCueType.None;
        [SerializeField] private PlayerRelativeDirection directionHint = PlayerRelativeDirection.None;
        [SerializeField] private MovementResolveMode movementResolveMode = MovementResolveMode.None;
        [SerializeField] private float moveUnits;
        [SerializeField] private float targetBandUnits;
        [SerializeField] private MovementDirectionHint movementDirectionHint = MovementDirectionHint.None;
        [SerializeField] private float playerKnockbackUnits;
        [SerializeField] private float monsterKnockbackUnits;

        public float BeatOffsetFromTiming
        {
            get => beatOffsetFromTiming;
            set => beatOffsetFromTiming = value;
        }

        public CombatEventPhase Phase
        {
            get => phase;
            set => phase = value;
        }

        public ReadyCueType ReadyCueType
        {
            get => readyCueType;
            set => readyCueType = value;
        }

        public PlayerRelativeDirection DirectionHint
        {
            get => directionHint;
            set => directionHint = value;
        }

        public MovementResolveMode MovementResolveMode
        {
            get => movementResolveMode;
            set => movementResolveMode = value;
        }

        public float MoveUnits
        {
            get => moveUnits;
            set => moveUnits = value;
        }

        public float TargetBandUnits
        {
            get => targetBandUnits;
            set => targetBandUnits = value;
        }

        public MovementDirectionHint MovementDirectionHint
        {
            get => movementDirectionHint;
            set => movementDirectionHint = value;
        }

        public float PlayerKnockbackUnits
        {
            get => playerKnockbackUnits;
            set => playerKnockbackUnits = value;
        }

        public float MonsterKnockbackUnits
        {
            get => monsterKnockbackUnits;
            set => monsterKnockbackUnits = value;
        }
    }
}
