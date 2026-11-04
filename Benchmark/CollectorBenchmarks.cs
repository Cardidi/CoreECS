using BenchmarkDotNet.Attributes;
using CoreECS;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class CollectorBenchmarks
{
    [Params(0, 1, 100)]
    public int CollectorCount;

    [Params(100)]
    public int UnrelatedCollectorCount;

    private World _world = null!;
    private Entity[] _entities = null!;
    private EntityCollector[] _collectors = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
        _entities = new Entity[256];
        for (int i = 0; i < _entities.Length; i++)
        {
            Entity entity = _world.CreateEntity();
            entity.CreateComponent(new Position { X = i });
            _entities[i] = entity;
        }

        _collectors = new EntityCollector[CollectorCount];
        for (int i = 0; i < CollectorCount; i++)
        {
            _collectors[i] = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>());
            _collectors[i].Flush();
        }

        for (int i = 0; i < UnrelatedCollectorCount; i++)
        {
            EntityCollector unrelated = _world.CreateCollector(
                EntityMatcher.With.OfAll<Health>());
            unrelated.Flush();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public float RepeatedWrite()
    {
        ref Position position = ref _entities[0].GetComponent<Position>().RW;
        position.X += 1;
        return position.X;
    }

    [Benchmark]
    public void FirstFlushAfterWrite()
    {
        RepeatedWrite();
        if (_collectors.Length != 0)
        {
            _collectors[0].Flush();
        }
    }

    [Benchmark]
    public void FlushAllCollectors()
    {
        RepeatedWrite();
        for (int i = 0; i < _collectors.Length; i++)
        {
            _collectors[i].Flush();
        }
    }
}
