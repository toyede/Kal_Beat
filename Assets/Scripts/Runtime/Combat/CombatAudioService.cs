using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class CombatAudioService : MonoBehaviour
    {
        private readonly Dictionary<string, EventDescription> cachedDescriptions =
            new Dictionary<string, EventDescription>(StringComparer.Ordinal);

        public void WarmupEventPath(string eventPath)
        {
            if (TryGetEventDescription(eventPath, out EventDescription eventDescription))
            {
                eventDescription.loadSampleData();
            }
        }

        public void PlayOneShot(string eventPath, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return;
            }

            if (TryGetEventDescription(eventPath, out EventDescription eventDescription) &&
                eventDescription.createInstance(out EventInstance eventInstance) == FMOD.RESULT.OK &&
                eventInstance.isValid())
            {
                eventInstance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
                eventInstance.start();
                eventInstance.release();
                return;
            }

            RuntimeManager.PlayOneShot(eventPath, position);
        }

        private bool TryGetEventDescription(string eventPath, out EventDescription eventDescription)
        {
            eventDescription = default;
            if (string.IsNullOrWhiteSpace(eventPath))
            {
                return false;
            }

            string normalizedPath = eventPath.Trim();
            if (cachedDescriptions.TryGetValue(normalizedPath, out eventDescription) &&
                eventDescription.isValid())
            {
                return true;
            }

            if (RuntimeManager.StudioSystem.getEvent(normalizedPath, out eventDescription) != FMOD.RESULT.OK ||
                !eventDescription.isValid())
            {
                cachedDescriptions.Remove(normalizedPath);
                return false;
            }

            cachedDescriptions[normalizedPath] = eventDescription;
            return true;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<string, EventDescription> pair in cachedDescriptions)
            {
                if (pair.Value.isValid())
                {
                    pair.Value.unloadSampleData();
                }
            }

            cachedDescriptions.Clear();
        }
    }
}
