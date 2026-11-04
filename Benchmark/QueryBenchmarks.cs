using BenchmarkDotNet.Attributes;
using CoreECS;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class QueryBenchmarks
{
    [Params(10000)]
    public int EntityCount;

    private World _world = null!;
    private EntityQuery _allCommon = null!;
    private EntityQuery _allRare = null!;
    private EntityQuery _anyUnion = null!;
    private EntityQuery _noneOnly = null!;
    private EntityQuery _empty = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
        for (int i = 0; i < EntityCount; i++)
        {
            Entity entity = _world.CreateEntity();
            entity.CreateComponent(new Position { X = i });
            if ((i & 1) == 0) entity.CreateComponent(new Velocity());
            if (i < 10) entity.CreateComponent(new Rare { Value = i });
            if (i % 100 == 0) entity.CreateComponent(new Disabled());
        }

        _allCommon = _world.CreateQuery(EntityMatcher.With.OfAll<Position>());
        _allRare = _world.CreateQuery(
            EntityMatcher.With.OfAll<Position>().OfAll<Rare>());
        _anyUnion = _world.CreateQuery(
            EntityMatcher.With.OfAny<Velocity>().OfAny<Rare>());
        _noneOnly = _world.CreateQuery(EntityMatcher.With.OfNone<Disabled>());
        _empty = _world.CreateQuery(EntityMatcher.With);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public void RefreshAllCommon() => _allCommon.Refresh();

    [Benchmark]
    public void RefreshAllRare() => _allRare.Refresh();

    [Benchmark]
    public void RefreshAnyUnion() => _anyUnion.Refresh();

    [Benchmark]
    public void RefreshNoneOnly() => _noneOnly.Refresh();

    [Benchmark]
    public void RefreshEmptyMatcher() => _empty.Refresh();

    [Benchmark]
    public EntityQuery CreateQuery() =>
        _world.CreateQuery(EntityMatcher.With.OfAll<Position>().OfAll<Rare>());
}
