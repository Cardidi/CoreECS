using BenchmarkDotNet.Attributes;
using CoreECS;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class WorldLifecycleBenchmarks
{
    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
        _world.Shutdown();
    }

    [Benchmark]
    public void StartupAndShutdown()
    {
        _world.Startup();
        _world.Shutdown();
    }
}
