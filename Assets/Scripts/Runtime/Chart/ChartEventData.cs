using System;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class ChartEventData
    {
        [SerializeField] private float beat;
        [SerializeField] private int lane;
        [SerializeField] private ChartEventType eventType = ChartEventType.AttackTiming;
        [SerializeField] private MonsterActionKind monsterActionKind = MonsterActionKind.None;
        [SerializeField] private CameraCueKind cameraCueKind = CameraCueKind.None;
        [SerializeField] private CameraMovingKind cameraMovingKind = CameraMovingKind.None;
        [SerializeField] private float nudgeMs;

        public float Beat
        {
            get => beat;
            set => beat = value;
        }

        public int Lane
        {
            get => lane;
            set => lane = value;
        }

        public ChartEventType EventType
        {
            get => eventType;
            set => eventType = value;
        }

        public MonsterActionKind MonsterActionKind
        {
            get => monsterActionKind;
            set => monsterActionKind = value;
        }

        public CameraCueKind CameraCueKind
        {
            get => cameraCueKind;
            set => cameraCueKind = value;
        }

        public CameraMovingKind CameraMovingKind
        {
            get => cameraMovingKind;
            set => cameraMovingKind = value;
        }

        public float NudgeMs
        {
            get => nudgeMs;
            set => nudgeMs = value;
        }
    }
}
