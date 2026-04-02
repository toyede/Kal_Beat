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
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private string readyCueEventPath = "event:/preview/finger_snap";

        private readonly List<ResolvedCombatStep> runtimeMonsterSteps = new List<ResolvedCombatStep>();
        private readonly List<ResolvedCombatStep> runtimePlayerSteps = new List<ResolvedCombatStep>();
        private readonly List<ResolvedCombatStep> runtimeReadyCueSteps = new List<ResolvedCombatStep>();
        private EventInstance musicEventInstance;
        private int nextMonsterStartIndex;
        private int nextPlayerStartIndex;
        private int nextReadyCueIndex;
        private bool playbackStarted;
        private bool runtimeTimelineReady;

        private void Start()
        {
            ResolveReferences();
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

                if (playerActor != null && resolvedStep.PlayerDurationSec > 0f)
                {
                    playerActor.PlayResolvedHitMove(resolvedStep.EndPlayerLocalPos, resolvedStep.PlayerDurationSec);
                }

                nextPlayerStartIndex++;
            }

            while (nextReadyCueIndex < runtimeReadyCueSteps.Count)
            {
                ResolvedCombatStep resolvedStep = runtimeReadyCueSteps[nextReadyCueIndex];
                if (resolvedStep == null)
                {
                    nextReadyCueIndex++;
                    continue;
                }

                if (resolvedStep.HitSec > currentTimeSec + 0.0001f)
                {
                    break;
                }

                PlayReadyCue(resolvedStep);
                nextReadyCueIndex++;
            }
        }

        private void OnDisable()
        {
            StopPlayback();
        }

        private void OnDestroy()
        {
            StopPlayback();
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
        }

        private void ResetCombatState()
        {
            if (duelRoot != null)
            {
                duelRoot.ResetLayout();
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
                    runtimePlayerSteps.Clear();
                    return;
                }

                runtimeMonsterSteps.Add(resolvedStep);
                if (resolvedStep.PlayerDurationSec > 0f)
                {
                    runtimePlayerSteps.Add(resolvedStep);
                }

                if (ShouldPlayReadyCue(resolvedStep))
                {
                    runtimeReadyCueSteps.Add(resolvedStep);
                }
            }

            runtimeMonsterSteps.Sort(CompareResolvedMonsterStarts);
            runtimePlayerSteps.Sort(CompareResolvedPlayerStarts);
            runtimeReadyCueSteps.Sort(CompareResolvedReadyCueHits);
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
        }

        private void PlayReadyCue(ResolvedCombatStep resolvedStep)
        {
            if (string.IsNullOrWhiteSpace(readyCueEventPath))
            {
                return;
            }

            Vector3 cuePosition = monsterActor != null
                ? monsterActor.transform.position
                : Vector3.zero;

            if (duelRoot != null)
            {
                cuePosition = duelRoot.ConvertDuelLocalToWorld(resolvedStep.EndMonsterLocalPos);
            }

            RuntimeManager.PlayOneShot(readyCueEventPath, cuePosition);
        }

        private static bool ShouldPlayReadyCue(ResolvedCombatStep resolvedStep)
        {
            return resolvedStep != null &&
                   resolvedStep.Phase == CombatEventPhase.Ready &&
                   (resolvedStep.ReadyCueType == ReadyCueType.Pose || resolvedStep.ReadyCueType == ReadyCueType.Move);
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
