using System;
using System.Collections.Generic;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class CombatJudgementSystem : MonoBehaviour
    {
        [SerializeField] private KeyCode inputKey = KeyCode.Space;
        [SerializeField] private float perfectWindowMs = 35f;
        [SerializeField] private float goodWindowMs = 80f;
        [SerializeField] private float missWindowMs = 130f;

        private readonly List<JudgementTargetState> targets = new List<JudgementTargetState>();

        public event Action<CombatJudgementResult> JudgementResolved;

        public float PerfectWindowMs => perfectWindowMs;
        public float GoodWindowMs => goodWindowMs;
        public float MissWindowMs => missWindowMs;

        public void Initialize(List<ResolvedCombatStep> resolvedSteps)
        {
            targets.Clear();
            if (resolvedSteps == null)
            {
                return;
            }

            for (int i = 0; i < resolvedSteps.Count; i++)
            {
                ResolvedCombatStep resolvedStep = resolvedSteps[i];
                if (resolvedStep == null || resolvedStep.Phase != CombatEventPhase.Timing)
                {
                    continue;
                }

                targets.Add(new JudgementTargetState(resolvedStep));
            }

            targets.Sort(CompareTargets);
        }

        public void ResetState()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].IsConsumed = false;
            }
        }

        public bool IsInputPressedThisFrame()
        {
            return Input.GetKeyDown(inputKey);
        }

        public void UpdateAutoMiss(float currentTimeSec)
        {
            float missWindowSec = missWindowMs * 0.001f;
            for (int i = 0; i < targets.Count; i++)
            {
                JudgementTargetState target = targets[i];
                if (target.IsConsumed)
                {
                    continue;
                }

                if (currentTimeSec <= target.Step.HitSec + missWindowSec)
                {
                    break;
                }

                target.IsConsumed = true;
                float deltaMs = (currentTimeSec - target.Step.HitSec) * 1000f;
                EmitResult(new CombatJudgementResult(
                    CombatJudgementGrade.Miss,
                    deltaMs,
                    true,
                    true,
                    target.Step));
            }
        }

        public CombatJudgementResult JudgeInput(float currentTimeSec)
        {
            JudgementTargetState closestTarget = null;
            float closestAbsDeltaMs = float.MaxValue;

            for (int i = 0; i < targets.Count; i++)
            {
                JudgementTargetState candidate = targets[i];
                if (candidate.IsConsumed)
                {
                    continue;
                }

                float candidateDeltaMs = (currentTimeSec - candidate.Step.HitSec) * 1000f;
                float candidateAbsDeltaMs = Mathf.Abs(candidateDeltaMs);
                if (candidateAbsDeltaMs < closestAbsDeltaMs)
                {
                    closestAbsDeltaMs = candidateAbsDeltaMs;
                    closestTarget = candidate;
                }
            }

            if (closestTarget == null)
            {
                CombatJudgementResult emptyIgnore = new CombatJudgementResult(
                    CombatJudgementGrade.Ignore,
                    0f,
                    false,
                    false,
                    null);
                EmitResult(emptyIgnore);
                return emptyIgnore;
            }

            float deltaMs = (currentTimeSec - closestTarget.Step.HitSec) * 1000f;
            float absDeltaMs = Mathf.Abs(deltaMs);

            CombatJudgementGrade grade = CombatJudgementGrade.Ignore;
            bool consumeTarget = false;
            if (absDeltaMs <= perfectWindowMs)
            {
                grade = CombatJudgementGrade.Perfect;
                consumeTarget = true;
            }
            else if (absDeltaMs <= goodWindowMs)
            {
                grade = CombatJudgementGrade.Good;
                consumeTarget = true;
            }
            else if (deltaMs > goodWindowMs && deltaMs <= missWindowMs)
            {
                grade = CombatJudgementGrade.Miss;
                consumeTarget = true;
            }

            if (consumeTarget)
            {
                closestTarget.IsConsumed = true;
            }

            CombatJudgementResult result = new CombatJudgementResult(
                grade,
                deltaMs,
                true,
                false,
                closestTarget.Step);
            EmitResult(result);
            return result;
        }

        private void EmitResult(CombatJudgementResult result)
        {
            JudgementResolved?.Invoke(result);
        }

        private void OnValidate()
        {
            perfectWindowMs = Mathf.Max(0f, perfectWindowMs);
            goodWindowMs = Mathf.Max(perfectWindowMs, goodWindowMs);
            missWindowMs = Mathf.Max(goodWindowMs, missWindowMs);
        }

        private static int CompareTargets(JudgementTargetState left, JudgementTargetState right)
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

            int hitCompare = left.Step.HitSec.CompareTo(right.Step.HitSec);
            if (hitCompare != 0)
            {
                return hitCompare;
            }

            int laneCompare = left.Step.Lane.CompareTo(right.Step.Lane);
            if (laneCompare != 0)
            {
                return laneCompare;
            }

            return left.Step.StepIndex.CompareTo(right.Step.StepIndex);
        }

        private sealed class JudgementTargetState
        {
            public JudgementTargetState(ResolvedCombatStep step)
            {
                Step = step;
            }

            public ResolvedCombatStep Step { get; }
            public bool IsConsumed { get; set; }
        }
    }
}
