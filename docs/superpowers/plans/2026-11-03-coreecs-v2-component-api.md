# CoreECS v2 Component API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Switch CoreECS entities and components onto EntityTable + typed sparse-set storage while preserving stable ComponentRef handles and keeping the still-old matcher/Collector temporarily operational.

**Architecture:** Replace public component contracts, refs, Entity, and managers in one controlled vertical slice. Use the new handler as the temporary `IComponentRefCore` bridge and maintain the old EntityGraph list only until the Collector plan removes it; the authoritative ownership remains EntityTable + intrusive handlers.

**Tech Stack:** C# 9, EntityId, typed paged sparse-set, pooled ComponentHandler, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Depends on Foundation and Storage plans.
- Component Hook signatures use EntityId and have no defaults for data components.
- ITagComponent explicitly supplies empty base Hook implementations; dispatcher never invokes Tag Hooks.
- Each entity owns at most one T.
- Public component/entity change events are removed by the end of the Collector plan; a clearly marked internal bridge may survive only until then.
- Logical deletion is immediate; physical compact and EntityId free-list release occur at EndTick.

## Review Focus

- A Hook that tries any structural World operation must fail before mutating storage; Task 1 tests create and destroy reentrancy.
- OnCreate mutation of the stored struct must persist; Task 1 tests a field set by Hook.
- Handler pool reuse must not revive a stale ComponentRef; Task 2 tests typed and untyped refs.
- Destroying one component and compacting another slot must preserve the moved component's EntityId/ref; Task 4 tests it across EndTick.
- Temporary EntityGraph compatibility data must never become authoritative or survive Plan 5; Task 3 labels and tests the bridge.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Finalize component contracts and Hook dispatch

**Files:**
- Modify: `Kernel/Defines/IComponent.cs`
- Create: `Kernel/Defines/ITagComponent.cs`
- Create: `Kernel/Structures/ComponentHookDispatcher.cs`
- Create: `Kernel/Structures/StructuralMutationGuard.cs`
- Modify temporarily: `Kernel/Managers/ComponentManager.cs`
- Create: `Test/ComponentInterfaceTestUnit.cs`
- Modify: all test component declarations under `Test/*.cs`

**Interfaces:**
- Produces: final `IComponent<T>` and `ITagComponent<T>` signatures; `ComponentHookDispatcher<T>.InvokeCreate(ref T,EntityId)` / `InvokeDestroy(ref T,EntityId)` that are no-ops for Tag; World-scoped guard queried by structural APIs.

- [ ] **Step 1: Write failing contract tests**

Tests prove:

- data component must implement both EntityId Hook signatures;
- empty Tag compiles without declaring Hooks;
- Tag TypeInfo is Tag and dispatcher calls neither a same-named public method nor base Hook;
- data OnCreate mutates the stored component by ref;
- Hook exceptions are logged and swallowed;
- structural operation attempted from OnCreate/OnDestroy throws `InvalidOperationException` and leaves the outer operation coherent.

- [ ] **Step 2: Run focused tests red**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentInterfaceTestUnit`

- [ ] **Step 3: Implement the contracts and dispatcher**

Use explicit default base-interface implementations in ITagComponent. Do not use `sealed` on explicit implementations (C# forbids it). Update ComponentTypeRegistry so types assignable to their closed `ITagComponent<T>` register as Tag while preserving already assigned TypeIds; reject Tag types with instance fields. Dispatcher branches on cached ComponentKind before any constrained Hook call. Until Task 3 replaces the managers, adapt legacy ComponentManager's ulong entity IDs to `EntityId.Create(index, 1)` solely for Hook calls and route both create/destroy through the dispatcher/guard; this adapter is removed in Task 3.

- [ ] **Step 4: Mechanically update test components**

Every data test struct gets explicit empty or existing EntityId Hooks. Convert intentional marker test types to ITagComponent only where the test needs Tag semantics; do not turn all empty data structs into Tags.

- [ ] **Step 5: Run full tests and commit**

```bash
git add Kernel/Defines Kernel/Structures/ComponentHookDispatcher.cs Kernel/Structures/StructuralMutationGuard.cs Test
git commit -m "feat(core): finalize component lifecycle contracts"
```

### Task 2: Replace ComponentRef with handler-backed refs

**Files:**
- Rewrite: `Kernel/Defines/ComponentRef.cs`
- Modify: `Kernel/Structures/ComponentHandler.cs`
- Modify: `Kernel/Structures/IComponentStorage.cs`
- Modify: `Kernel/Structures/ComponentStorage.cs`
- Modify: `Kernel/Structures/TagStorage.cs`
- Rewrite: `Test/ComponentTestUnit.cs`
- Add/Modify: `Test/ComponentHandlerTestUnit.cs`

**Interfaces:**
- Produces final public `ComponentRef`, `ComponentRef<T>` with NotNull, EntityId, Revision, RO, RW, Inspect, Typed, conversions; all handles capture handler + BindGeneration. Removes unsafe/noSafeCheck conversion.

- [ ] **Step 1: Write failing ComponentRef tests**

Cover valid typed/untyped conversion, wrong type exception, stale after remove, stale after handler pool reuse, equality/hash include BindGeneration, EntityId, RO no revision, RW revision each getter, conditional uint/ulong Revision property type, revision wrap skipping 0, cached raw ref only bumps once, Tag NotNull/RO shared default/RW independent revision, and handler relocation preserving a preexisting ref.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement handler-backed refs**

Handler directly owns Revision and LastJournalEpoch. `RW` calls an internal change sink only when interest is enabled and this pump epoch has no entry yet. The sink can be null until Plan 5.

- [ ] **Step 4: Verify focused/full suites in both modes**

- [ ] **Step 5: Commit**

```bash
git add Kernel/Defines/ComponentRef.cs Kernel/Structures Test/ComponentTestUnit.cs Test/ComponentHandlerTestUnit.cs
git commit -m "feat(core): replace component references with stable handlers"
```

### Task 3: Switch ComponentManager and EntityManager to the new kernel

**Files:**
- Rewrite: `Kernel/Managers/ComponentManager.cs`
- Rewrite: `Kernel/Managers/EntityManager.cs`
- Modify temporarily: `Kernel/EntityGraph.cs`
- Modify temporarily: `Kernel/Defines/IEntityCollector.cs`
- Rewrite: `Test/ComponentManagerTestUnit.cs`
- Rewrite: `Test/EntityManagerTestUnit.cs`
- Keep until Plan 5: `Kernel/Defines/IComponentRefCore` compatibility members currently located in `ComponentRef.cs`

**Interfaces:**
- ComponentManager produces: storage registry access, Create<T>(EntityId,in T), Get<T>, Has<T>, Destroy<T>, Destroy(ComponentRef), CompactDirtyStores, revision-interest sink.
- EntityManager produces: EntityTable-backed Create(mask), Get/IsAlive, Destroy, CommitReleases, handler-chain attach/detach.

- [ ] **Step 1: Write failing manager tests against EntityId**

Assert unique component type, Hook ordering, logical immediate remove, deferred compact, EntityId release only after commit, destroy entity traverses only its intrusive handler list, and Tag Hook skip.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement managers over EntityTable and storage registry**

Maintain a temporary EntityGraph/RwComponents compatibility view for the old EntityMatchManager. Mark it internal and `[Obsolete("Temporary v2 migration bridge")]`; handler is authoritative. Update it from attach/detach only.

- [ ] **Step 4: Convert collector buffer IDs mechanically to EntityId**

Do not change Collector phase semantics yet. This is required so existing Collector tests compile after Entity IDs switch.

- [ ] **Step 5: Run manager, Collector, then full tests**

Expected: current Collector behavior remains green except tests intentionally deferred to Plan 5.

- [ ] **Step 6: Commit**

```bash
git add Kernel/Managers Kernel/EntityGraph.cs Kernel/Defines/IEntityCollector.cs Test
git commit -m "refactor(core): move managers to typed storages"
```

### Task 4: Switch Entity and World component operations

**Files:**
- Rewrite: `Kernel/Entity.cs`
- Rewrite: `Kernel/EntityExtension.cs`
- Modify: `Kernel/World.cs`
- Modify: `Kernel/Defines/IWorld.cs`
- Rewrite: `Test/EntityTestUnit.cs`
- Rewrite: `Test/WorldTestUnit.cs`
- Modify: `Test/IntegrationTestUnit.cs`
- Modify: `Test/StressTestUnit.cs`

**Interfaces:**
- Produces final Entity identity `(World,EntityId)`, immutable Mask, Create/Get/TryGet/GetOrCreate/Has/Destroy APIs, and `int GetComponents(ICollection<ComponentRef>)`.
- World produces `CreateEntity(ulong mask=ulong.MaxValue)`, `GetEntity(EntityId)`, `DestroyEntity(Entity/EntityId)` and EndTick compact/release behavior.

- [ ] **Step 1: Write failing Entity API tests**

Cover World-local equality, immutable mask, invalid entity calls, duplicate Create exception, missing DestroyComponent exception, repeated DestroyEntity no-op, GetOrCreate old bool semantics, non-alloc untyped enumeration, and no generic plural/array API in source analysis.

- [ ] **Step 2: Write failing tick-boundary tests**

Create multiple components/entities, remove a middle packed slot during tick, assert raw slot positions do not move until EndTick, assert old ComponentRef remains valid after EndTick and refers to the same EntityId/data, and assert removed refs are invalid immediately.

- [ ] **Step 3: Verify red**

- [ ] **Step 4: Implement Entity/World APIs**

Structural APIs enter the mutation guard. `EndTick` compacts dirty storages before committing EntityId releases. Do not pump Collector journals at EndTick.

- [ ] **Step 5: Run focused, integration, stress, and full tests in both modes**

- [ ] **Step 6: Commit**

```bash
git add Kernel/Entity.cs Kernel/EntityExtension.cs Kernel/World.cs Kernel/Defines/IWorld.cs Test
git commit -m "feat(core): switch public entity and component APIs"
```
