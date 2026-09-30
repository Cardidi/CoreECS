# CoreECS Quick Start

English · [简体中文](QUICK_START.zh-CN.md) · [Project overview](../README.md)

This guide builds a small moving-entity example, then introduces the APIs that matter in a real game loop.

## 1. Install and import

```bash
dotnet add package CoreECS
```

Most programs need these namespaces:

```csharp
using CoreECS;
using CoreECS.Defines;
```

`Structure`, manager implementations, and scheduling handles are defined in `CoreECS.Structures` and `CoreECS.Managers`.

## 2. Define components

Components are plain structs; the interface you implement decides how they are stored.

```csharp
public struct Position : IComponent<Position>
{
    public float X;
    public float Y;
}

public struct Velocity : IComponent<Velocity>
{
    public float X;
    public float Y;
}

public struct Name : ISparseComponent<Name>
{
    public string Value;
}

public struct Player : ITagComponent<Player>
{
}
```

### Storage kinds

| Kind | Contract | Storage and behavior |
| --- | --- | --- |
| Dense | `IComponent<T>` | Row-aligned archetype column. Add/remove changes archetype. |
| Sparse | `ISparseComponent<T>` | Per-entity data outside dense columns. Add/remove does not change archetype. |
| Tag | `ITagComponent<T>` | Presence bit only. No component value or usable ref. |

Dense and sparse components may implement lifecycle hooks:

```csharp
public struct Lifetime : IComponent<Lifetime>
{
    public ulong Owner;

    public void OnCreate(ulong entityId) => Owner = entityId;
    public void OnDestroy(ulong entityId) { }
}
```

If `OnCreate` needs to see the real initial value, pass it to `CreateComponent(value)`. Creating a default component and then writing through `.RW` means `OnCreate` runs on `default(T)` first.

## 3. Start a world

```csharp
var world = new World();
world.Startup();
```

Call `Startup()` before creating entities, collectors, queries, command buffers, or registering systems. Call `Shutdown()` when the world is finished.

For each simulation step:

```csharp
world.BeginTick();
world.Tick();
world.EndTick();
```

`BeginTick()` advances `TickCount` and applies pending schedule changes. `Tick(mask)` runs systems whose `TickGroup` intersects the mask. `EndTick()` completes system cleanup.

A world is not thread-safe: create it, tick it, and shut it down on the same thread.

## 4. Create entities and components

```csharp
var entity = world.CreateEntity();

entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });
entity.CreateComponent(new Name { Value = "Player One" });
entity.CreateComponent<Player>();
```

Read, write, check, and remove components through the entity handle:

```csharp
if (entity.HasComponent<Position>())
{
    var positionHandle = entity.GetComponent<Position>();
    Console.WriteLine(positionHandle.RO.X);
    positionHandle.RW.X += 5;
}

if (entity.TryGetComponent<Name>(out var name))
    Console.WriteLine(name.RO.Value);

entity.DestroyComponent<Velocity>();
world.DestroyEntity(entity);
```

`GetOrCreateComponent` returns `true` when the component already existed:

```csharp
var existed = entity.GetOrCreateComponent(
    out ComponentRef<Name> name,
    new Name { Value = "Unnamed" });
```

For a tag, use `HasComponent<T>()` to inspect presence. Tags carry no data, so their create/get operations return a default component reference.

### Entity handles and masks

`Entity` is a handle that stays valid across archetype migrations. Once the entity is destroyed, the handle reports `IsValid == false`.

Masks are application-defined bit fields used by matchers and system ticks:

```csharp
[Flags]
public enum EntityLayer : ulong
{
    Simulation = 1UL << 0,
    Presentation = 1UL << 1,
}

var simulated = world.CreateEntity((ulong)EntityLayer.Simulation);
simulated.SetMask((ulong)(EntityLayer.Simulation | EntityLayer.Presentation));
```

The mask is part of the archetype key. `SetMask` may migrate the entity, but preserves dense, sparse, and tag components and does not invoke component lifecycle hooks. The default mask is `ulong.MaxValue`.

## 5. Match entities

Build a matcher with `OfAll`, `OfAny`, and `OfNone`:

```csharp
var movingPlayers = EntityMatcher.With
    .OfAll<Position, Velocity>()
    .OfAll<Player>();

var visibleWithoutVelocity = EntityMatcher
    .WithMask((ulong)EntityLayer.Presentation)
    .OfAll<Position>()
    .OfNone<Velocity>();
```

Rules:

- Every `OfAll` condition must be present.
- At least one `OfAny` condition must be present when any are configured.
- No `OfNone` condition may be present.
- `WithMask(mask)` first requires `(entity.Mask & mask) != 0`.
- `EntityMatcher.With` uses `ulong.MaxValue`, effectively disabling mask filtering for normal nonzero masks.
- A matcher without component conditions matches every entity that passes its mask condition.

## 6. Query current state

`IEntityQuery` owns a snapshot. It starts empty and changes only when you call `Refresh()`.

```csharp
using var query = world.CreateQuery(
    EntityMatcher.With.OfAll<Position, Velocity>());

query.Refresh();

foreach (var entityId in query.Entities)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}
```

Use `query.Structures` for dense batch processing:

```csharp
foreach (var structure in query.Structures)
{
    var positions = structure.GetReadWriteDenseColumn<Position>();
    var velocities = structure.GetReadOnlyDenseColumn<Velocity>();

    for (var row = 0; row < structure.Count; row++)
    {
        positions[row].X += velocities[row].X;
        positions[row].Y += velocities[row].Y;
    }
}
```

`GetReadWriteDenseColumn<T>()` marks every row in the structure as changed. Acquire it once per structure rather than once per row. Structure spans are available only for dense components.

## 7. Track changes with collectors

A collector watches the world continuously, but only surfaces its findings when you call `Flush()`.

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>());

// Make changes to the world...
collector.Flush();

foreach (var id in collector.Matching)
    Console.WriteLine($"Entered: {id}");

foreach (var id in collector.Clashing)
    Console.WriteLine($"Left: {id}");

foreach (var id in collector.Changed)
    Console.WriteLine($"Reprocess: {id}");
```

The buffers represent:

| Buffer | Contents after the latest `Flush()` |
| --- | --- |
| `Collected` | All entities currently matching |
| `Matching` | Entities that entered during the phase |
| `Clashing` | Entities that left during the phase |
| `Changed` | Entities selected by the collector flags for reprocessing |

The default flags are:

```csharp
EntityCollectorFlag.RevisionAsChange |
EntityCollectorFlag.MatchAsChange |
EntityCollectorFlag.RelatedComponentOnly
```

Departures are always visible in `Clashing`; add `ClashAsChange` to mirror them into `Changed`:

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
```

Use `EntityCollectorFlag.None` when only membership buffers are needed. Dispose collectors when their owner is destroyed.

## 8. Define and schedule systems

Systems implement `ISystem`. Constructor arguments are resolved from the world's injection proxy.

```csharp
public sealed class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityQuery m_query;

    public MovementSystem(World world) => m_world = world;

    public ulong TickGroup => 1UL << 0;

    public void OnCreate()
    {
        m_query = m_world.CreateQuery(
            EntityMatcher.With.OfAll<Position, Velocity>());
    }

    public void OnTick(ulong tickMask)
    {
        m_query.Refresh();
        foreach (var structure in m_query.Structures)
        {
            var positions = structure.GetReadWriteDenseColumn<Position>();
            var velocities = structure.GetReadOnlyDenseColumn<Velocity>();
            for (var row = 0; row < structure.Count; row++)
            {
                positions[row].X += velocities[row].X;
                positions[row].Y += velocities[row].Y;
            }
        }
    }

    public void OnDestroy() => m_query.Dispose();
}
```

Register systems after startup:

```csharp
world.RegisterGroup("Simulation");
world.RegisterSystem<MovementSystem>("Simulation");

world.RegisterGroup("Presentation").After("Simulation");
world.RegisterSystem<RenderSystem>("Presentation");
```

Registration handles can declare `Before<T>()`, `After<T>()`, `Before("Group")`, or `After("Group")`. Groups may be nested by passing a parent name to `RegisterGroup`. Forward references are allowed; unresolved anchors are logged and ignored, while cycles fall back to registration order.

Schedule changes made during a tick take effect at the next `BeginTick()`. Entity and component operations are immediate unless explicitly recorded in a command buffer.

Run a subset of systems with a tick mask:

```csharp
world.BeginTick();
world.Tick(1UL << 0);
world.EndTick();
```

## 9. Record structural work

To queue structural work and apply it later in one batch, use a `CommandBuffer`:

```csharp
using var commands = world.CreateCommandBuffer();

var spawned = commands.CreateEntity((ulong)EntityLayer.Simulation);
commands.CreateComponent(spawned, new Position { X = 0, Y = 0 });
commands.CreateComponent(spawned, new Velocity { X = 2, Y = 0 });
commands.CreateComponent<Player>(spawned);

commands.Playback();
```

- Recording does not modify the world.
- `Playback()` applies commands immediately in recording order and clears the buffer for reuse.
- A placeholder from `CreateEntity()` is private to that buffer and valid only for its pending batch.
- Disposing without playback discards pending commands.
- If playback throws, recorded commands are still cleared and the buffer remains reusable.

## 10. Avoid stale refs and spans

Structural changes can move entities or reallocate dense columns. Do not keep a raw ref or span alive across such an operation.

Unsafe:

```csharp
ref var position = ref entity.GetComponent<Position>().RW;
entity.CreateComponent<Velocity>();
position.X = 10; // The raw ref may now point at stale storage.
```

Safe:

```csharp
var positionHandle = entity.GetComponent<Position>();
entity.CreateComponent<Velocity>();
positionHandle.RW.X = 10; // The handle resolves the current location.
```

Treat these as structural boundaries:

- entity creation or destruction;
- dense component addition or removal;
- entity mask changes;
- `CommandBuffer.Playback()`;
- calls that transitively perform those operations.

The analyzers in `Analyzers/` report common violations as `ECS0001` and `ECS0002`. See [Analyzers/README.md](../Analyzers/README.md).

## 11. Customize startup and dependency injection

Subclass `World` to register services and managers:

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class GameWorld : World
{
    protected override void OnRegister(
        IManagerRegister register,
        IServiceCollection services)
    {
        services.AddSingleton<GameClock>();
    }

    protected override void OnSetup()
    {
        RegisterSystem<MovementSystem>();
    }

    protected override void OnCleanup()
    {
    }
}
```

`OnRegister` runs only during the first startup, before the injection proxy is built. `OnSetup` runs after every successful startup, and `OnCleanup` runs before every shutdown. A shut-down `World` cannot be restarted; build a new instance when you need another run.

The built-in container registers `IWorld`, the concrete world type, and world managers. Override `GetInjectionProxyFactory()` to integrate another container.

## 12. Complete example

```csharp
using System;
using CoreECS;
using CoreECS.Defines;

public struct Position : IComponent<Position>
{
    public float X;
    public float Y;
}

public struct Velocity : IComponent<Velocity>
{
    public float X;
    public float Y;
}

public sealed class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityQuery m_query;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_query = m_world.CreateQuery(
            EntityMatcher.With.OfAll<Position, Velocity>());
    }

    public void OnTick(ulong tickMask)
    {
        m_query.Refresh();
        foreach (var structure in m_query.Structures)
        {
            var positions = structure.GetReadWriteDenseColumn<Position>();
            var velocities = structure.GetReadOnlyDenseColumn<Velocity>();
            for (var row = 0; row < structure.Count; row++)
            {
                positions[row].X += velocities[row].X;
                positions[row].Y += velocities[row].Y;
            }
        }
    }

    public void OnDestroy() => m_query.Dispose();
}

public static class Program
{
    public static void Main()
    {
        var world = new World();
        world.Startup();
        world.RegisterSystem<MovementSystem>();

        var entity = world.CreateEntity();
        entity.CreateComponent(new Position());
        entity.CreateComponent(new Velocity { X = 1, Y = 0.5f });

        for (var frame = 0; frame < 3; frame++)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();

            ref readonly var position = ref entity.GetComponent<Position>().RO;
            Console.WriteLine($"Frame {frame}: ({position.X}, {position.Y})");
        }

        world.Shutdown();
    }
}
```

[Project overview](../README.md) · [简体中文](QUICK_START.zh-CN.md)
