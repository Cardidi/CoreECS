using BenchmarkDotNet.Attributes;
using CoreECS;
using CoreECS.Defines;

namespace CoreECS.Benchmark;

[MemoryDiagnoser]
public class ComponentStorageBenchmarks
{
    [Params(1000)]
    public int EntityCount;

    private World _world = null!;
    private Entity[] _entities = null!;
    private ComponentRef<Position> _cached;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        _world.Startup();
        _entities = new Entity[EntityCount];
        for (int i = 0; i < EntityCount; i++)
        {
            Entity entity = _world.CreateEntity();
            entity.CreateComponent(new Position { X = i, Y = -i });
            _entities[i] = entity;
        }

        _cached = _entities[EntityCount / 2].GetComponent<Position>();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Shutdown();

    [Benchmark]
    public ComponentRef<Position> GetComponent() =>
        _entities[EntityCount / 2].GetComponent<Position>();

    [Benchmark]
    public float ReadRef()
    {
        ref readonly Position position = ref _entities[EntityCount / 2].GetComponent<Position>().RO;
        return position.X + position.Y;
    }

    [Benchmark]
    public float ReadCachedRef()
    {
        ref readonly Position position = ref _cached.RO;
        return position.X + position.Y;
    }

    [Benchmark]
    public float WriteRef()
    {
        ref Position position = ref _entities[EntityCount / 2].GetComponent<Position>().RW;
        position.X += 1;
        return position.X;
    }

    [Benchmark]
    public void AddAndRemove()
    {
        Entity entity = _world.CreateEntity();
        entity.CreateComponent(new Velocity());
        entity.DestroyComponent<Velocity>();
        _world.BeginTick();
        _world.Tick();
        _world.EndTick();
    }
}
