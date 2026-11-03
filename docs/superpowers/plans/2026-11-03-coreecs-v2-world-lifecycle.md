# CoreECS v2 World Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove MinimalWorld and make World the single transactional, restartable owner of managers, DI, entities, collectors, storage, and scheduling.

**Architecture:** World constructs core/custom managers and DI once after the first successful registration, then reuses them across normal start/stop cycles. Startup uses explicit rollback state; normal shutdown clears every cycle-owned object and DAG while preserving reusable manager/DI instances.

**Tech Stack:** C# 9, Microsoft.Extensions.DependencyInjection, existing IInjectionProxy abstraction, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Delete MinimalWorld; World remains public, directly constructible, and inheritable.
- Keep OnRegister(IManagerRegister,IServiceCollection), OnSetup, OnCleanup virtual hooks.
- Keep four IWorldManager methods and call all four every successful cycle despite instance reuse.
- Startup failures propagate and roll back framework state; OnSetup failure does not call OnCleanup.
- Shutdown invalidates all cycle-owned objects; callers must discard them before restart.
- Core managers cannot be replaced by custom manager registration.

## Review Focus

- First Startup failure after partial manager creation must cause the next attempt to run OnRegister again; Task 2 tests it.
- Restart after a previously successful cycle must reuse DI/manager instances but reset entity IDs/DAG/collectors; Task 3 tests identities and state.
- OnSetup failure must not call OnCleanup, while rollback manager exceptions must not hide the original failure; Task 2 tests exception identity.
- A live Collector and pending System mutation at Shutdown must be safely invalidated/cleared; Task 3 tests both.
- Illegal lifecycle transitions must throw without partially changing state; Task 1 tests each transition.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Merge MinimalWorld state machine into World

**Files:**
- Rewrite: `Kernel/World.cs`
- Delete: `Kernel/MinimalWorld.cs`
- Modify: `Kernel/Defines/IWorld.cs`
- Rewrite: `Test/WorldTestUnit.cs`
- Create: `Test/WorldStateMachineTestUnit.cs`

**Interfaces:**
- Produces directly constructible/inheritable World; Ready, Ticking, TickCount; Startup/Shutdown/BeginTick/Tick/EndTick strict transitions; virtual OnRegister/OnSetup/OnCleanup.

- [ ] **Step 1: Write failing lifecycle-state tests**

Cover normal cycle, direct base World, duplicate Startup, Shutdown before Startup, nested BeginTick, Tick/EndTick without BeginTick, Shutdown while ticking, repeated Shutdown, and TickCount reset on each Startup.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Move state and fixed tick orchestration into World**

BeginTick applies DAG/pending systems; Tick executes cache; EndTick finalizes systems then component compact/entity releases. No overridable tick hooks.

- [ ] **Step 4: Delete MinimalWorld and adapt subclasses/tests**

- [ ] **Step 5: Verify and commit**

```bash
git add Kernel/World.cs Kernel/MinimalWorld.cs Kernel/Defines/IWorld.cs Test/WorldTestUnit.cs Test/WorldStateMachineTestUnit.cs
git commit -m "refactor(core): merge MinimalWorld into World"
```

### Task 2: Make first Startup transactional and retryable

**Files:**
- Rewrite: `Kernel/ManagerMediator.cs`
- Modify: `Kernel/World.cs`
- Create: `Test/WorldStartupRollbackTestUnit.cs`

**Interfaces:**
- Produces ManagerMediator registration/build/start/stop/destroy operations with explicit completion tracking and reverse rollback; World retry after failed first Startup.

- [ ] **Step 1: Write failure-injection managers/proxy tests**

Inject failures in OnRegister, service provider construction, manager activation, OnManagerCreated, OnWorldStarted, and OnSetup. Assert original exception propagates, completed phases unwind in reverse, World is retryable, failed first Startup reruns OnRegister, and OnSetup failure never calls OnCleanup.

- [ ] **Step 2: Add rollback-exception test**

Make rollback OnWorldEnded/OnManagerDestroyed throw; assert errors are logged, rollback continues, and the original Startup exception is what caller receives.

- [ ] **Step 3: Verify red**

- [ ] **Step 4: Implement transactional construction/boot**

Do not catch-and-continue manager startup failures. Track distinct manager instances once even when interface and implementation keys map to the same object.

- [ ] **Step 5: Verify and commit**

```bash
git add Kernel/ManagerMediator.cs Kernel/World.cs Test/WorldStartupRollbackTestUnit.cs
git commit -m "fix(core): make world startup rollback retryable"
```

### Task 3: Implement normal restart and cycle cleanup

**Files:**
- Modify: `Kernel/World.cs`
- Modify: `Kernel/ManagerMediator.cs`
- Modify: core managers under `Kernel/Managers/`
- Create: `Test/WorldRestartTestUnit.cs`
- Modify: `Test/ManagerLifecycleWiringTestUnit.cs` if ported from v2

**Interfaces:**
- Produces repeated successful Startup/Shutdown cycles reusing manager/DI instances while clearing entities, storages, Collectors, journal, systems, groups, anchors, registration handles, and EntityId table.

- [ ] **Step 1: Write failing restart tests**

Assert OnRegister once across two successful cycles; OnSetup/OnCleanup each cycle; each manager receives Created/Started/Ended/Destroyed each cycle; manager and injection proxy object identity is reused; entity IDs may reset; old Entity/Ref/Query/Collector/registration handles are invalid by contract; DAG is empty until second OnSetup registers it.

- [ ] **Step 2: Write shutdown-with-pending-state tests**

Include dirty storage tombstones, unpumped journal, live Collector, pending system add/remove, and allocated pages. Shutdown completes without NRE; stale Collector Dispose is no-op.

- [ ] **Step 3: Implement per-manager cycle reset**

Keep process-global ComponentTypeRegistry. Release per-World storage pages at Shutdown as specified; reset EntityTable; clear journal/subscription lists and DAG.

- [ ] **Step 4: Verify full default/ulong suites and commit**

```bash
git add Kernel Test/WorldRestartTestUnit.cs Test/ManagerLifecycleWiringTestUnit.cs
git commit -m "feat(core): support repeatable world lifecycles"
```

### Task 4: Final public API and source-surface audit

**Files:**
- Create: `Test/KernelSourceAnalysisTestUnit.cs`
- Modify/delete: remaining obsolete public files under `Kernel/Defines/`
- Modify: `Kernel/Kernel.csproj`

**Interfaces:**
- Produces the exact approved public surface and no temporary migration types.

- [ ] **Step 1: Add source/reflection contract tests**

Assert absence of MinimalWorld, IEntityMatcher, IEntityQuery, IEntityCollector, EntityGraph, Change(), SetMask, generic plural GetComponents, Query collection overloads, public storage, public component/entity events, unsafe ComponentRef casts, CommandBuffer, analyzer references, and old ECS namespace/project strings.

- [ ] **Step 2: Assert required surface**

Reflection verifies EntityId, Entity, ComponentRef types, IComponent/ITagComponent, EntityMatcher/EntityQuery/EntityCollector sealed classes, World hooks, DAG registrations, and four IWorldManager methods.

- [ ] **Step 3: Remove obsolete code and run Release builds/tests**

Run Kernel for both TFMs and Test in default/ulong modes.

- [ ] **Step 4: Commit**

```bash
git add Kernel Test/KernelSourceAnalysisTestUnit.cs
git commit -m "refactor(core): finalize v2 public surface"
```
