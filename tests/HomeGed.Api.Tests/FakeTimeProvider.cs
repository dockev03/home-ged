namespace HomeGed.Api.Tests;

/// <summary>Horloge pilotée par les tests.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
