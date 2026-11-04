# CoreECS Quick Start

Required usings:

```csharp
using CoreECS;
using CoreECS.Defines;
using Microsoft.Extensions.DependencyInjection;
```

This guide covers the complete v2 workflow: world lifecycle, data and tag components, entity operations, queries, collectors, systems, and restart.

## 1. World lifecycle

```csharp
var world = new World();
world.Startup();

world.BeginTick();
world.Tick();
world.EndTick();

world.Shutdown();
```

A successful world can be started again after `Shutdown()`. Managers and DI are reused; `OnRegister` runs once, `OnSetup` and `OnCleanup` run every cycle. Treat every object from an earlier cycle as invalid.

The World is single-threaded. `World`, `EntityQuery.Refresh`, `EntityCollector.Flush`, DAG changes, and component storage access are not thread-safe and use no World-level locks. Component type registration uses a one-time process-wide lock.

Subclass `World` to register services, managers, and systems:

```csharp
public sealed class GameWorld : World
{
    protected override void OnRegister(
        IManagerRegister managers,
        IServiceCollection services)
    {
        services.AddSingleton<GameConfig>();
    }

    protected override void OnSetup()
    {
        RegisterGroup("Gameplay");
        RegisterSystem<MovementSystem>("Gameplay");
    }

    protected override void OnCleanup() { }
}
```

## 2. Data components

```csharp
public struct Position : IComponent<Position>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}
```

Both hooks are mandatory. Hooks may mutate their own component, but structural operations on entities and components are rejected while a hook runs.

Create, read, and destroy:

```csharp
var entity = world.CreateEntity();
var position = entity.CreateComponent(new Position { X = 1, Y = 2 });

ref readonly Position read = ref position.RO;
ref Position write = ref position.RW; // increments Revision
write.X += 10;

entity.DestroyComponent<Position>();
```

- Duplicate creation throws.
- Destroying a missing component throws.
- Prefer caching a `ref` when writing several fields: each `.RW` access bumps `Revision` and may record a change reason.

## 3. Tag components

```csharp
public struct Stunned : ITagComponent<Stunned>
{
}
```

Tags have no payload storage and their hooks are never invoked. `CreateComponent<Stunned>()`, `HasComponent<Stunned>()`, and `DestroyComponent<Stunned>()` work like normal components. `GetComponent<Stunned>()` returns a valid handle; its `RO`/`RW` exposes a shared default value.

## 4. Entity operations

```csharp
Entity created = world.CreateEntity(mask: 0b0001);
Entity fetched = world.GetEntity(created.EntityId);

bool alive = created.IsValid;
ulong mask = created.Mask; // immutable after creation

bool has = created.HasComponent<Position>();
bool found = created.TryGetComponent<Position>(out var positionRef);
bool existed = created.GetOrCreateComponent<Position>(
    out var ensured, new Position { X = 0, Y = 0 });

var buffer = new List<ComponentRef>();
int count = created.GetComponents(buffer); // non-allocating untyped enumeration

world.DestroyEntity(created);
world.DestroyEntity(created); // no-op
```

`Entity` compares world identity plus `EntityId`. Two different worlds may produce equal `EntityId` values.

## 5. Matchers and queries

```csharp
EntityMatcher matcher = EntityMatcher.With
    .OfAll<Position>()
    .OfAny<Velocity>()
    .OfNone<Stunned>();

EntityQuery query = world.CreateQuery(matcher);
int count = query.Count;

foreach (EntityId entityId in query.Entities)
{
    // stable snapshot
}

query.Refresh();
```

Matchers freeze on first bind. Filters are:

- `OfAll` — must have every listed component.
- `OfAny` — must have at least one.
- `OfNone` — must have none.
- `WithMask` — intersects the entity mask (`(matcherMask & entityMask) != 0`).

`CreateQuery(matcher, refreshImmediately: false)` starts empty until `Refresh()`.

## 6. Collectors

```csharp
using EntityCollector collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default);

collector.Flush();

foreach (EntityId entityId in collector.Collected) { }
foreach (EntityId entityId in collector.Matching) { }
foreach (EntityId entityId in collector.Clashing) { }
foreach (EntityId entityId in collector.Changed) { }
```

Flags:

| Flag | Meaning |
|------|---------|
| `RevisionAsChange` | Component writes enter `Changed`. |
| `MatchAsChange` | New members enter `Changed`. |
| `ClashAsChange` | Departures enter `Changed`. |
| `RelatedComponentOnly` | Only matcher-relevant component writes enter `Changed`. |
| `Default` | `RevisionAsChange \| MatchAsChange \| RelatedComponentOnly`. |

`Flush()` publishes net membership relative to the previous flush. A structural change that leaves and re-enters within one phase produces no `Matching`/`Clashing`, but may still produce one `Changed`.

## 7. Systems and groups

```csharp
public sealed class MovementSystem : ISystem
{
    private readonly IWorld m_world;

    public MovementSystem(IWorld world) => m_world = world;

    public void OnCreate() { }
    public void OnTick(ulong tickMask) { }
    public void OnDestroy() { }
}
```

```csharp
world.RegisterGroup("Gameplay");
world.RegisterGroup("Render");
world.RegisterSystem<MovementSystem>("Gameplay")
    .Before("Render");
```

`TickGroup` defaults to `ulong.MaxValue`; override it to filter systems with `world.Tick(mask)`.

## 8. Tick boundary

```csharp
world.BeginTick();
// structural changes: logically immediate
world.Tick();
world.EndTick(); // compact storages, release entity IDs
```

- Removed components disappear from new query/collector matching immediately. Existing `EntityQuery` snapshots keep their old `EntityId` values until the next `Refresh()`.
- Packed slots do not move until `EndTick`.
- `ComponentRef<T>` survives compaction while the component is alive.
- Raw refs do not; reacquire them next tick.

## 9. 64-bit EntityId

Build and test the source tree with:

```bash
dotnet test CoreECS.sln -p:CoreECSUseUlongEntity=true
```

Default layout: uint, 20-bit index + 12-bit generation. Ulong layout: 32 + 32.
