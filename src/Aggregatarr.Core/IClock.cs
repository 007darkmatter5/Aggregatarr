namespace Aggregatarr;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class UtcClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
