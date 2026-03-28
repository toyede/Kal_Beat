using System;
using System.Collections.Generic;
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
        [SerializeField] private List<LaneBindingData> laneBindings = new List<LaneBindingData>();

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
            set
            {
                laneCount = Mathf.Max(1, value);
                SyncLaneBindingsWithLaneCount();
            }
        }

        public int MaxEventsPerSlot
        {
            get => maxEventsPerSlot;
            set => maxEventsPerSlot = value;
        }

        public List<LaneBindingData> LaneBindings => laneBindings;

        public void SyncLaneBindingsWithLaneCount()
        {
            laneBindings ??= new List<LaneBindingData>();

            int targetCount = Mathf.Max(1, laneCount);
            while (laneBindings.Count < targetCount)
            {
                laneBindings.Add(new LaneBindingData());
            }

            if (laneBindings.Count > targetCount)
            {
                laneBindings.RemoveRange(targetCount, laneBindings.Count - targetCount);
            }

            for (int i = 0; i < laneBindings.Count; i++)
            {
                if (laneBindings[i] == null)
                {
                    laneBindings[i] = new LaneBindingData();
                }
            }
        }
    }
}
