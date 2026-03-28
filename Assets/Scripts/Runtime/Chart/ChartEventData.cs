using System;
using KalBeat.Combat;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class ChartEventData
    {
        [SerializeField] private float beat;
        [SerializeField] private int lane;
        [SerializeField] private CombatEventKind kind = CombatEventKind.None;
        [SerializeField] private CombatEventPhase phase = CombatEventPhase.Timing;
        [SerializeField] private int stepIndex;
        [SerializeField] private ReadyCueType readyCueType = ReadyCueType.None;
        [SerializeField] private PlayerRelativeDirection directionHint = PlayerRelativeDirection.None;
        [SerializeField] private float nudgeMs;
        [SerializeField] private MovementResolveMode movementResolveMode = MovementResolveMode.None;
        [SerializeField] private float moveUnits;
        [SerializeField] private float targetBandUnits;
        [SerializeField] private MovementDirectionHint movementDirectionHint = MovementDirectionHint.None;
        [SerializeField] private float entryBandUnits;

        public float Beat
        {
            get => beat;
            set => beat = value;
        }

        public int Lane
        {
            get => lane;
            set => lane = value;
        }

        public CombatEventKind Kind
        {
            get => kind;
            set => kind = value;
        }

        public CombatEventPhase Phase
        {
            get => phase;
            set => phase = value;
        }

        public int StepIndex
        {
            get => stepIndex;
            set => stepIndex = value;
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

        public float NudgeMs
        {
            get => nudgeMs;
            set => nudgeMs = value;
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

        public float EntryBandUnits
        {
            get => entryBandUnits;
            set => entryBandUnits = value;
        }
    }
}
