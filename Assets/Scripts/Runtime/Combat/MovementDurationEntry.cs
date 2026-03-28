using System;
using UnityEngine;

namespace KalBeat.Combat
{
    [Serializable]
    public sealed class MovementDurationEntry
    {
        [SerializeField] private float units = 2f;
        [SerializeField] private float durationSec = 0.1f;

        public float Units
        {
            get => units;
            set => units = value;
        }

        public float DurationSec
        {
            get => durationSec;
            set => durationSec = value;
        }
    }
}
