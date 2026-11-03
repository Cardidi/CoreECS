# CoreECS v2 System DAG Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic grouped DAG registration and a dirty-only execution cache while preserving current-tick order under tick-time modifications.

**Architecture:** Store groups and systems in a registration tree, flatten it deterministically, expand Before/After anchors into graph edges, and run a stable topological sort. SystemManager owns a committed graph/execution cache plus pending final-state mutations made during Tick.

**Tech Stack:** C# 9, DI-created systems, stable topological sort, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Preserve one System instance per concrete Type and globally unique group names.
- Keep GroupInsertMode Early/Later, nested groups, system/group anchors, and forward references.
- No UnregisterGroup, parallel scheduler, or resource-access graph.
- Rebuild/copy execution cache only when DAG dirty.
- Tick-time changes affect the next BeginTick only; current execution cache is immutable.
- ISystem OnCreate/OnTick/OnDestroy are mandatory; TickGroup defaults MaxValue.

## Review Focus

- A cycle involving only part of the graph must fall back the whole order to registration flatten order; Task 2 tests it.
- Duplicate edges must not over-increment indegree and create a false cycle; Task 2 tests deduplication.
- Register→unregister before first activation must call neither lifecycle method; Task 3 tests final-state convergence.
- Unregistering a not-yet-run current-tick system must not remove it from the captured cache; Task 3 tests it still runs once.
- Begin/End observer callbacks must not mutate DAG and their exceptions must not skip system execution; Task 4 tests both.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Add registration tree and fluent handles

**Files:**
- Create: `Kernel/Defines/GroupInsertMode.cs`
- Create: `Kernel/Managers/SystemScheduleNodes.cs`
- Create: `Kernel/Managers/SystemRegistration.cs`
- Create: `Kernel/Managers/GroupRegistration.cs`
- Create: `Test/SystemGroupRegistrationTestUnit.cs`

**Interfaces:**
- Produces SystemSchedule root/group/system node lookup; public registration handles with Before/After overloads for system Type/generic and group name.

- [ ] **Step 1: Write failing registration tests**

Cover root registration, nested groups, Early/Later child order, globally duplicate group error, duplicate system error, unknown parent error, forward anchor storage, and lack of UnregisterGroup API.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement tree and handles**

Anchors are declarations only; do not resolve in registration methods. Registration handles become invalid after World shutdown/restart.

- [ ] **Step 4: Verify and commit**

```bash
git add Kernel/Defines/GroupInsertMode.cs Kernel/Managers/SystemScheduleNodes.cs Kernel/Managers/SystemRegistration.cs Kernel/Managers/GroupRegistration.cs Test/SystemGroupRegistrationTestUnit.cs
git commit -m "feat(core): add grouped system registrations"
```

### Task 2: Implement stable DAG resolution

**Files:**
- Create: `Kernel/Managers/SystemSchedule.cs`
- Create: `Test/SystemOrderingTestUnit.cs`

**Interfaces:**
- Consumes registration nodes/anchors.
- Produces `BuildExecutionOrder() -> IReadOnlyList<Type>`, `IsDirty`, and internal rebuild diagnostics.

- [ ] **Step 1: Write failing ordering tests**

Cover unconstrained flatten order, system Before/After, group-to-system, system-to-group, group-to-group subtree edges, cross-level anchors, forward resolution, duplicate edge deduplication, unknown target log/ignore, self/ancestor no-op, stable tie-break, and cycle log/full fallback.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement flatten + stable topo sort**

Deduplicate each edge before incrementing indegree. Select the lowest flatten index among zero-indegree nodes. Unknown anchors log through existing Log utility.

- [ ] **Step 4: Verify and commit**

```bash
git add Kernel/Managers/SystemSchedule.cs Test/SystemOrderingTestUnit.cs
git commit -m "feat(core): resolve stable system DAG order"
```

### Task 3: Integrate pending graph mutations and dirty execution cache

**Files:**
- Rewrite: `Kernel/Managers/SystemManager.cs`
- Modify: `Kernel/World.cs`
- Rewrite: `Test/SystemTestUnit.cs`
- Create: `Test/SystemConvergenceTestUnit.cs`

**Interfaces:**
- Produces World.RegisterGroup overloads, RegisterSystem returning handles, UnregisterSystem; BeginTick apply/rebuild; Tick cached execution; EndTick finalize pending removals.

- [ ] **Step 1: Write failing activation/cache tests**

Assert tick-outside registration immediately constructs/OnCreate but executes next BeginTick; clean BeginTick leaves rebuild/copy diagnostics unchanged; dirty BeginTick rebuilds once; TickGroup any-bit filtering remains.

- [ ] **Step 2: Write failing tick-time convergence tests**

Cover add, remove, remove+readd, queued add+cancel, group move, anchor change, and multiple same-type operations. Assert current cache remains unchanged, final desired next graph wins, no lifecycle calls for never-instantiated systems, and removed cached system completes current tick then OnDestroy after tick.

- [ ] **Step 3: Verify red**

- [ ] **Step 4: Implement committed/pending graph integration**

Represent final desired state rather than replaying operation history. Snapshot execution references at BeginTick only when dirty. Reuse existing instances when registration survives.

- [ ] **Step 5: Verify full tests and commit**

```bash
git add Kernel/Managers/SystemManager.cs Kernel/World.cs Test/SystemTestUnit.cs Test/SystemConvergenceTestUnit.cs
git commit -m "feat(core): cache convergent system schedules"
```

### Task 4: Restrict execution observation events

**Files:**
- Modify: `Kernel/Managers/SystemManager.cs`
- Create: `Test/SystemExecutionEventTestUnit.cs`

**Interfaces:**
- Produces only public OnSystemBeginExecute/OnSystemEndExecute events; removes public teardown/cleanup events.

- [ ] **Step 1: Write failing event tests**

Assert Begin before OnTick and End after; no handlers means no allocation; event exception logs and system still runs; DAG mutation from either callback throws/logs without changing graph; OnTick may modify pending next graph.

- [ ] **Step 2: Implement event dispatch guard**

Reuse/simplify EventDispatchState only if it enforces the exact no-DAG-mutation contract. Do not expose teardown/cleanup phases.

- [ ] **Step 3: Verify and commit**

```bash
git add Kernel/Managers/SystemManager.cs Test/SystemExecutionEventTestUnit.cs
git commit -m "refactor(core): narrow system execution events"
```
