using BenchmarkDotNet.Attributes;
using CoreECS;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class EntityLifecycleBenchmarks
{
    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public void CreateDestroyAndTick()
    {
        Entity entity = _world.CreateEntity();
        _world.DestroyEntity(entity);
        _world.BeginTick();
        _world.Tick();
        _world.EndTick();
    }
}
