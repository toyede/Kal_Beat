using System;
using KalBeat.Combat;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class ResolvedCombatStep
    {
        [SerializeField] private string monsterId = string.Empty;
        [SerializeField] private string monsterDefinitionSignature = string.Empty;
        [SerializeField] private float sourceBeat;
        [SerializeField] private int lane;
        [SerializeField] private CombatEventKind kind = CombatEventKind.None;
        [SerializeField] private CombatEventPhase phase = CombatEventPhase.Timing;
        [SerializeField] private int stepIndex;
        [SerializeField] private ReadyCueType readyCueType = ReadyCueType.None;
        [SerializeField] private PlayerRelativeDirection playerDirectionHint = PlayerRelativeDirection.None;
        [SerializeField] private MovementResolveMode movementResolveMode = MovementResolveMode.None;
        [SerializeField] private float moveUnits;
        [SerializeField] private float targetBandUnits;
        [SerializeField] private MovementDirectionHint movementDirectionHint = MovementDirectionHint.None;
        [SerializeField] private float monsterStartSec;
        [SerializeField] private float hitSec;
        [SerializeField] private float monsterDurationSec;
        [SerializeField] private float playerStartSec;
        [SerializeField] private float playerDurationSec;
        [SerializeField] private Vector2 startPlayerLocalPos;
        [SerializeField] private Vector2 endPlayerLocalPos;
        [SerializeField] private Vector2 startMonsterLocalPos;
        [SerializeField] private Vector2 endMonsterLocalPos;
        [SerializeField] private Vector2 playerApproachLocalPos;
        [SerializeField] private Vector2 lockedPlayerLocalPos;
        [SerializeField] private Vector2 monsterApproachLocalPos;
        [SerializeField] private float entryBandUnits;
        [SerializeField] private float exitBandUnits;
        [SerializeField] private float playerKnockbackUnits;
        [SerializeField] private float monsterKnockbackUnits;

        public string MonsterId
        {
            get => monsterId;
            set => monsterId = value;
        }

        public string MonsterDefinitionSignature
        {
            get => monsterDefinitionSignature;
            set => monsterDefinitionSignature = value;
        }

        public float SourceBeat
        {
            get => sourceBeat;
            set => sourceBeat = value;
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

        public PlayerRelativeDirection PlayerDirectionHint
        {
            get => playerDirectionHint;
            set => playerDirectionHint = value;
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

        public float MonsterStartSec
        {
            get => monsterStartSec;
            set => monsterStartSec = value;
        }

        public float HitSec
        {
            get => hitSec;
            set => hitSec = value;
        }

        public float MonsterDurationSec
        {
            get => monsterDurationSec;
            set => monsterDurationSec = value;
        }

        public float PlayerStartSec
        {
            get => playerStartSec;
            set => playerStartSec = value;
        }

        public float PlayerDurationSec
        {
            get => playerDurationSec;
            set => playerDurationSec = value;
        }

        public Vector2 StartPlayerLocalPos
        {
            get => startPlayerLocalPos;
            set => startPlayerLocalPos = value;
        }

        public Vector2 EndPlayerLocalPos
        {
            get => endPlayerLocalPos;
            set => endPlayerLocalPos = value;
        }

        public Vector2 StartMonsterLocalPos
        {
            get => startMonsterLocalPos;
            set => startMonsterLocalPos = value;
        }

        public Vector2 EndMonsterLocalPos
        {
            get => endMonsterLocalPos;
            set => endMonsterLocalPos = value;
        }

        public Vector2 PlayerApproachLocalPos
        {
            get => playerApproachLocalPos;
            set => playerApproachLocalPos = value;
        }

        public Vector2 LockedPlayerLocalPos
        {
            get => lockedPlayerLocalPos;
            set => lockedPlayerLocalPos = value;
        }

        public Vector2 MonsterApproachLocalPos
        {
            get => monsterApproachLocalPos;
            set => monsterApproachLocalPos = value;
        }

        public float EntryBandUnits
        {
            get => entryBandUnits;
            set => entryBandUnits = value;
        }

        public float ExitBandUnits
        {
            get => exitBandUnits;
            set => exitBandUnits = value;
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
