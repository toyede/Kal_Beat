using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    public static class CombatDuelMath
    {
        public static float GetBandUnits(Vector2 playerLocalPosition, Vector2 monsterLocalPosition)
        {
            return Vector2.Distance(playerLocalPosition, monsterLocalPosition);
        }

        public static Vector2 GetDuelForward(Vector2 playerLocalPosition, Vector2 monsterLocalPosition)
        {
            Vector2 forward = monsterLocalPosition - playerLocalPosition;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return Vector2.up;
            }

            return forward.normalized;
        }

        public static Vector2 GetPlayerRight(Vector2 playerLocalPosition, Vector2 monsterLocalPosition)
        {
            Vector2 duelForward = GetDuelForward(playerLocalPosition, monsterLocalPosition);
            Vector2 playerRight = new Vector2(duelForward.y, -duelForward.x);
            if (playerRight.sqrMagnitude <= 0.0001f)
            {
                return Vector2.right;
            }

            return playerRight.normalized;
        }

        public static Vector2 GetPlayerDirection(Vector2 playerLocalPosition, Vector2 monsterLocalPosition, PlayerRelativeDirection directionHint)
        {
            switch (directionHint)
            {
                case PlayerRelativeDirection.Left:
                    return -GetPlayerRight(playerLocalPosition, monsterLocalPosition);
                case PlayerRelativeDirection.Right:
                    return GetPlayerRight(playerLocalPosition, monsterLocalPosition);
                default:
                    return GetPlayerRight(playerLocalPosition, monsterLocalPosition);
            }
        }

        public static Vector2 ResolvePlayerDirectionTarget(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            PlayerRelativeDirection directionHint,
            float units)
        {
            return playerLocalPosition + (GetPlayerDirection(playerLocalPosition, monsterLocalPosition, directionHint) * units);
        }

        public static Vector2 ResolveForwardToBand(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            float targetBandUnits)
        {
            return playerLocalPosition + (GetDuelForward(playerLocalPosition, monsterLocalPosition) * targetBandUnits);
        }

        public static bool TryResolveReadyMoveTarget(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            MovementResolveMode movementResolveMode,
            float moveUnits,
            float targetBandUnits,
            MovementDirectionHint movementDirectionHint,
            out Vector2 targetLocalPosition)
        {
            targetLocalPosition = monsterLocalPosition;

            switch (movementResolveMode)
            {
                case MovementResolveMode.ForwardToBand:
                    targetLocalPosition = ResolveForwardToBand(playerLocalPosition, monsterLocalPosition, targetBandUnits);
                    return true;

                case MovementResolveMode.SideMoveOnBand:
                    return TryResolveSideMoveOnBand(
                        playerLocalPosition,
                        monsterLocalPosition,
                        moveUnits,
                        targetBandUnits,
                        movementDirectionHint,
                        out targetLocalPosition);

                default:
                    return false;
            }
        }

        public static bool TryResolveSideMoveOnBand(
            Vector2 playerLocalPosition,
            Vector2 monsterLocalPosition,
            float moveUnits,
            float targetBandUnits,
            MovementDirectionHint movementDirectionHint,
            out Vector2 targetLocalPosition)
        {
            targetLocalPosition = monsterLocalPosition;
            if (movementDirectionHint == MovementDirectionHint.None)
            {
                return false;
            }

            if (!TryGetCircleIntersections(
                    monsterLocalPosition,
                    moveUnits,
                    playerLocalPosition,
                    targetBandUnits,
                    out Vector2 intersectionA,
                    out Vector2 intersectionB))
            {
                return false;
            }

            Vector2 playerRight = GetPlayerRight(playerLocalPosition, monsterLocalPosition);
            float dotA = Vector2.Dot((intersectionA - playerLocalPosition).normalized, playerRight);
            float dotB = Vector2.Dot((intersectionB - playerLocalPosition).normalized, playerRight);

            bool chooseRight = movementDirectionHint == MovementDirectionHint.Right;
            targetLocalPosition = chooseRight
                ? (dotB >= dotA ? intersectionB : intersectionA)
                : (dotA <= dotB ? intersectionA : intersectionB);
            return true;
        }

        private static bool TryGetCircleIntersections(
            Vector2 centerA,
            float radiusA,
            Vector2 centerB,
            float radiusB,
            out Vector2 intersectionA,
            out Vector2 intersectionB)
        {
            intersectionA = default;
            intersectionB = default;

            float centerDistance = Vector2.Distance(centerA, centerB);
            if (centerDistance <= 0.0001f)
            {
                return false;
            }

            if (centerDistance > radiusA + radiusB)
            {
                return false;
            }

            if (centerDistance < Mathf.Abs(radiusA - radiusB))
            {
                return false;
            }

            float a = ((radiusA * radiusA) - (radiusB * radiusB) + (centerDistance * centerDistance)) / (2f * centerDistance);
            float hSquared = (radiusA * radiusA) - (a * a);
            if (hSquared < 0f)
            {
                if (hSquared > -0.0001f)
                {
                    hSquared = 0f;
                }
                else
                {
                    return false;
                }
            }

            float h = Mathf.Sqrt(hSquared);
            Vector2 direction = (centerB - centerA) / centerDistance;
            Vector2 midpoint = centerA + (direction * a);
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);

            intersectionA = midpoint + (perpendicular * h);
            intersectionB = midpoint - (perpendicular * h);
            return true;
        }
    }
}
