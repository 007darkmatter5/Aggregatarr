using Aggregatarr.Models;

namespace Aggregatarr.Web.Services;

public interface IDashboardRefresh
{
    void Kick();
}

public sealed class DashboardStore
{
    private readonly object _gate = new();
    private DashboardSnapshot _current = DashboardSnapshot.Empty;
    private DateTimeOffset _calendarStart;
    private DateTimeOffset _calendarEnd;

    public DashboardStore(IClock clock)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        _calendarStart = new DateTimeOffset(today.AddMonths(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        _calendarEnd = new DateTimeOffset(today.AddMonths(2).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
    }

    public event Action? Changed;

    public DashboardSnapshot Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public (DateTimeOffset Start, DateTimeOffset End) CalendarWindow
    {
        get
        {
            lock (_gate)
                return (_calendarStart, _calendarEnd);
        }
    }

    public void Publish(DashboardSnapshot snapshot)
    {
        lock (_gate)
            _current = snapshot;
        Changed?.Invoke();
    }

    public bool EnsureCalendarCovers(DateOnly day)
    {
        lock (_gate)
        {
            var start = DateOnly.FromDateTime(_calendarStart.UtcDateTime);
            var end = DateOnly.FromDateTime(_calendarEnd.UtcDateTime);
            if (day >= start && day <= end)
                return false;
            _calendarStart = new DateTimeOffset(day.AddMonths(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            _calendarEnd = new DateTimeOffset(day.AddMonths(2).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            return true;
        }
    }
}
