using BenchmarkDotNet.Attributes;
using CoreECS;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class QueryBenchmarks
{
    [Params(10000)]
    public int EntityCount;

    private World _world = null!;

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
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public int AllCommon() =>
        _world.CreateQuery(EntityMatcher.With.OfAll<Position>()).Count;

    [Benchmark]
    public int AllRare() =>
        _world.CreateQuery(EntityMatcher.With.OfAll<Position>().OfAll<Rare>()).Count;

    [Benchmark]
    public int AnyUnion() =>
        _world.CreateQuery(EntityMatcher.With.OfAny<Velocity>().OfAny<Rare>()).Count;

    [Benchmark]
    public int NoneOnly() =>
        _world.CreateQuery(EntityMatcher.With.OfNone<Disabled>()).Count;

    [Benchmark]
    public int EmptyMatcher() =>
        _world.CreateQuery(EntityMatcher.With).Count;
}
