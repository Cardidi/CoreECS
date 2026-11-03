# CoreECS v2 Storage Kernel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the internal paged sparse-set, component type registry, pooled handlers, and versioned entity table while leaving the existing public managers operational.

**Architecture:** Build the new kernel beside the old ComponentStore as internal code. Packed columns use exponential pages 4/8/16/...; sparse indexes use fixed lazy pages. Component handlers carry identity, revision, journal stamp, storage location, and the intrusive entity component links.

**Tech Stack:** C# 9, .NET 9, netstandard2.1-compatible arrays/generics, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Depends on completed Foundation plan and `EntityId`.
- All types in this plan are internal. Public `ITagComponent<T>` and automatic Tag classification are introduced in Plan 3 after the final EntityId Hook signature exists.
- Packed pages are exactly 4, 8, 16, 32... and total capacity after n pages is `2^(n+2)-4`.
- Sparse pages are fixed-size, lazy, and selected by benchmark later; use an internal constant that tests do not expose as public API.
- No existing public component behavior changes in this plan.

## Review Focus

- Packed slot/page math at every boundary (3/4, 11/12, 27/28) must be exact; Task 1 tests boundaries.
- A high EntityId index in a rare component must allocate one fixed sparse page, not an exponential packed page; Task 2 tests allocation count.
- Reusing a pooled handler must not revive an old captured generation; Task 3 tests stale generations.
- Swap-compaction must update sparse index, moved EntityId, and handler offset atomically; Task 5 tests all three.
- Entity index reuse must distinguish old/new generations in sparse contains; Task 5 tests same index with a new generation.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Implement exponential packed columns

**Files:**
- Create: `Kernel/Structures/PackedPageLayout.cs`
- Create: `Kernel/Structures/PagedColumn.cs`
- Create: `Test/PagedColumnTestUnit.cs`

**Interfaces:**
- Produces: `PackedPageLayout.PageSize(int)`, `CapacityBeforePage(int)`, `Locate(int slot, out int page, out int offset)`; internal `PagedColumn<T>` with `CountPages`, `Capacity`, indexer by packed slot, `EnsureSlot`, and `ClearSlot`.

- [ ] **Step 1: Write failing layout/column tests**

Test exact page sizes `4,8,16,32`, capacities `4,12,28,60`, and location boundaries: slots `0,3` page 0; `4,11` page 1; `12,27` page 2; `28` page 3. Verify growing to slot 28 preserves values in earlier pages and never replaces existing page arrays (internal test hook may expose page references).

- [ ] **Step 2: Run focused tests red**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~PagedColumnTestUnit`

Expected: FAIL because types do not exist.

- [ ] **Step 3: Implement page math and generic column**

Use highest-bit math on `slot + 4`; provide a netstandard-compatible fallback if `BitOperations.Log2` is unavailable. `EnsureSlot` allocates all preceding packed pages in order.

- [ ] **Step 4: Run focused and full tests**

Expected: PagedColumn tests and existing suite PASS in both EntityId modes.

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures/PackedPageLayout.cs Kernel/Structures/PagedColumn.cs Test/PagedColumnTestUnit.cs
git commit -m "feat(core): add exponential packed columns"
```

### Task 2: Implement the fixed lazy sparse index

**Files:**
- Create: `Kernel/Structures/SparseIndex.cs`
- Create: `Test/SparseIndexTestUnit.cs`

**Interfaces:**
- Consumes: EntityId index/generation.
- Produces: `SparseIndex.TryGet(EntityId, out int packedSlot)`, `Set(EntityId,int)`, `Remove(EntityId)`, `Clear()` and internal allocated-page diagnostics.

- [ ] **Step 1: Write failing sparse tests**

Cover absent lookup, set/get/remove, same index wrong generation, replacing a slot only after explicit remove, low and near-max indexes, and one high index allocating only its fixed page.

- [ ] **Step 2: Verify red**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~SparseIndexTestUnit`

- [ ] **Step 3: Implement paged sparse entries**

Each entry stores packed slot + 1 and the EntityId generation needed to validate the incarnation. Null pages represent entirely absent index ranges.

- [ ] **Step 4: Verify both modes**

Run focused tests in default and `-p:CoreECSUseUlongEntity=true`; expect PASS.

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures/SparseIndex.cs Test/SparseIndexTestUnit.cs
git commit -m "feat(core): add paged sparse index"
```

### Task 3: Add component type metadata and pooled handlers

**Files:**
- Create: `Kernel/Structures/ComponentKind.cs`
- Create: `Kernel/Structures/ComponentTypeRegistry.cs`
- Create: `Kernel/Structures/ComponentHandler.cs`
- Create: `Kernel/Utils/ComponentHandlerPool.cs`
- Create: `Test/ComponentTypeRegistryTestUnit.cs`
- Create: `Test/ComponentHandlerTestUnit.cs`

**Interfaces:**
- Produces: process-global `ComponentTypeRegistry.GetOrRegister<T>()`; generic static cache; ComponentKind Data/Tag; pooled `ComponentHandler.Bind(...)`, `Invalidate()`, `BindGeneration`, EntityId, TypeId, conditionally uint/ulong Revision, ulong LastJournalEpoch, packed offset, storage reference, Previous/Next entity links.

- [ ] **Step 1: Write failing registry tests**

Assert one stable TypeId per T across calls/world-independent contexts, distinct IDs for distinct types, and generic cache identity. All types register as Data in this internal phase; Plan 3 adds final ITag classification without changing assigned TypeIds.

- [ ] **Step 2: Write failing handler pool tests**

Assert bind initializes all fields, revision starts 0, increment wraps while skipping 0 in both numeric modes, invalidation clears references/links, pooled reuse increments BindGeneration, and `(handler, oldGeneration)` remains stale after reuse.

- [ ] **Step 3: Implement metadata and handler pool**

Use minimal synchronization only around first process-global Type registration. Handler pool is single-threaded and clears retained storage/neighbor references on release.

- [ ] **Step 4: Verify focused/full suites in both modes**

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures Kernel/Utils/ComponentHandlerPool.cs Test/ComponentTypeRegistryTestUnit.cs Test/ComponentHandlerTestUnit.cs
git commit -m "feat(core): add component metadata and handlers"
```

### Task 4: Implement the versioned EntityTable

**Files:**
- Create: `Kernel/Structures/EntityRecord.cs`
- Create: `Kernel/Structures/EntityTable.cs`
- Create: `Test/EntityTableTestUnit.cs`

**Interfaces:**
- Produces: `Create(ulong mask) -> EntityId`, `IsAlive(EntityId)`, `GetMask(EntityId)`, component-head get/set, `MarkDestroyed(EntityId)`, `CommitReleases()`, `ClearForRestart()`; index free-list reuse occurs only in CommitReleases.

- [ ] **Step 1: Write failing entity-table tests**

Cover invalid default, create index starts 1, immutable mask, logical death before commit, no same-phase index reuse, reuse after commit with next generation, generation wrap skipping 0, and clear-for-restart resetting IDs as specified.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement EntityTable**

Use paged records or another allocation strategy that supports the EntityId index maximum without preallocating it. Keep the handler-head field internal and nullable.

- [ ] **Step 4: Verify focused/full suites in both modes**

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures/EntityRecord.cs Kernel/Structures/EntityTable.cs Test/EntityTableTestUnit.cs
git commit -m "feat(core): add versioned entity table"
```

### Task 5: Implement data and Tag sparse-set storages

**Files:**
- Create: `Kernel/Structures/IComponentStorage.cs`
- Create: `Kernel/Structures/ComponentStorage.cs`
- Create: `Kernel/Structures/TagStorage.cs`
- Create: `Kernel/Structures/ComponentStorageRegistry.cs`
- Create: `Test/ComponentStorageTestUnit.cs`
- Create: `Test/TagStorageTestUnit.cs`

**Interfaces:**
- Consumes: PagedColumn, SparseIndex, ComponentHandler, EntityTable.
- Produces: internal storage APIs `Contains`, `Create`, `GetHandler`, typed payload ref access, `MarkRemove`, `Compact`, `Clear`, live/allocated counts, packed EntityId iteration for Query.

- [ ] **Step 1: Write failing data-storage tests**

Cover unique create, default/initial payload, sparse O(1) lookup, logical removal, append-not-hole-reuse before compact, packed page growth preserving refs, compact moving tail into holes, handler offset and sparse map relocation, capacity retained after compact, and stale EntityId generation rejection.

- [ ] **Step 2: Write failing Tag-storage tests**

Cover no payload allocation, valid handler, independent per-entity revision, logical remove/compact, and shared default access supplied later by ComponentRef.

- [ ] **Step 3: Verify red**

- [ ] **Step 4: Implement storage registry and storages**

The registry maps TypeId to one storage instance per World and accepts the ComponentKind selected by its caller; Plan 3 wires public type metadata to that selection. Store the handler in both its packed column and the EntityRecord intrusive list. Removal marks EntityId invalid in packed storage, removes sparse membership immediately, detaches and invalidates the handler, returns it to the pool, clears the handler packed slot, and records a hole for compact. Payload bytes remain untouched until compact/reuse.

- [ ] **Step 5: Verify focused/full suites in both modes**

- [ ] **Step 6: Commit**

```bash
git add Kernel/Structures Test/ComponentStorageTestUnit.cs Test/TagStorageTestUnit.cs
git commit -m "feat(core): add typed sparse-set storages"
```
