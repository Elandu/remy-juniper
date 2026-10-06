using LostAndFound;

namespace LostAndFound.Tests;

/// <summary>Deterministic random source: serves queued values, then a fixed fallback.</summary>
public sealed class SequenceRandom : IRandomSource
{
    private readonly Queue<double> _queue;

    public SequenceRandom(params double[] values) => _queue = new Queue<double>(values);

    public double NextDouble() => _queue.Count > 0 ? _queue.Dequeue() : 0.5;

    public int Next(int maxExclusive)
        => maxExclusive <= 0 ? 0 : (int)(NextDouble() * maxExclusive);
}

/// <summary>Spins up a fully wired service graph backed by a throwaway SQLite file.</summary>
public sealed class Harness : IDisposable
{
    public string Dir { get; }
    public LostFoundConfig Cfg { get; }
    public Catalog Cat { get; }
    public Db Db { get; }
    public EconomyBridge Economy { get; }
    public LostFoundService Core { get; }
    public CollectionService Collection { get; }
    public InventoryService Inventory { get; }
    public ExplorationService Explore { get; }
    public DropService Drops { get; }
    public EncounterService Encounters { get; }
    public QuestService Quests { get; }
    public CraftingService Crafting { get; }
    public TradeService Trade { get; }
    public ParcelService Parcels { get; }

    public Harness(Action<LostFoundConfig> configure = null, IRandomSource rng = null)
    {
        Dir = Path.Combine(Path.GetTempPath(), "lf_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);

        Cfg = new LostFoundConfig();
        configure?.Invoke(Cfg);
        Cfg.Normalize();

        Cat = new Catalog(Cfg);
        Db = new Db(Path.Combine(Dir, "lostfound.db"));
        Db.Init();
        Economy = new EconomyBridge(Path.Combine(Dir, "NadekoBot.db")); // absent -> unavailable

        Core = new LostFoundService(Db, Cat, Economy);
        Collection = new CollectionService(Core);
        Inventory = new InventoryService(Core);
        Explore = new ExplorationService(Core, Collection, Inventory, rng);
        Drops = new DropService(Core, Collection, Explore, Inventory, rng);
        Encounters = new EncounterService(Core, Collection, Explore, Inventory, rng);
        Quests = new QuestService(Core);
        Crafting = new CraftingService(Core, Collection);
        Trade = new TradeService(Core, Inventory);
        Parcels = new ParcelService(Core, Inventory, Collection, Explore, rng);
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); }
        catch { /* best effort */ }
    }
}
