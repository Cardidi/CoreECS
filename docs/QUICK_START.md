# Quick Start Guide

> Step-by-step guide to building with CoreECS — from `World` setup to collectors and a complete runnable example.

**English** · [简体中文](QUICK_START.zh-CN.md)

[← Back to README](../README.md) · [README（中文）](../README.zh-CN.md)

## Table of Contents

1. [Creating a World](#1-creating-a-world)
2. [Defining Components](#2-defining-components)
3. [Creating Entities](#3-creating-entities)
4. [Adding Components](#4-adding-components-to-entities)
5. [Accessing Components](#5-accessing-components)
6. [Removing Components](#6-removing-components)
7. [Defining Systems](#7-defining-systems)
8. [Managing Systems](#8-managing-systems)
9. [Entity Matchers](#9-using-entity-matchers)
10. [Entity Collectors](#10-entity-collector--advanced-filtering-and-change-tracking)
11. [Command Buffer](#11-command-buffer)
12. [v1 → v2 Breaking Changes](#12-v1--v2-breaking-changes)
13. [Complete Example](#13-complete-example)

---

## 1. Creating a World

A `World` is the root container for entities, components, and systems.

```csharp
using CoreECS;

var world = new World();
world.Startup();
```

### Before `Startup()`

Do **not**:

- Create entities
- Add components
- Register systems

You **can** prepare a custom `World` subclass:

- Override `OnRegister(register, services)` to register extra managers and DI services (built on first `Startup()`)
- Override lifecycle hooks (`OnSetup`, `OnCleanup`)

Hook timing: `OnRegister` runs only on the first `Startup()`; `OnSetup` runs on every `Startup()`; `OnCleanup` runs on every `Shutdown()`.

After `Startup()`, use `World.InjectionProxy` to resolve services (`null` until the first `Startup()` completes).

> **Thread safety:** Worlds are not thread-safe. Access them from a single thread (typically the main/game thread).

When finished, call `World.Shutdown()` to release resources.

After `Startup()`, `world.CreateCommandBuffer()` returns a buffer for recording structural changes — see [Command Buffer](#11-command-buffer).

---

## 2. Defining Components

Components are data-only structs implementing `IComponent<T>`:

```csharp
public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

public struct HealthComponent : IComponent<HealthComponent>
{
    public float Value;
}
```

Optional lifecycle hooks:

```csharp
public struct LifecycleComponent : IComponent<LifecycleComponent>
{
    public bool OnCreateCalled;
    public bool OnDestroyCalled;

    public void OnCreate(ulong entityId) => OnCreateCalled = true;
    public void OnDestroy(ulong entityId) => OnDestroyCalled = true;
}
```

### Component kinds

Dense components implement `IComponent<T>` directly; two further kinds are available:

```csharp
public struct PositionComponent : IComponent<PositionComponent>          // Dense
{
    public float X;
    public float Y;
}

public struct ManaComponent : IDiscreteComponent<ManaComponent>          // Discrete
{
    public int Value;
}

public struct PlayerTag : ITagComponent<PlayerTag>                       // Tag
{
}
```

- Kind is determined by the most-derived interface: Tag > Discrete > Dense.
- Only Dense components and the entity mask decide Structure membership; Discrete / Tag components never migrate the entity.
- Dense and discrete components run `OnCreate` / `OnDestroy` when added or removed; tags use the default empty implementations and the kernel does not invoke tag hooks (tag add/remove only flips the tag bitmap).
- `GetComponent<Tag>` returns `default` (`NotNull == false`).

---

## 3. Creating Entities

Entities are identified by `ulong` at the storage layer; prefer the `Entity` struct for API ergonomics.

```csharp
var entity = world.CreateEntity();
var anotherEntity = world.GetEntity(entityId);
```

### Entity masks

Tag entities with a bitmask for matcher filtering:

```csharp
enum EntityType
{
    Actor    = 1 << 1,
    Terrain  = 1 << 2,
}

var actor = world.CreateEntity((ulong)EntityType.Actor);
```

Default `CreateEntity()` uses `ulong.MaxValue` (compatible with any matcher mask).

Change the mask at runtime with `SetMask`:

```csharp
var actor = world.CreateEntity((ulong)EntityType.Actor);
actor.SetMask((ulong)EntityType.Terrain);   // migrates the entity; data is preserved
```

The mask is part of the archetype structure key, so `SetMask` migrates the entity. Dense data, discrete components and tags are all preserved, and no component lifecycle hooks run. Setting the same mask is a no-op.

---

## 4. Adding Components to Entities

```csharp
var velocityRef = entity.CreateComponent<VelocityComponent>();
velocityRef.RW.X = 1;
velocityRef.RW.Y = 1;

entity.CreateComponent<HealthComponent>().RW.Value = 100;

// Recommended: initial value in one step (runs OnCreate with that value)
var positionRef = entity.CreateComponent(new PositionComponent { X = 10, Y = 20 });

// Avoid: OnCreate runs on default(T), then RW overwrites
var positionRef2 = entity.CreateComponent<PositionComponent>();
positionRef2.RW = new PositionComponent { X = 10, Y = 20 };
```

---

## 5. Accessing Components

Use `RO` for read-only access and `RW` for writes (writes mark revision and can feed collectors).

```csharp
var positionRef = entity.GetComponent<PositionComponent>();
Console.WriteLine($"Position: ({positionRef.RO.X}, {positionRef.RO.Y})");

bool hasHealth = entity.HasComponent<HealthComponent>();
var allComponents = entity.GetComponents();
```

### RO / RW notes

| Access | Behavior |
|--------|----------|
| `RO` | Read-only; preferred in hot paths |
| `RW` | Writable; triggers revision tracking (`RevisionAsChange` on collectors) |

### Extension helpers

```csharp
if (entity.TryGetComponent<PositionComponent>(out var pos))
    Console.WriteLine($"({pos.RO.X}, {pos.RO.Y})");

bool existed = entity.GetOrCreateComponent<VelocityComponent>(out var vel);
if (!existed)
    vel.RW = new VelocityComponent { X = 1, Y = 1 };

entity.GetOrCreateComponent(out var health, new HealthComponent { Value = 100 });
```

`GetOrCreateComponent` returns `true` if the component already existed, `false` if it was created.

### Queries

`world.Query(matcher)` returns a non-pooled `IEntityQuery`; the snapshot is empty until `Refresh()`:

```csharp
var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
query.Refresh();   // the snapshot stays stable until the next Refresh()

foreach (var id in query.Entities)
    Console.WriteLine(id);

query.Dispose();
```

### Batch access (SoA)

```csharp
var query = world.Query(EntityMatcher.With.OfAll<PositionComponent>());
query.Refresh();

foreach (var structure in query.Structures)
{
    var positions = structure.RO<PositionComponent>();   // ReadOnlySpan<PositionComponent>
    for (var row = 0; row < positions.Length; row++)
        Console.WriteLine($"{structure.Entities[row]}: {positions[row].X}");
}

// RW marks every row of the structure (revision + change event) and is invalidated
// by structural changes; acquire once per structure.
var velocities = query.Structures[0].RW<VelocityComponent>();
for (var row = 0; row < velocities.Length; row++)
    velocities[row].X += 1;
```

---

## 6. Removing Components

```csharp
entity.DestroyComponent(positionRef);
entity.DestroyComponent<HealthComponent>();
```

---

## 7. Defining Systems

Systems implement `ISystem` and process entities (usually via collectors).

- Register dependencies in `OnRegister`; the world resolves constructor parameters via `IInjectionProxy`.
- Group systems with `TickGroup` and filter execution with `World.Tick(tickMask)` (`(system.TickGroup & tickMask) != 0`).
- Create collectors in `OnCreate`, call `Flush()` before reading buffers, dispose in `OnDestroy`.

```csharp
public class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityCollector m_movingEntities;

    public ulong TickGroup => ulong.MaxValue;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>());
    }

    public void OnTick(ulong tickMask)
    {
        m_movingEntities.Flush();
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            position.RW.X += velocity.RW.X;
            position.RW.Y += velocity.RW.Y;
        }
    }

    public void OnDestroy() => m_movingEntities?.Dispose();
}
```

---

## 8. Managing Systems

Groups are pure ordering buckets — they carry no mask (`TickGroup` stays on the system) and can be nested. `Before` / `After` anchors may target a system type or a group name, may cross levels, and allow forward references (targets registered later). Anchors that cannot be resolved are logged as errors and ignored; a constraint cycle falls back to flattened registration order. Unconstrained nodes keep registration order (stable sort).

```csharp
world.RegisterGroup("Physics");
world.RegisterGroup("Gameplay", GroupInsertMode.Early);

world.RegisterSystem<InputSystem>("Gameplay").Before<MovementSystem>();
world.RegisterSystem<MovementSystem>("Gameplay");

world.RegisterGroup("Render").After("Gameplay");
world.RegisterSystem<RenderSystem>("Render");
world.RegisterSystem<RootLevelSystem>();   // no group → root level
```

System-graph changes made during a tick are applied and re-sorted at the next `BeginTick()`; already registered systems keep their instance (no repeated `OnCreate`). Entity/component ops are **not** deferred.

```csharp
var movementSystem = world.FindSystem<MovementSystem>();

while (running)
{
    world.BeginTick();
    world.Tick();
    world.EndTick();
}
```

---

## 9. Using Entity Matchers

```csharp
var positionOnly = EntityMatcher.With.OfAll<PositionComponent>();

var positionOrVelocity = EntityMatcher.With
    .OfAny<PositionComponent>()
    .OfAny<VelocityComponent>();

var noHealth = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfNone<HealthComponent>();

var complex = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfAll<VelocityComponent>()
    .OfNone<HealthComponent>();

var byMask = EntityMatcher.WithMask((ulong)EntityType.Actor);
```

**Mask rules**

- `EntityMatcher.With` → `EntityMask == ulong.MaxValue` (no mask filter).
- `WithMask(m)` → entity must satisfy `(entity.Mask & m) != 0` before component rules run.
- No `OfAll` / `OfAny` / `OfNone` → matches any entity that passes the mask check.

---

## 10. Entity Collector — Advanced Filtering and Change Tracking

Collectors track matcher-qualified entities and summarize changes per `Flush()` phase.

### Basic usage

```csharp
var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>());

collector.Flush();
for (var i = 0; i < collector.Collected.Count; i++)
{
    var entity = world.GetEntity(collector.Collected[i]);
    // ...
}
```

Prefer `Flush()` over obsolete `IEntityCollector.Change()`.

### Buffers (after each `Flush()`)

| Buffer | Meaning |
|--------|---------|
| `Collected` | Entities currently in the collector |
| `Matching` | Entered this phase |
| `Clashing` | Left this phase |
| `Changed` | Subset to reprocess (controlled by flags) |

Call `Flush()` once per frame/phase before reading any buffer.

### Flags

`EntityCollectorFlag.Default` mirrors into `Changed`:

- Structural **match** (`MatchAsChange`)
- Match-relevant **add/remove** (`RelatedComponentOnly`)
- Match-relevant **data** revisions (`RevisionAsChange` + `RelatedComponentOnly`)

Not in `Default`: departures (use `Clashing`, or add `ClashAsChange` to mirror into `Changed`).

```csharp
var @default = world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());

var withClash = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);

var membershipOnly = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.None);
```

### Change tracking example

```csharp
var entity = world.CreateEntity();
entity.CreateComponent<PositionComponent>();
collector.Flush();

foreach (var id in collector.Matching)
    Console.WriteLine($"Joined: {id}");
foreach (var id in collector.Clashing)
    Console.WriteLine($"Left: {id}");
```

| Flag | Effect on `Changed` |
|------|---------------------|
| `RevisionAsChange` | Data revisions (in `Default`) |
| `MatchAsChange` | New members (in `Default`) |
| `ClashAsChange` | Departures (not in `Default`) |
| `RelatedComponentOnly` | Matcher-relevant component events (in `Default`) |
| `None` | Empty `Changed`; use `Matching` / `Clashing` / `Collected` |

### Best practices

1. Always `Flush()` before reading buffers.
2. Use indexed `for` loops on `Collected` (not `foreach`) if you might mutate membership while iterating.
3. `Dispose()` collectors in `OnDestroy`.
4. Pick flags for your workflow; add `ClashAsChange` when leave events must appear in `Changed`.

---

## 11. Command Buffer

A `CommandBuffer` records entity/component commands so a batch of structural changes can be applied in one explicit `Playback()`:

```csharp
using var cmd = world.CreateCommandBuffer();

var e = cmd.CreateEntity(0b01);                       // placeholder; resolved at Playback
cmd.CreateComponent<PositionComponent>(e, new PositionComponent { X = 1, Y = 2 });
cmd.CreateComponent<ManaComponent>(e);
cmd.CreateComponent<PlayerTag>(e);
cmd.DestroyComponent<PlayerTag>(e);
cmd.SetMask(e, 0b10);
cmd.DestroyEntity(e);

cmd.Playback();   // applies every record in order; the buffer is reusable afterwards
```

- Recording performs zero structural migration; `Playback` applies every record immediately, in recording order, and may be called inside a tick.
- The placeholder returned by `CreateEntity` is a buffer-private handle, valid only until playback; referencing it afterwards throws.
- Disposing without `Playback` discards the pending records.
- Suited to batch spawn / batch destroy workloads.
- `SetMask` produces no component events: event-driven collectors update on the next relevant component event, while `IEntityQuery.Refresh()` always sees the new mask.

---

## 12. v1 → v2 Breaking Changes

- `world.Query(matcher, ICollection<...>)` overload removed → use `world.Query(matcher)`, which returns `IEntityQuery`.
- `MinimalWorld` removed → `World` is the only entry point; core managers are built in.
- `EntityGraph` / `ComponentStore<T>` are no longer public (archetype kernel).
- Lifecycle hooks consolidated: `OnRegisterManager` / `RegisterServices` / `OnConstruct` / `OnFirstStart` / `OnStart` / `OnShutdown` → `OnRegister(IManagerRegister, IServiceCollection)` (first `Startup`) / `OnSetup` (every `Startup`) / `OnCleanup` (every `Shutdown`); the `OnTickBegin` / `OnTick` / `OnTickEnd` virtual hooks are removed — the tick is driven internally by `World.BeginTick` / `Tick` / `EndTick`.
- `IEntityCollector.Change()` is marked obsolete → use `Flush()`.
- New in v2: `IDiscreteComponent<T>` / `ITagComponent<T>` component kinds, `Entity.SetMask`, `World.CreateCommandBuffer()`, `IEntityQuery`, system groups (`RegisterGroup` / `Before` / `After`).
- Existing component definitions need no changes; `CreateComponent` / `DestroyComponent` / `GetComponent` / `HasComponent` names are preserved.

---

## 13. Complete Example

```csharp
using System;
using CoreECS;
using CoreECS.Defines;

public struct PositionComponent : IComponent<PositionComponent>
{
    public float X, Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X, Y;
}

public class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityCollector m_movingEntities;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>());
    }

    public void OnTick(ulong tickMask)
    {
        m_movingEntities.Flush();
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            position.RW.X += velocity.RW.X * 0.016f;
            position.RW.Y += velocity.RW.Y * 0.016f;
        }
    }

    public void OnDestroy() => m_movingEntities?.Dispose();
}

class Program
{
    static void Main()
    {
        var world = new World();
        world.Startup();
        world.RegisterSystem<MovementSystem>();

        var entity = world.CreateEntity();
        entity.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 0, Y = 0 };
        entity.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = 10, Y = 5 };

        for (var i = 0; i < 100; i++)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();
            var pos = entity.GetComponent<PositionComponent>();
            Console.WriteLine($"Frame {i}: ({pos.RW.X:F2}, {pos.RW.Y:F2})");
        }

        world.Shutdown();
    }
}
```

---

**English** · [简体中文](QUICK_START.zh-CN.md)

[← Back to README](../README.md) · [README（中文）](../README.zh-CN.md)
