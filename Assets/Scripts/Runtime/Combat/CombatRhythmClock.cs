namespace KalBeat.Combat
{
    public sealed class CombatRhythmClock
    {
        public float NowSec { get; private set; }
        public bool HasTimelineTime { get; private set; }

        public void SetTimelineTime(float timeSec)
        {
            NowSec = timeSec < 0f ? 0f : timeSec;
            HasTimelineTime = true;
        }

        public void Reset()
        {
            NowSec = 0f;
            HasTimelineTime = false;
        }
    }
}
