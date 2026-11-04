using BenchmarkDotNet.Attributes;
using CoreECS;
using CoreECS.Defines;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class SystemDagBenchmarks
{
    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
        _world.RegisterGroup("Core");
        _world.RegisterGroup("Late", "Core");
        _world.RegisterSystem<FirstSystem>("Core");
        _world.RegisterSystem<SecondSystem>("Core").After<FirstSystem>();
        _world.RegisterSystem<LateSystem>("Late").After("Core");
        _world.BeginTick();
        _world.EndTick();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public void CleanBeginTick()
    {
        _world.BeginTick();
        _world.EndTick();
    }

    [Benchmark]
    public void DirtyBeginTick()
    {
        _world.UnregisterSystem<SecondSystem>();
        _world.RegisterSystem<SecondSystem>("Core").After<FirstSystem>();
        _world.BeginTick();
        _world.EndTick();
    }

    private sealed class FirstSystem : ISystem
    {
        public void OnCreate() { }
        public void OnTick(ulong tickMask) { }
        public void OnDestroy() { }
    }

    private sealed class SecondSystem : ISystem
    {
        public void OnCreate() { }
        public void OnTick(ulong tickMask) { }
        public void OnDestroy() { }
    }

    private sealed class LateSystem : ISystem
    {
        public void OnCreate() { }
        public void OnTick(ulong tickMask) { }
        public void OnDestroy() { }
    }
}
