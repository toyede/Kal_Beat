using UnityEngine;
using KalBeat.Charting;

namespace KalBeat.Combat
{
    public sealed class CombatDuelRoot : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor playerActor;
        [SerializeField] private MonsterCombatActor monsterActor;
        [SerializeField] private float unitSize = 1f;
        [SerializeField] private float startDistanceUnits = 4f;

        public PlayerCombatActor PlayerActor => playerActor;
        public MonsterCombatActor MonsterActor => monsterActor;
        public float UnitSize => unitSize;
        public float StartDistanceUnits => startDistanceUnits;

        public void InitializeLayout()
        {
            if (playerActor != null)
            {
                playerActor.InitializeCombat(this);
                playerActor.transform.position = GetPlayerBaseWorldPosition();
                playerActor.transform.forward = transform.forward;
            }

            if (monsterActor != null)
            {
                monsterActor.InitializeCombat(this);
                monsterActor.transform.position = GetMonsterBaseWorldPosition();
                monsterActor.transform.forward = -transform.forward;
            }
        }

        public void ResetLayout()
        {
            InitializeLayout();

            if (playerActor != null)
            {
                playerActor.ResetCombatState();
            }

            if (monsterActor != null)
            {
                monsterActor.ResetCombatState();
            }
        }

        public Vector3 GetPlayerBaseWorldPosition()
        {
            return ConvertDuelLocalToWorld(GetPlayerBaseLocalPositionUnits());
        }

        public Vector3 GetMonsterBaseWorldPosition()
        {
            return ConvertDuelLocalToWorld(GetMonsterBaseLocalPositionUnits());
        }

        public Vector2 GetPlayerBaseLocalPositionUnits()
        {
            return Vector2.zero;
        }

        public Vector2 GetMonsterBaseLocalPositionUnits()
        {
            return new Vector2(0f, startDistanceUnits);
        }

        public Vector3 GetCurrentPlayerWorldPosition()
        {
            return playerActor != null ? playerActor.transform.position : GetPlayerBaseWorldPosition();
        }

        public Vector3 GetCurrentMonsterWorldPosition()
        {
            return monsterActor != null ? monsterActor.transform.position : GetMonsterBaseWorldPosition();
        }

        public Vector2 GetCurrentPlayerLocalPositionUnits()
        {
            return ConvertWorldToDuelLocalUnits(GetCurrentPlayerWorldPosition());
        }

        public Vector2 GetCurrentMonsterLocalPositionUnits()
        {
            return ConvertWorldToDuelLocalUnits(GetCurrentMonsterWorldPosition());
        }

        public Vector3 ConvertDuelLocalToWorld(Vector2 localPositionUnits)
        {
            return transform.position +
                   (transform.right * (localPositionUnits.x * unitSize)) +
                   (transform.forward * (localPositionUnits.y * unitSize));
        }

        public Vector2 ConvertWorldToDuelLocalUnits(Vector3 worldPosition)
        {
            Vector3 localOffset = worldPosition - transform.position;
            float localXUnits = Vector3.Dot(localOffset, transform.right) / unitSize;
            float localYUnits = Vector3.Dot(localOffset, transform.forward) / unitSize;
            return new Vector2(localXUnits, localYUnits);
        }

        public Vector3 GetCurrentDuelForward()
        {
            Vector2 duelForward = CombatDuelMath.GetDuelForward(GetCurrentPlayerLocalPositionUnits(), GetCurrentMonsterLocalPositionUnits());
            return ((transform.right * duelForward.x) + (transform.forward * duelForward.y)).normalized;
        }

        public Vector3 GetCurrentPlayerRight()
        {
            Vector2 playerRight = CombatDuelMath.GetPlayerRight(GetCurrentPlayerLocalPositionUnits(), GetCurrentMonsterLocalPositionUnits());
            return ((transform.right * playerRight.x) + (transform.forward * playerRight.y)).normalized;
        }

        public void RefreshActorFacing()
        {
            Vector3 duelForward = GetCurrentDuelForward();

            if (playerActor != null)
            {
                playerActor.transform.forward = duelForward;
            }

            if (monsterActor != null)
            {
                monsterActor.transform.forward = -duelForward;
            }
        }

        public Vector3 GetPlayerDirectionWorld(PlayerRelativeDirection directionHint)
        {
            switch (directionHint)
            {
                case PlayerRelativeDirection.Left:
                    return -GetCurrentPlayerRight();
                case PlayerRelativeDirection.Right:
                    return GetCurrentPlayerRight();
                default:
                    return GetCurrentPlayerRight();
            }
        }

        public Vector3 GetPlayerResolvedWorldPosition(PlayerRelativeDirection directionHint, float units)
        {
            Vector2 targetLocalPosition = CombatDuelMath.ResolvePlayerDirectionTarget(
                GetCurrentPlayerLocalPositionUnits(),
                GetCurrentMonsterLocalPositionUnits(),
                directionHint,
                units);
            return ConvertDuelLocalToWorld(targetLocalPosition);
        }

        public Vector3 GetMonsterAdvanceWorldPosition(float units)
        {
            Vector3 forward = -GetCurrentDuelForward();
            return GetCurrentMonsterWorldPosition() + (forward * (units * unitSize));
        }

        public float GetCurrentBandUnits()
        {
            return CombatDuelMath.GetBandUnits(GetCurrentPlayerLocalPositionUnits(), GetCurrentMonsterLocalPositionUnits());
        }

        public bool TryResolveReadyMoveTarget(PatternStepDefinition stepDefinition, out Vector3 targetPosition)
        {
            targetPosition = GetCurrentMonsterWorldPosition();
            if (stepDefinition == null || stepDefinition.Phase != CombatEventPhase.Ready || stepDefinition.ReadyCueType != ReadyCueType.Move)
            {
                return false;
            }

            return TryResolveReadyMoveTarget(
                stepDefinition.MovementResolveMode,
                stepDefinition.MoveUnits,
                stepDefinition.TargetBandUnits,
                stepDefinition.MovementDirectionHint,
                out targetPosition);
        }

        public bool TryResolveReadyMoveTarget(
            MovementResolveMode movementResolveMode,
            float moveUnits,
            float targetBandUnits,
            MovementDirectionHint movementDirectionHint,
            out Vector3 targetPosition)
        {
            targetPosition = GetCurrentMonsterWorldPosition();
            if (!CombatDuelMath.TryResolveReadyMoveTarget(
                    GetCurrentPlayerLocalPositionUnits(),
                    GetCurrentMonsterLocalPositionUnits(),
                    movementResolveMode,
                    moveUnits,
                    targetBandUnits,
                    movementDirectionHint,
                    out Vector2 targetLocalPosition))
            {
                return false;
            }

            targetPosition = ConvertDuelLocalToWorld(targetLocalPosition);
            return true;
        }

        public Vector3 GetLockedPlayerWorldPositionForAttack()
        {
            return ConvertDuelLocalToWorld(GetCurrentPlayerLocalPositionUnits());
        }

        public void ApplyResolvedState(Vector2 playerLocalPositionUnits, Vector2 monsterLocalPositionUnits)
        {
            if (playerActor != null)
            {
                playerActor.transform.position = ConvertDuelLocalToWorld(playerLocalPositionUnits);
            }

            if (monsterActor != null)
            {
                monsterActor.transform.position = ConvertDuelLocalToWorld(monsterLocalPositionUnits);
            }

            RefreshActorFacing();
        }

        private void OnValidate()
        {
            unitSize = Mathf.Max(0.01f, unitSize);
            startDistanceUnits = Mathf.Max(0f, startDistanceUnits);
        }
    }
}
