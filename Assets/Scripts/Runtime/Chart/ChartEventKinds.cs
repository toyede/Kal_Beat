namespace KalBeat.Charting
{
    public enum MonsterActionKind
    {
        None = 0,
        MoveToNextMarker = 1,
        AttackPlayer = 2,
        ReturnToOrigin = 3
    }

    public enum CameraCueKind
    {
        None = 0,
        BackQuarter = 1,
        CloseUp = 2,
        TwoShot = 3,
        Aerial = 4
    }

    public enum CameraMovingKind
    {
        None = 0,
        ShakeSmall = 1,
        PanSlow = 2,
        OrbitShort = 3
    }
}
