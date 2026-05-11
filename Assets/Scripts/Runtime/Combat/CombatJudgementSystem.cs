using System;
using System.Collections.Generic;
using KalBeat.Charting;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KalBeat.Combat
{
    public sealed class CombatJudgementSystem : MonoBehaviour
    {
        [SerializeField] private float perfectWindowMs = 35f;
        [SerializeField] private float goodWindowMs = 80f;
        [SerializeField] private float missWindowMs = 130f;
        [SerializeField] private float inputOffsetMs;
#if ENABLE_INPUT_SYSTEM
        [SerializeField] private InputActionReference inputActionReference;
#endif

        private readonly List<JudgementTargetState> targets = new List<JudgementTargetState>();
#if ENABLE_INPUT_SYSTEM
        private readonly Queue<double> pendingInputSystemTimes = new Queue<double>();
#endif
        private float lastClockTimelineSec;
        private double lastClockRealtimeSec;
        private bool hasClockSample;
#if ENABLE_INPUT_SYSTEM
        private InputAction subscribedInputAction;
        private bool inputActionEnabledBySystem;
        private bool missingInputActionWarningLogged;
#endif

        public event Action<CombatJudgementResult> JudgementResolved;
        public event Action InputFeedbackRequested;

        public float PerfectWindowMs => perfectWindowMs;
        public float GoodWindowMs => goodWindowMs;
        public float MissWindowMs => missWindowMs;
        public float InputOffsetMs => inputOffsetMs;

#if ENABLE_INPUT_SYSTEM
        private void OnEnable()
        {
            SubscribeInputAction();
        }

        private void OnDisable()
        {
            UnsubscribeInputAction();
            pendingInputSystemTimes.Clear();
        }
#endif

        public void Initialize(List<ResolvedCombatStep> resolvedSteps)
        {
            targets.Clear();
#if ENABLE_INPUT_SYSTEM
            SubscribeInputAction();
            pendingInputSystemTimes.Clear();
#endif
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

#if ENABLE_INPUT_SYSTEM
            pendingInputSystemTimes.Clear();
#endif
        }

        public void SetClockSample(float timelineSec)
        {
            lastClockTimelineSec = Mathf.Max(0f, timelineSec);
            lastClockRealtimeSec = Time.realtimeSinceStartupAsDouble;
            hasClockSample = true;
        }

        public bool TryConsumeInput(float fallbackTimelineSec, out float inputTimelineSec)
        {
#if ENABLE_INPUT_SYSTEM
            if (pendingInputSystemTimes.Count > 0)
            {
                inputTimelineSec = Mathf.Max(0f, ConvertInputSystemTimeToTimeline(pendingInputSystemTimes.Dequeue()) + (inputOffsetMs * 0.001f));
                return true;
            }
#endif

            inputTimelineSec = 0f;
            return false;
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

#if ENABLE_INPUT_SYSTEM
        private void SubscribeInputAction()
        {
            InputAction action = inputActionReference != null ? inputActionReference.action : null;
            if (action == null)
            {
                if (!missingInputActionWarningLogged)
                {
                    Debug.LogError("CombatJudgementSystem requires an InputActionReference. Assign Combat/Judgement to enable rhythm input.", this);
                    missingInputActionWarningLogged = true;
                }

                return;
            }

            if (subscribedInputAction == action)
            {
                return;
            }

            UnsubscribeInputAction();
            subscribedInputAction = action;
            subscribedInputAction.performed += HandleInputActionPerformed;
            if (!subscribedInputAction.enabled)
            {
                subscribedInputAction.Enable();
                inputActionEnabledBySystem = true;
            }
        }

        private void UnsubscribeInputAction()
        {
            if (subscribedInputAction == null)
            {
                return;
            }

            subscribedInputAction.performed -= HandleInputActionPerformed;
            if (inputActionEnabledBySystem)
            {
                subscribedInputAction.Disable();
            }

            subscribedInputAction = null;
            inputActionEnabledBySystem = false;
        }

        private void HandleInputActionPerformed(InputAction.CallbackContext context)
        {
            pendingInputSystemTimes.Enqueue(context.time);
            InputFeedbackRequested?.Invoke();
        }

        private float ConvertInputSystemTimeToTimeline(double inputSystemTimeSec)
        {
            if (!hasClockSample)
            {
                return Mathf.Max(0f, lastClockTimelineSec);
            }

            double realtimeDeltaSec = Math.Max(0.0, lastClockRealtimeSec - inputSystemTimeSec);
            return Mathf.Max(0f, lastClockTimelineSec - (float)realtimeDeltaSec);
        }
#endif

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
