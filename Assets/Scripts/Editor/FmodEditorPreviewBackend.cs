using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace KalBeat.Charting.Editor
{
    internal sealed class FmodEditorPreviewBackend : IDisposable
    {
        private static readonly Dictionary<string, float> EmptyParameterValues = new Dictionary<string, float>();

        private EventInstance mainEventInstance;
        private string currentMainEventPath = string.Empty;
        private bool mainEventPaused;
        private float mainEventSpeed = 1f;
        private float mainEventVolume = 1f;

        internal bool IsMainPlaying(string eventPath)
        {
            if (!HasMainInstance(eventPath) || mainEventPaused)
            {
                return false;
            }

            return TryGetPlaybackState(out PLAYBACK_STATE playbackState) &&
                   (playbackState == PLAYBACK_STATE.PLAYING || playbackState == PLAYBACK_STATE.SUSTAINING);
        }

        internal bool HasMainInstance(string eventPath)
        {
            return mainEventInstance.isValid() &&
                   !string.IsNullOrWhiteSpace(eventPath) &&
                   string.Equals(currentMainEventPath, eventPath, StringComparison.OrdinalIgnoreCase);
        }

        internal bool CanPreviewEvent(string eventPath)
        {
            return ResolveEvent(eventPath) != null;
        }

        internal bool PlayMainEvent(string eventPath, float startTimeSec, float speed, float volume)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return false;
            }

            LoadPreviewBanks();

            if (HasMainInstance(eventPath))
            {
                SetMainSpeed(speed);
                SetMainVolume(volume);
                SetTimelinePosition(startTimeSec);
                FMOD.RESULT result = mainEventInstance.setPaused(false);
                if (result == FMOD.RESULT.OK)
                {
                    mainEventPaused = false;
                    return true;
                }

                return false;
            }

            StopMainEvent();
            mainEventSpeed = Mathf.Max(0.01f, speed);
            mainEventVolume = Mathf.Max(0f, volume);

            EditorEventRef eventRef = ResolveEvent(eventPath);
            if (eventRef == null)
            {
                return false;
            }

            mainEventInstance = EditorUtils.PreviewEvent(eventRef, EmptyParameterValues, mainEventVolume, Mathf.Max(0f, startTimeSec));
            if (!mainEventInstance.isValid())
            {
                currentMainEventPath = string.Empty;
                return false;
            }

            ApplyMainSpeed();
            ApplyMainVolume();
            currentMainEventPath = eventPath;
            mainEventPaused = false;
            return true;
        }

        internal void PauseMainEvent()
        {
            if (!mainEventInstance.isValid())
            {
                return;
            }

            if (mainEventInstance.setPaused(true) == FMOD.RESULT.OK)
            {
                mainEventPaused = true;
            }
        }

        internal void SeekMainEvent(string eventPath, float timeSec, bool keepPlaying)
        {
            if (HasMainInstance(eventPath))
            {
                ApplyMainSpeed();
                SetTimelinePosition(timeSec);
                mainEventInstance.setPaused(!keepPlaying);
                mainEventPaused = !keepPlaying;
                return;
            }

            if (keepPlaying)
            {
                PlayMainEvent(eventPath, timeSec, mainEventSpeed, mainEventVolume);
            }
        }

        internal void SetMainSpeed(float speed)
        {
            mainEventSpeed = Mathf.Max(0.01f, speed);

            if (mainEventInstance.isValid())
            {
                ApplyMainSpeed();
            }
        }

        internal void SetMainVolume(float volume)
        {
            mainEventVolume = Mathf.Max(0f, volume);

            if (mainEventInstance.isValid())
            {
                ApplyMainVolume();
            }
        }

        internal void StopMainEvent()
        {
            if (mainEventInstance.isValid())
            {
                EditorUtils.PreviewStop(mainEventInstance);
                mainEventInstance.clearHandle();
            }

            currentMainEventPath = string.Empty;
            mainEventPaused = false;
        }

        internal void SyncMainPlaybackState()
        {
            if (!mainEventInstance.isValid())
            {
                currentMainEventPath = string.Empty;
                mainEventPaused = false;
                return;
            }

            if (!TryGetPlaybackState(out PLAYBACK_STATE playbackState))
            {
                return;
            }

            if (playbackState == PLAYBACK_STATE.STOPPED)
            {
                mainEventInstance.clearHandle();
                currentMainEventPath = string.Empty;
                mainEventPaused = false;
            }
        }

        internal float GetMainTimelinePositionSec()
        {
            if (!mainEventInstance.isValid())
            {
                return 0f;
            }

            if (mainEventInstance.getTimelinePosition(out int positionMs) != FMOD.RESULT.OK)
            {
                return 0f;
            }

            return Mathf.Max(0f, positionMs / 1000f);
        }

        internal bool PlayOneShot(string eventPath, float volume)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return false;
            }

            LoadPreviewBanks();

            EditorEventRef eventRef = ResolveEvent(eventPath);
            if (eventRef == null)
            {
                return false;
            }

            EventInstance previewInstance = EditorUtils.PreviewEvent(eventRef, EmptyParameterValues, Mathf.Max(0f, volume));
            return previewInstance.isValid();
        }

        internal string FindEventPathByName(string shortName)
        {
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return string.Empty;
            }

            List<EditorEventRef> events = EventManager.Events;
            string normalizedName = shortName.Trim();
            string fallbackPath = string.Empty;

            for (int i = 0; i < events.Count; i++)
            {
                EditorEventRef eventRef = events[i];
                if (eventRef == null || string.IsNullOrWhiteSpace(eventRef.Path))
                {
                    continue;
                }

                if (fallbackPath.Length == 0 &&
                    eventRef.Path.IndexOf(normalizedName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    fallbackPath = eventRef.Path;
                }

                int lastSlashIndex = eventRef.Path.LastIndexOf('/');
                string eventName = lastSlashIndex >= 0 ? eventRef.Path.Substring(lastSlashIndex + 1) : eventRef.Path;
                if (string.Equals(eventName, normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    return eventRef.Path;
                }
            }

            return fallbackPath;
        }

        public void Dispose()
        {
            StopMainEvent();
        }

        private static EditorEventRef ResolveEvent(string eventPath)
        {
            return string.IsNullOrWhiteSpace(eventPath) ? null : EventManager.EventFromPath(eventPath);
        }

        private static void LoadPreviewBanks()
        {
            if (!EditorUtils.PreviewBanksLoaded)
            {
                EditorUtils.LoadPreviewBanks();
            }
        }

        private void SetTimelinePosition(float timeSec)
        {
            if (!mainEventInstance.isValid())
            {
                return;
            }

            int positionMs = Mathf.Max(0, Mathf.RoundToInt(timeSec * 1000f));
            mainEventInstance.setTimelinePosition(positionMs);
        }

        private void ApplyMainSpeed()
        {
            if (!mainEventInstance.isValid())
            {
                return;
            }

            mainEventInstance.setPitch(mainEventSpeed);
        }

        private void ApplyMainVolume()
        {
            if (!mainEventInstance.isValid())
            {
                return;
            }

            mainEventInstance.setVolume(mainEventVolume);
        }

        private bool TryGetPlaybackState(out PLAYBACK_STATE playbackState)
        {
            playbackState = PLAYBACK_STATE.STOPPED;
            return mainEventInstance.isValid() &&
                   mainEventInstance.getPlaybackState(out playbackState) == FMOD.RESULT.OK;
        }
    }
}
