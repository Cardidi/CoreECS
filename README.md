# CoreECS

**A state-first entity–component–system toolkit for C# games.**

English · [简体中文](README.zh-CN.md)

[Quick Start](docs/QUICK_START.md) · [Migration guide](docs/MIGRATING_V1_TO_V2.md) · [License](LICENSE) · [NuGet](https://www.nuget.org/packages/CoreECS)

CoreECS stores components in typed, paged sparse sets, exposes stable component handles, and publishes entity changes through collectors at explicit flush points. It is a library, not an engine: you own the game loop and the integration with your own stack.

## Features

- **Typed paged sparse-set storage** — one storage per component type, O(1) membership, contiguous iteration, no archetype migration.
- **Stable `ComponentRef<T>` handles** — survive page growth and end-of-tick compaction; stale handles are detected.
- **State-first collectors** — `Collected`, `Matching`, `Clashing`, and `Changed` are published only by `Flush()`.
- **Materialized queries** — `EntityQuery` captures a stable `EntityId` snapshot and refreshes on demand.
- **Deterministic system DAG** — groups, nested groups, `Before` / `After`, forward references, stable topological sorting.
- **Explicit tick boundaries** — removals are logical immediately and compacted at `EndTick`; entity IDs are released afterwards.
- **Restartable worlds** — managers and dependency injection are reused across successful start/stop cycles.
- **Tag components** — `ITagComponent<T>` stores membership only, with no per-entity payload.
- **Dual EntityId modes** — default `uint` (20-bit index + 12-bit generation); `COREECS_ULONG_ENTITY` selects `ulong` (32 + 32).
- **Targets** — `net8.0` and `netstandard2.1`, C# 9.

## Install

```bash
dotnet add package CoreECS
```

The published package uses the default `uint` EntityId. Build from source with `COREECS_ULONG_ENTITY` if you need the 64-bit layout.

## First world

```csharp
using CoreECS;
using CoreECS.Defines;

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

var world = new World();
world.Startup();

var entity = world.CreateEntity();
entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });

using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>().OfAll<Velocity>());
collector.Flush();

world.BeginTick();
foreach (var entityId in collector.Collected)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}
world.Tick();
world.EndTick();

world.Shutdown();
```

## Components

Every data component implements `IComponent<T>` and must provide both lifecycle hooks. Hooks receive only an `EntityId`; they initialize or clean up the component itself and must not change entity structure.

```csharp
public struct Health : IComponent<Health>
{
    public int Value;

    public void OnCreate(EntityId entityId) => Value = 100;
    public void OnDestroy(EntityId entityId) { }
}
```

- One component instance per entity and type. Creating a duplicate throws.
- Destroying a missing component throws; destroying an already destroyed entity is a no-op.
- Empty marker components can implement `ITagComponent<T>` and omit payload storage:

```csharp
public struct Stunned : ITagComponent<Stunned>
{
}
```

Tag hooks are never called by the framework.

## Queries

`EntityQuery` is a stable materialized snapshot. It does not subscribe to the world and is not disposable.

```csharp
EntityQuery query = world.CreateQuery(
    EntityMatcher.With.OfAll<Position>().OfNone<Stunned>());

foreach (var entityId in query.Entities)
{
    // Same snapshot until query.Refresh().
}
```

`CreateQuery(matcher, refreshImmediately: false)` starts empty until `Refresh()`.

## Collectors

Collectors track membership and relevant component changes across a flush phase.

```csharp
using var changed = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default);

// ... gameplay writes ...

changed.Flush();
foreach (var entityId in changed.Changed) { /* reprocess */ }
```

- `Collected` — stable membership after the last flush.
- `Matching` / `Clashing` — net entries and exits since the last flush.
- `Changed` — entities to reprocess according to the flags.
- `Flush()` — pumps the shared change journal once and publishes this collector's phase.
- `Dispose()` is idempotent. `World.Shutdown()` invalidates live collectors.

## Systems and groups

`ISystem` requires `OnCreate`, `OnTick`, and `OnDestroy`. Ordering is declarative and resolved into a stable DAG.

```csharp
world.RegisterGroup("Physics");
world.RegisterGroup("Render");

world.RegisterSystem<InputSystem>("Physics");
world.RegisterSystem<MovementSystem>("Physics").After<InputSystem>();
world.RegisterSystem<RenderSystem>("Render").After("Physics");
```

- Group names are globally unique; each concrete system type is registered once.
- Forward anchors are allowed and resolved at `BeginTick`.
- Tick-time graph changes affect the next `BeginTick`; the current tick keeps its captured execution order.
- Cycles are logged and the whole graph falls back to registration order.

## Tick and lifecycle

```text
Startup → BeginTick → Tick → EndTick → Shutdown
```

- Removing a component or entity is logically immediate.
- Physical storage compaction and entity-ID release happen at `EndTick`.
- Raw `RO` / `RW` references are only valid for the current tick phase; reacquire them after `EndTick`.
- `ComponentRef<T>` remains valid across compaction while the component is alive.
- A successful `Shutdown()` may be followed by another `Startup()` on the same `World`; `OnRegister` runs once, while `OnSetup` and `OnCleanup` run each cycle.
- After `Shutdown()`, discard all old entities, handles, queries, collectors, and registration handles.

## Build

```bash
dotnet build CoreECS.sln --configuration Release
dotnet test CoreECS.sln --configuration Release
dotnet run -c Release --project Benchmark/Benchmark.csproj
```

64-bit EntityId:

```bash
dotnet test CoreECS.sln -p:CoreECSUseUlongEntity=true
```

## Repository layout

```text
Kernel/       CoreECS library
Test/         NUnit tests
Benchmark/    BenchmarkDotNet suites
docs/         Guides and migration notes
```

## License

MIT.
