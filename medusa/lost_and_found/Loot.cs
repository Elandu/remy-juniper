namespace LostAndFound;

/// <summary>Small random abstraction so selection logic is testable.</summary>
public interface IRandomSource
{
    double NextDouble();
    int Next(int maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _r;
    public SystemRandomSource() => _r = new Random();
    public SystemRandomSource(int seed) => _r = new Random(seed);
    public double NextDouble() => _r.NextDouble();
    public int Next(int maxExclusive) => maxExclusive <= 0 ? 0 : _r.Next(maxExclusive);
}

public static class Loot
{
    /// <summary>Rolls a rarity from a weight table. Returns null when every weight is &lt;= 0.</summary>
    public static Rarity? RollRarity(IReadOnlyDictionary<Rarity, double> weights, IRandomSource rng)
    {
        if (weights is null || weights.Count == 0)
            return null;

        var total = 0.0;
        foreach (var kv in weights)
            if (kv.Value > 0)
                total += kv.Value;
        if (total <= 0)
            return null;

        var roll = rng.NextDouble() * total;
        foreach (var kv in weights)
        {
            if (kv.Value <= 0) continue;
            roll -= kv.Value;
            if (roll <= 0)
                return kv.Key;
        }

        // floating point safety net: return the last positive-weight rarity
        Rarity? last = null;
        foreach (var kv in weights)
            if (kv.Value > 0) last = kv.Key;
        return last;
    }

    public static T PickWeighted<T>(IReadOnlyList<(T item, double weight)> entries, IRandomSource rng)
    {
        if (entries is null || entries.Count == 0)
            return default;

        var total = 0.0;
        foreach (var e in entries)
            if (e.weight > 0) total += e.weight;
        if (total <= 0)
            return entries[0].item;

        var roll = rng.NextDouble() * total;
        foreach (var e in entries)
        {
            if (e.weight <= 0) continue;
            roll -= e.weight;
            if (roll <= 0)
                return e.item;
        }
        return entries[entries.Count - 1].item;
    }

    public static T Pick<T>(IReadOnlyList<T> list, IRandomSource rng)
        => list == null || list.Count == 0 ? default : list[rng.Next(list.Count)];
}
