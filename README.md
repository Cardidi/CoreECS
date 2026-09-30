# CoreECS

**A state-first Entity Component System toolkit for C# games.**

English · [简体中文](README.zh-CN.md)

[Quick Start](docs/QUICK_START.md) · [NuGet](https://www.nuget.org/packages/CoreECS) · [License](LICENSE)

CoreECS keeps game state in archetype-shaped storage and makes change tracking explicit. Want a plain view of the world? Take a query snapshot. Want to react to what changed? Use a collector. Want to defer structural edits? Record them in a command buffer and apply them in one batch.

It is a library, not an engine: the game loop, the lifecycle, and the integration with your stack stay in your hands.

## Highlights

- **Three component layouts:** dense archetype columns, sparse per-entity data, and presence-only tags.
- **Two ways to select entities:** refreshable `IEntityQuery` snapshots and event-driven `IEntityCollector` buffers.
- **Direct data access:** typed `ComponentRef<T>` handles, plus bulk `ReadOnlySpan<T>` / `Span<T>` views over dense columns.
- **Explicit structural batching:** `CommandBuffer` records create, destroy, component, and mask operations for ordered playback.
- **Deterministic system scheduling:** groups, nested groups, `Before` / `After` constraints, and tick masks.
- **Built-in constructor injection:** powered by `Microsoft.Extensions.DependencyInjection`, with an extension point for another container.
- **Ref-safety analyzers:** `ECS0001` and `ECS0002` detect raw refs or spans used across structural changes.
- **Broad library targets:** `net8.0` and `netstandard2.1`.

## Install

```bash
dotnet add package CoreECS
```

The repository itself requires the .NET 8 SDK.

## A first world

```csharp
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

var world = new World();
world.Startup();

var entity = world.CreateEntity();
entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });

var matcher = EntityMatcher.With.OfAll<Position, Velocity>();
using var query = world.CreateQuery(matcher);
query.Refresh();

foreach (var entityId in query.Entities)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}

world.Shutdown();
```

The lifecycle order is:

```text
Startup → create/register/use → BeginTick → Tick → EndTick → Shutdown
```

`World` is single-threaded: everything a world hands you belongs to the thread that created it.

## How it works

### Components

Every component is a struct implementing `IComponent<T>`.

- `IComponent<T>`: dense data stored in an archetype column. Adding or removing it migrates the entity.
- `ISparseComponent<T>`: data stored outside dense columns. It does not determine archetype membership.
- `ITagComponent<T>`: presence only; it carries no value and returns no usable `ComponentRef<T>`.

The entity mask also participates in archetype identity. Calling `Entity.SetMask` may therefore migrate an entity while preserving its components.

### Queries and collectors

Use an `IEntityQuery` to inspect a stable snapshot:

```csharp
using var query = world.CreateQuery(EntityMatcher.With.OfAll<Position>());
query.Refresh();

foreach (var entityId in query.Entities)
{
    // Snapshot contents stay unchanged until Refresh() is called again.
}
```

Use an `IEntityCollector` to process membership and data changes by phase:

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>());

collector.Flush();

foreach (var entityId in collector.Changed)
{
    // Process changes published by this Flush().
}
```

### Ref safety

`ComponentRef<T>` is a relocatable handle, so it stays valid across structural changes. Raw refs from `.RO` / `.RW` and spans from a `Structure` do not: they point straight into internal storage that can move. Finish using them before creating or destroying entities or components, changing masks, or calling `CommandBuffer.Playback()` — then reacquire.

See [`Analyzers/README.md`](Analyzers/README.md) for analyzer installation and the complete `ECS0001` / `ECS0002` rules.

## Next steps

The [Quick Start](docs/QUICK_START.md) covers:

1. world lifecycle and dependency injection;
2. dense, sparse, and tag components;
3. entity and component operations;
4. matchers, queries, and batch access;
5. collectors and change flags;
6. systems, groups, and tick masks;
7. command buffers and ref-safety rules.

Chinese documentation: [README.zh-CN.md](README.zh-CN.md) and [docs/QUICK_START.zh-CN.md](docs/QUICK_START.zh-CN.md).

## Build from source

```bash
git clone https://github.com/Cardidi/CoreECS.git
cd CoreECS
dotnet restore CoreECS.sln
dotnet build CoreECS.sln --configuration Release
dotnet test CoreECS.sln --configuration Release --no-build
```

Repository layout:

```text
Kernel/       CoreECS library
Analyzers/    Roslyn ref-safety analyzers
Test/         NUnit library and analyzer tests
docs/         Guides, designs, and implementation plans
```

## License

[MIT](LICENSE)
