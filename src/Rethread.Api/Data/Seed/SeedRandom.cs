namespace Rethread.Api.Data.Seed;

/// <summary>
/// mulberry32, as the admin portal's mock world uses it (<c>src/mock/seed/random.ts</c>). Same seed, same
/// calls in the same order, same data: the portal's e2e tests run against this seed unchanged.
/// </summary>
internal sealed class SeedRandom(uint seed)
{
    private uint _state = seed;

    public double Next()
    {
        unchecked
        {
            _state += 0x6D2B79F5;
            var t = _state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public int Int(int min, int max) => min + (int)Math.Floor(Next() * (max - min + 1));

    public T Pick<T>(IReadOnlyList<T> items) => items[(int)Math.Floor(Next() * items.Count)];

    public bool Chance(double probability) => Next() < probability;

    /// <summary>A weight with 2 decimals, rounded like JavaScript's <c>Math.round</c>.</summary>
    public decimal Kg(double min, double max) => JsRound((min + Next() * (max - min)) * 100) / 100m;

    /// <summary>JavaScript's <c>Math.round</c>: halves go up.</summary>
    public static decimal JsRound(double value) => (decimal)Math.Floor(value + 0.5);
}
