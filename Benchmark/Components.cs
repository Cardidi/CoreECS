using CoreECS;
using CoreECS.Defines;

namespace CoreECS.Benchmark;

public struct Position : IComponent<Position>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

public struct Velocity : IComponent<Velocity>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

public struct Rare : IComponent<Rare>
{
    public int Value;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

public struct Health : IComponent<Health>
{
    public int Value;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

public struct Disabled : ITagComponent<Disabled>
{
}
