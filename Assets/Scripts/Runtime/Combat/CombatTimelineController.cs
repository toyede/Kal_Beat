using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class CombatTimelineController : MonoBehaviour
    {
        [SerializeField] private ChartData chart;
        [SerializeField] private CombatDuelRoot duelRoot;
        [SerializeField] private PlayerCombatActor playerActor;
        [SerializeField] private MonsterCombatActor monsterActor;
        [SerializeField] private CombatJudgementSystem judgementSystem;
        [SerializeField] private CombatAudioService audioService;
        [SerializeField] private PlayerAttackDefinition fallbackPlayerRangedAttackDefinition;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private string judgementInputCueEventPath = "event:/preview/finger_snap";
        [SerializeField] private float sfxLeadMs;
        [SerializeField] private bool enableJudgementLogs = true;

        private readonly List<ResolvedCombatStep> runtimeMonsterSteps = new List<ResolvedCombatStep>();
        private readonly List<ResolvedCombatStep> runtimePlayerSteps = new List<ResolvedCombatStep>();
        private readonly List<ResolvedCombatStep> runtimeReadyCueSteps = new List<ResolvedCombatStep>();
        private readonly CombatRhythmClock rhythmClock = new CombatRhythmClock();
        private EventInstance musicEventInstance;
        private int nextMonsterStartIndex;
        private int nextPlayerStartIndex;
        private int nextReadyCueIndex;
        private bool playbackStarted;
        private bool runtimeTimelineReady;

        private void Start()
        {
            ResolveReferences();
            SubscribeJudgementSystem();
            BuildRuntimeTimeline();
            ResetCombatState();

            if (playOnStart)
            {
                StartPlayback();
            }
        }

        private void Update()
        {
            if (!playbackStarted || !musicEventInstance.isValid())
            {
                return;
            }

            if (musicEventInstance.getTimelinePosition(out int timelinePositionMs) != FMOD.RESULT.OK)
            {
                return;
            }

            float currentTimeSec = timelinePositionMs * 0.001f;
            rhythmClock.SetTimelineTime(currentTimeSec);
            judgementSystem?.SetClockSample(currentTimeSec);

            while (nextPlayerStartIndex < runtimePlayerSteps.Count)
            {
                ResolvedCombatStep resolvedStep = runtimePlayerSteps[nextPlayerStartIndex];
                if (resolvedStep == null)
                {
                    nextPlayerStartIndex++;
                    continue;
                }

                if (resolvedStep.PlayerStartSec > currentTimeSec + 0.0001f)
                {
                    break;
                }

                if (playerActor != null)
                {
                    playerActor.PlayResolvedApproachMove(
                        resolvedStep.PlayerApproachLocalPos,
                        resolvedStep.PlayerStartSec,
                        resolvedStep.PlayerDurationSec);
                }

                nextPlayerStartIndex++;
            }

            ProcessJudgementInput(currentTimeSec);
            judgementSystem?.UpdateAutoMiss(currentTimeSec);

            while (nextMonsterStartIndex < runtimeMonsterSteps.Count)
            {
                ResolvedCombatStep resolvedStep = runtimeMonsterSteps[nextMonsterStartIndex];
                if (resolvedStep == null)
                {
                    nextMonsterStartIndex++;
                    continue;
                }

                if (resolvedStep.MonsterStartSec > currentTimeSec + 0.0001f)
                {
                    break;
                }

                monsterActor?.HandleResolvedStepStart(resolvedStep);
                nextMonsterStartIndex++;
            }

            float sfxLeadSec = Mathf.Max(0f, sfxLeadMs * 0.001f);
            while (nextReadyCueIndex < runtimeReadyCueSteps.Count)
            {
                ResolvedCombatStep resolvedStep = runtimeReadyCueSteps[nextReadyCueIndex];
                if (resolvedStep == null)
                {
                    nextReadyCueIndex++;
                    continue;
                }

                if (resolvedStep.HitSec - sfxLeadSec > currentTimeSec + 0.0001f)
                {
                    break;
                }

                PlayReadyCue(resolvedStep);
                nextReadyCueIndex++;
            }
        }

        private void ProcessJudgementInput(float currentTimeSec)
        {
            if (judgementSystem == null)
            {
                return;
            }

            while (judgementSystem.TryConsumeInput(currentTimeSec, out float inputTimelineSec))
            {
                judgementSystem.JudgeInput(inputTimelineSec);
            }
        }

        private void OnDisable()
        {
            StopPlayback();
            UnsubscribeJudgementSystem();
        }

        private void OnDestroy()
        {
            StopPlayback();
            UnsubscribeJudgementSystem();
        }

        public void RestartPlayback()
        {
            StopPlayback();
            BuildRuntimeTimeline();
            ResetCombatState();
            StartPlayback();
        }

        private void ResolveReferences()
        {
            if (duelRoot != null)
            {
                if (playerActor == null)
                {
                    playerActor = duelRoot.PlayerActor;
                }

                if (monsterActor == null)
                {
                    monsterActor = duelRoot.MonsterActor;
                }
            }

            if (judgementSystem == null)
            {
                judgementSystem = GetComponent<CombatJudgementSystem>();
            }

            if (audioService == null)
            {
                audioService = GetComponent<CombatAudioService>();
            }

            if (audioService == null)
            {
                audioService = gameObject.AddComponent<CombatAudioService>();
            }
        }

        private void ResetCombatState()
        {
            if (duelRoot != null)
            {
                duelRoot.ResetLayout(rhythmClock);
            }
            else if (playerActor != null)
            {
                playerActor.ResetCombatState();
            }
            else if (monsterActor != null)
            {
                monsterActor.ResetCombatState();
            }

            if (duelRoot != null && runtimeMonsterSteps.Count > 0)
            {
                ResolvedCombatStep firstStep = runtimeMonsterSteps[0];
                duelRoot.ApplyResolvedState(firstStep.StartPlayerLocalPos, firstStep.StartMonsterLocalPos);
            }

            judgementSystem?.ResetState();
        }

        private void BuildRuntimeTimeline()
        {
            runtimeMonsterSteps.Clear();
            runtimePlayerSteps.Clear();
            runtimeReadyCueSteps.Clear();
            nextMonsterStartIndex = 0;
            nextPlayerStartIndex = 0;
            nextReadyCueIndex = 0;
            runtimeTimelineReady = false;
            judgementSystem?.Initialize(null);

            if (chart == null || chart.Meta == null || chart.ResolvedTimeline == null || monsterActor == null || monsterActor.Definition == null)
            {
                return;
            }

            if (!chart.ResolvedTimeline.IsValid)
            {
                Debug.LogWarning($"CombatTimelineController could not build runtime timeline: {chart.ResolvedTimeline.InvalidReason}", this);
                return;
            }

            string currentSignature = ChartResolvedTimelineBuilder.BuildSourceSignature(chart);
            if (!string.Equals(chart.ResolvedTimeline.SourceSignature, currentSignature, StringComparison.Ordinal))
            {
                Debug.LogWarning("CombatTimelineController blocked playback because ResolvedTimeline is stale. Save the chart to regenerate it.", this);
                return;
            }

            string targetMonsterId = monsterActor.Definition.MonsterId;
            string targetMonsterSignature = monsterActor.Definition.BuildRuntimeSignature();
            List<ResolvedCombatStep> resolvedSteps = chart.ResolvedTimeline.Steps;
            if (resolvedSteps == null)
            {
                return;
            }

            for (int i = 0; i < resolvedSteps.Count; i++)
            {
                ResolvedCombatStep resolvedStep = resolvedSteps[i];
                if (resolvedStep == null || !string.Equals(resolvedStep.MonsterId, targetMonsterId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(resolvedStep.MonsterDefinitionSignature, targetMonsterSignature, StringComparison.Ordinal))
                {
                    Debug.LogWarning("CombatTimelineController blocked playback because MonsterDefinition changed after ResolvedTimeline generation. Save the chart again.", this);
                    runtimeMonsterSteps.Clear();
                    return;
                }

                runtimeMonsterSteps.Add(resolvedStep);

                if (ShouldPlayReadyCue(resolvedStep))
                {
                    runtimeReadyCueSteps.Add(resolvedStep);
                }

                if (ShouldStartPlayerStep(resolvedStep))
                {
                    runtimePlayerSteps.Add(resolvedStep);
                }
            }

            runtimeMonsterSteps.Sort(CompareResolvedMonsterStarts);
            runtimePlayerSteps.Sort(CompareResolvedPlayerStarts);
            runtimeReadyCueSteps.Sort(CompareResolvedReadyCueHits);
            WarmupRuntimeAudio();
            judgementSystem?.Initialize(runtimeMonsterSteps);
            runtimeTimelineReady = true;
        }

        private void StartPlayback()
        {
            if (!runtimeTimelineReady)
            {
                Debug.LogWarning("CombatTimelineController blocked playback because ResolvedTimeline is not ready.", this);
                return;
            }

            if (chart == null || chart.Meta == null || string.IsNullOrWhiteSpace(chart.Meta.FmodEventPath))
            {
                Debug.LogWarning("CombatTimelineController could not start because ChartData or FMOD event path is missing.", this);
                return;
            }

            StopPlayback();

            musicEventInstance = RuntimeManager.CreateInstance(chart.Meta.FmodEventPath);
            if (!musicEventInstance.isValid())
            {
                Debug.LogWarning($"CombatTimelineController could not create FMOD event instance: {chart.Meta.FmodEventPath}", this);
                return;
            }

            if (musicEventInstance.start() != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"CombatTimelineController could not start FMOD event: {chart.Meta.FmodEventPath}", this);
                musicEventInstance.release();
                musicEventInstance = default;
                return;
            }

            rhythmClock.SetTimelineTime(0f);
            judgementSystem?.SetClockSample(0f);
            playbackStarted = true;
        }

        private void StopPlayback()
        {
            if (musicEventInstance.isValid())
            {
                musicEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                musicEventInstance.release();
            }

            musicEventInstance = default;
            playbackStarted = false;
            rhythmClock.Reset();
        }

        private void PlayReadyCue(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep == null || string.IsNullOrWhiteSpace(resolvedStep.ReadyCueEventPath))
            {
                Debug.LogError(
                    "Ready cue FMOD path is missing from ResolvedCombatStep. Define ReadyCueEventPath on the source PatternDefinition and save the chart again.",
                    this);
                return;
            }

            Vector3 cuePosition = monsterActor != null
                ? monsterActor.transform.position
                : Vector3.zero;

            if (duelRoot != null)
            {
                cuePosition = duelRoot.ConvertDuelLocalToWorld(resolvedStep.EndMonsterLocalPos);
            }

            PlayCombatSfx(resolvedStep.ReadyCueEventPath, cuePosition);
        }

        private void PlayJudgementInputCue()
        {
            if (string.IsNullOrWhiteSpace(judgementInputCueEventPath))
            {
                return;
            }

            Vector3 cuePosition = playerActor != null
                ? playerActor.transform.position
                : Vector3.zero;
            PlayCombatSfx(judgementInputCueEventPath, cuePosition);
        }

        private void WarmupRuntimeAudio()
        {
            if (audioService == null)
            {
                return;
            }

            audioService.WarmupEventPath(judgementInputCueEventPath);
            for (int i = 0; i < runtimeReadyCueSteps.Count; i++)
            {
                ResolvedCombatStep resolvedStep = runtimeReadyCueSteps[i];
                if (resolvedStep != null)
                {
                    audioService.WarmupEventPath(resolvedStep.ReadyCueEventPath);
                }
            }
        }

        private void HandleInputFeedbackRequested()
        {
            if (!playbackStarted)
            {
                return;
            }

            PlayJudgementInputCue();
        }

        private void PlayCombatSfx(string eventPath, Vector3 position)
        {
            if (audioService != null)
            {
                audioService.PlayOneShot(eventPath, position);
                return;
            }

            RuntimeManager.PlayOneShot(eventPath, position);
        }

        private void OnValidate()
        {
            sfxLeadMs = Mathf.Max(0f, sfxLeadMs);
        }

        private void SubscribeJudgementSystem()
        {
            if (judgementSystem != null)
            {
                judgementSystem.JudgementResolved -= HandleJudgementResolved;
                judgementSystem.InputFeedbackRequested -= HandleInputFeedbackRequested;
                judgementSystem.JudgementResolved += HandleJudgementResolved;
                judgementSystem.InputFeedbackRequested += HandleInputFeedbackRequested;
            }
        }

        private void UnsubscribeJudgementSystem()
        {
            if (judgementSystem != null)
            {
                judgementSystem.JudgementResolved -= HandleJudgementResolved;
                judgementSystem.InputFeedbackRequested -= HandleInputFeedbackRequested;
            }
        }

        private void HandleJudgementResolved(CombatJudgementResult result)
        {
            ApplyJudgementReaction(result);

            if (!enableJudgementLogs)
            {
                return;
            }

            string deltaText = result.HasDelta ? $"{result.DeltaMs:+0.0;-0.0;0.0}ms" : "n/a";
            string beatText = result.ResolvedStep != null ? result.ResolvedStep.SourceBeat.ToString("0.###") : "n/a";
            Debug.Log($"[Judgement] {result.Grade} | delta={deltaText} | beat={beatText}", this);
        }

        private void ApplyJudgementReaction(CombatJudgementResult result)
        {
            if (result.ResolvedStep == null)
            {
                return;
            }

            switch (result.Grade)
            {
                case CombatJudgementGrade.Perfect:
                case CombatJudgementGrade.Good:
                case CombatJudgementGrade.Miss:
                    if (result.ResolvedStep.Kind == CombatEventKind.PlayerRangedAttack)
                    {
                        ApplyPlayerRangedAttackJudgement(result);
                        return;
                    }

                    if (playerActor != null)
                    {
                        playerActor.PlayResolvedReactionMove(
                            result.ResolvedStep.EndPlayerLocalPos,
                            result.ResolvedStep.PlayerDurationSec,
                            result.Grade);
                    }

                    monsterActor?.HandleResolvedJudgement(result.ResolvedStep, result.Grade);
                    break;
            }
        }

        private void ApplyPlayerRangedAttackJudgement(CombatJudgementResult result)
        {
            if (playerActor != null)
            {
                playerActor.PlayResolvedReactionMove(
                    result.ResolvedStep.EndPlayerLocalPos,
                    result.ResolvedStep.PlayerDurationSec,
                    result.Grade);
            }

            if (result.Grade == CombatJudgementGrade.Perfect ||
                result.Grade == CombatJudgementGrade.Good)
            {
                playerActor?.FireRangedAttack(ResolvePlayerRangedAttackDefinition(), monsterActor != null ? monsterActor.HitPoint : null);
            }

            monsterActor?.HandleResolvedJudgement(result.ResolvedStep, result.Grade);
        }

        private PlayerAttackDefinition ResolvePlayerRangedAttackDefinition()
        {
            return fallbackPlayerRangedAttackDefinition;
        }

        private static bool ShouldPlayReadyCue(ResolvedCombatStep resolvedStep)
        {
            return resolvedStep != null &&
                   resolvedStep.Phase == CombatEventPhase.Ready &&
                   (resolvedStep.ReadyCueType == ReadyCueType.Pose ||
                    resolvedStep.ReadyCueType == ReadyCueType.Move ||
                    resolvedStep.Kind == CombatEventKind.PlayerDashAttack ||
                    resolvedStep.Kind == CombatEventKind.PlayerRangedAttack ||
                    resolvedStep.Kind == CombatEventKind.RangedThreatCounterable ||
                    resolvedStep.Kind == CombatEventKind.RangedThreatDodgeOnly);
        }

        private static bool ShouldStartPlayerStep(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep == null || resolvedStep.PlayerDurationSec <= 0f)
            {
                return false;
            }

            return (resolvedStep.Kind == CombatEventKind.PlayerDashAttack &&
                    resolvedStep.Phase == CombatEventPhase.Timing) ||
                   (resolvedStep.Kind == CombatEventKind.RangedThreatCounterable &&
                    resolvedStep.Phase == CombatEventPhase.Ready) ||
                   (resolvedStep.Kind == CombatEventKind.RangedThreatDodgeOnly &&
                    resolvedStep.Phase == CombatEventPhase.Ready);
        }

        private static int CompareResolvedMonsterStarts(ResolvedCombatStep left, ResolvedCombatStep right)
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

            int timeCompare = left.MonsterStartSec.CompareTo(right.MonsterStartSec);
            if (timeCompare != 0)
            {
                return timeCompare;
            }

            int laneCompare = left.Lane.CompareTo(right.Lane);
            if (laneCompare != 0)
            {
                return laneCompare;
            }

            int phaseCompare = left.Phase.CompareTo(right.Phase);
            if (phaseCompare != 0)
            {
                return phaseCompare;
            }

            return left.StepIndex.CompareTo(right.StepIndex);
        }

        private static int CompareResolvedReadyCueHits(ResolvedCombatStep left, ResolvedCombatStep right)
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

            int timeCompare = left.HitSec.CompareTo(right.HitSec);
            if (timeCompare != 0)
            {
                return timeCompare;
            }

            int laneCompare = left.Lane.CompareTo(right.Lane);
            if (laneCompare != 0)
            {
                return laneCompare;
            }

            return left.StepIndex.CompareTo(right.StepIndex);
        }

        private static int CompareResolvedPlayerStarts(ResolvedCombatStep left, ResolvedCombatStep right)
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

            int timeCompare = left.PlayerStartSec.CompareTo(right.PlayerStartSec);
            if (timeCompare != 0)
            {
                return timeCompare;
            }

            int laneCompare = left.Lane.CompareTo(right.Lane);
            if (laneCompare != 0)
            {
                return laneCompare;
            }

            return left.StepIndex.CompareTo(right.StepIndex);
        }

    }
}
