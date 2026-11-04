# Migrating from v1 to CoreECS v2

v2 is a deliberate breaking release. It keeps the state-first idea — stable component handles, revision tracking, collectors, explicit phases — but replaces the storage and public surface that had accumulated too much machinery.

## At a glance

| v1 | v2 |
|----|----|
| `ulong` entity IDs | `EntityId` struct (default uint 20/12; opt-in ulong 32/32) |
| `EntityGraph` component list | Intrusive handler chain + typed sparse-set storage |
| `ComponentStore<T>` AoS groups | Paged SoA component storage |
| Multiple components of one type per entity | One component per type |
| `IComponent<T>` with default hooks | Mandatory `OnCreate(EntityId)` / `OnDestroy(EntityId)` |
| — | `ITagComponent<T>` membership-only components |
| `IEntityMatcher` | Concrete frozen `EntityMatcher` |
| `World.Query(matcher, collection)` | `world.CreateQuery(matcher, refreshImmediately)` |
| `IEntityQuery` | Public sealed `EntityQuery` snapshot |
| `IEntityCollector` | Public sealed `EntityCollector` |
| `Collector.Change()` | `Collector.Flush()` |
| `MinimalWorld` + `World` | Single `World` |
| Tick-end component pool cleanup | Logical removal + `EndTick` compaction and ID release |
| Mutable entity mask | Immutable mask supplied at creation |

## 1. Components

v1:

```csharp
public struct Position : IComponent<Position>
{
    public float X;
    public float Y;
}
```

v2:

```csharp
public struct Position : IComponent<Position>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}
```

- Hooks receive only `EntityId`.
- Hooks must not change entity or component structure.
- A component may not be added twice to the same entity.
- Marker components can use `ITagComponent<T>`:

```csharp
public struct Stunned : ITagComponent<Stunned> { }
```

## 2. Entity identity

```csharp
Entity entity = world.CreateEntity();
EntityId id = entity.EntityId;

// Old v1: ulong id = entity.EntityId;
```

`EntityId` exposes `Index` and `Generation`, compares by both, and uses `default` as invalid. `Entity` compares world identity plus `EntityId`; `EntityId` alone is world-local.

`Entity.Mask` is immutable after creation and is no longer set through a mutating API.

## 3. Component access

```csharp
ComponentRef<Position> position = entity.CreateComponent(new Position());
ref readonly Position read = ref position.RO;
ref Position write = ref position.RW;

bool found = entity.TryGetComponent(out ComponentRef<Position> existing);
bool existed = entity.GetOrCreateComponent(out var ensured);

Entity.DestroyEntity(entity);
```

Removed APIs:

- Array-returning `GetComponents()`;
- Generic plural `GetComponents<T>()`;
- Unsafe/no-safe-check `ComponentRef` conversions;
- `ComponentRef.RuntimeType`.

Only the non-allocating untyped form remains:

```csharp
int count = entity.GetComponents(ICollection<ComponentRef> output);
```

## 4. Queries

v1:

```csharp
world.Query(EntityMatcher.With.OfAll<Position>(), results);
```

v2:

```csharp
EntityQuery query = world.CreateQuery(EntityMatcher.With.OfAll<Position>());
foreach (EntityId id in query.Entities) { }

query.Refresh(); // recapture the current world
```

`EntityQuery` is a materialized snapshot, is not disposable, and does not update automatically. Matchers freeze on first bind; create a new matcher for different conditions.

## 5. Collectors

v1:

```csharp
IEntityCollector collector = world.CreateCollector(matcher);
collector.Change();
```

v2:

```csharp
using EntityCollector collector = world.CreateCollector(matcher);
collector.Flush();
```

The flags and four buffers keep their meaning, but membership is published as a net change relative to the previous flush. `Shutdown()` invalidates live collectors; `Dispose()` is idempotent.

## 6. World

- `MinimalWorld` is gone. Derive from `World` directly.
- Hooks: `OnRegister(IManagerRegister, IServiceCollection)`, `OnSetup()`, `OnCleanup()`.
- `OnRegister` runs once after the first successful startup.
- A successful `Shutdown()` may be followed by another `Startup()`; managers and DI are reused.
- After shutdown, discard every old entity, component handle, query, collector, and registration handle.

## 7. Systems

```csharp
world.RegisterGroup("Gameplay");
world.RegisterSystem<MovementSystem>("Gameplay")
    .After<InputSystem>()
    .Before("Render");
```

- `ISystem` requires `OnCreate`, `OnTick`, and `OnDestroy`.
- `TickGroup` still defaults to `ulong.MaxValue`.
- Tick-time DAG changes apply at the next `BeginTick`.
- Cycles are logged and fall back to registration order.

## 8. Removed v2 non-goals

The following are intentionally absent:

- `CommandBuffer`
- Roslyn ref analyzer
- Archetype/Structure storage
- Sparse component category
- Typed multi-component query
- Public storage/Span access
- Component sorting
- Custom storage
- Automatic parallel scheduling
- Serialization/snapshots
- Public component/entity change events

## 9. Build modes

Default build: uint EntityId (20-bit index + 12-bit generation).

```bash
dotnet test CoreECS.sln -p:CoreECSUseUlongEntity=true
```

ulong mode: 32-bit index + 32-bit generation. NuGet publishes only the default uint build.
