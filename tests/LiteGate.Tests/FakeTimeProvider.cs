namespace LiteGate.Tests;

/// <summary>Deterministic clock for unit tests; advances only when told to.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;
}
