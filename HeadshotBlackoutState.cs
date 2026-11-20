namespace ER2RealismOverhaul;

internal struct HeadshotBlackoutState
{
    private const double LethalHitWindow = 0.35;
    private const double HoldSeconds = 2.5;
    private const double FadeSeconds = 0.5;

    private int _hitSoldier;
    private double _hitAt;
    private int _victim;
    private double _startedAt;

    internal bool Active => _victim != 0;

    internal void RecordHeadHit(int soldier, double now)
    {
        _hitSoldier = soldier;
        _hitAt = now;
    }

    internal bool TryBegin(int soldier, double now)
    {
        if (soldier == 0 || soldier != _hitSoldier)
            return false;

        _hitSoldier = 0;
        if (soldier == _victim || now - _hitAt > LethalHitWindow)
            return false;

        _victim = soldier;
        _startedAt = now;
        return true;
    }

    internal float Opacity(double now, int liveControlledSoldier, bool squadPickerOpen)
    {
        if (!Active || squadPickerOpen || (liveControlledSoldier != 0 && liveControlledSoldier != _victim))
            return 0f;

        // Both phases use the same real-time deadline, including after a stalled frame.
        return (float)Math.Clamp((HoldSeconds + FadeSeconds - (now - _startedAt)) / FadeSeconds, 0d, 1d);
    }
}
