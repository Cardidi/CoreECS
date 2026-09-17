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
11. [Complete Example](#11-complete-example)

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

After `Startup()`, use `World.InjectionProxy` to resolve services (`null` until the first `Startup()` completes).

> **Thread safety:** Worlds are not thread-safe. Access them from a single thread (typically the main/game thread).

When finished, call `World.Shutdown()` to release resources.

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

Registration order is execution order (FIFO). Avoid registering/unregistering between `BeginTick()` and `EndTick()` — changes are queued until the next `BeginTick()`. Entity/component ops are **not** deferred.

```csharp
var world = new World();
world.Startup();
world.RegisterSystem<MovementSystem>();

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

## 11. Complete Example

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
