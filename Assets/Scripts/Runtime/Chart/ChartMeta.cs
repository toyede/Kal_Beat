using System;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class ChartMeta
    {
        [SerializeField] private string songId = "new_song";
        [SerializeField] private string songName = "New Song";
        [SerializeField] private AudioClip previewAudioClip;
        [SerializeField] private string fmodEventPath = string.Empty;
        [SerializeField] private float bpm = 120f;
        [SerializeField] private float offsetSec;
        [SerializeField] private float songLengthSec = 60f;
        [SerializeField] private int gridDivision = 4;
        [SerializeField] private int laneCount = 4;
        [SerializeField] private int maxEventsPerSlot = 4;

        public string SongId
        {
            get => songId;
            set => songId = value;
        }

        public string SongName
        {
            get => songName;
            set => songName = value;
        }

        public AudioClip PreviewAudioClip
        {
            get => previewAudioClip;
            set => previewAudioClip = value;
        }

        public string FmodEventPath
        {
            get => fmodEventPath;
            set => fmodEventPath = value;
        }

        public float Bpm
        {
            get => bpm;
            set => bpm = value;
        }

        public float OffsetSec
        {
            get => offsetSec;
            set => offsetSec = value;
        }

        public float SongLengthSec
        {
            get => songLengthSec;
            set => songLengthSec = value;
        }

        public int GridDivision
        {
            get => gridDivision;
            set => gridDivision = value;
        }

        public int LaneCount
        {
            get => laneCount;
            set => laneCount = value;
        }

        public int MaxEventsPerSlot
        {
            get => maxEventsPerSlot;
            set => maxEventsPerSlot = value;
        }
    }
}
