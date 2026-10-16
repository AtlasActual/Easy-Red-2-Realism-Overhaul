using System;

namespace ER2RealismOverhaul;

/// <summary>
/// Bounds work while rotating admission through repeated native update waves.
/// Always admitting the first N calls can indefinitely defer the tail of a
/// wave that arrives in the same order each frame. No soldier references or
/// deferred native work survive an update; only the admission window rotates.
/// </summary>
internal sealed class RotatingFrameBudgetCore
{
    private readonly int _capacity;
    private int _frame = -1;
    private int _arrivals;
    private int _expectedArrivals;
    private int _start;
    private int _used;

    internal RotatingFrameBudgetCore(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal bool TryTake(int frame)
    {
        if (frame != _frame)
        {
            _expectedArrivals = _arrivals;
            _start = _expectedArrivals > _capacity
                ? (_start + _capacity) % _expectedArrivals
                : 0;
            _frame = frame;
            _arrivals = 0;
            _used = 0;
        }

        var offset = _arrivals++ - _start;
        if (offset < 0)
            offset += _expectedArrivals;
        if (offset < 0 || offset >= _capacity || _used >= _capacity)
            return false;

        _used++;
        return true;
    }

    internal void Clear()
    {
        _frame = -1;
        _arrivals = 0;
        _expectedArrivals = 0;
        _start = 0;
        _used = 0;
    }
}
