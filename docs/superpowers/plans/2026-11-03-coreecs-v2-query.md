# CoreECS v2 Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace runtime-interface matching and collection-output queries with a frozen EntityMatcher plan and reusable materialized EntityQuery snapshots.

**Architecture:** Resolve component Types to global TypeIds when an EntityMatcher first binds. Query chooses the smallest required storage or a deduplicated Any union, then materializes EntityIds into its own reusable buffer.

**Tech Stack:** C# 9, typed sparse-set registry, NUnit.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Depends on final Entity/World component API.
- Only built-in EntityMatcher is supported; remove public IEntityMatcher.
- Mask is immutable entity metadata and matches on any intersecting bit.
- Query is public sealed, not IDisposable, and never auto-updates after creation/Refresh.
- No Typed Query, Structure exposure, storage exposure, or ordering guarantee.

## Review Focus

- An empty matcher with default mask must include every live entity; Task 2 tests it.
- Any-only union must deduplicate entities that own multiple Any terms; Task 2 tests it.
- Destroy/reuse after snapshot must not alias a new generation; Task 2 tests stale IDs.
- A frozen matcher must reject every mutator, including duplicate terms; Task 1 tests it.
- Query planning must use live count rather than allocated/tombstone count; Task 2 checks diagnostics.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Replace IEntityMatcher with frozen EntityMatcher plans

**Files:**
- Delete: `Kernel/Defines/IEntityMatcher.cs`
- Rewrite: `Kernel/EntityMatcher.cs`
- Delete: `Kernel/EntityMatcherExtension.cs`
- Rewrite: `Test/EntityMatcherTestUnit.cs`

**Interfaces:**
- Produces: `EntityMatcher.With`, `WithMask(ulong)`, generic `OfAll/OfAny/OfNone` returning EntityMatcher; internal frozen plan exposing TypeId sets, mask, relevance, candidate strategy.

- [ ] **Step 1: Write failing matcher tests**

Cover all/any/none/mask truth table, empty matcher, duplicate term idempotence before freeze, freeze on Bind, every mutator throwing after freeze, relevant component behavior including empty matcher, and any-bit mask semantics.

- [ ] **Step 2: Verify red**

- [ ] **Step 3: Implement EntityMatcher and remove interface/extension**

Use Type sets while configuring and sorted/deduplicated TypeId arrays or compact immutable sets after Bind. No public third-party planning interface.

- [ ] **Step 4: Adapt the temporary old Collector to accept EntityMatcher**

Only compile/behavior adaptation; Plan 5 replaces its algorithm.

- [ ] **Step 5: Run full tests and commit**

```bash
git add Kernel/EntityMatcher.cs Kernel/Defines/IEntityMatcher.cs Kernel/EntityMatcherExtension.cs Kernel/Managers/EntityMatchManager.cs Test/EntityMatcherTestUnit.cs
git commit -m "refactor(core): freeze built-in entity matchers"
```

### Task 2: Add materialized EntityQuery

**Files:**
- Create: `Kernel/EntityQuery.cs`
- Modify: `Kernel/World.cs`
- Create: `Test/EntityQueryTestUnit.cs`
- Modify: `Test/WorldTestUnit.cs`

**Interfaces:**
- Produces: public sealed EntityQuery with Matcher, Entities, Count, indexer, Refresh; World.CreateQuery(EntityMatcher,bool refreshImmediately=true).

- [ ] **Step 1: Write failing Query contract tests**

Test immediate/default capture, false starts empty, Refresh replaces contents, snapshot stability after add/remove, stale generation remains old and does not alias reused index, Count/indexer, invalid use after World shutdown, and no IDisposable/interface.

- [ ] **Step 2: Write failing planner tests with diagnostics**

Test smallest All storage drives candidate count, Any union deduplicates, All+Any uses All candidates, none-only/empty scans live entity table, mask filters before component checks where possible, and tombstones do not inflate candidates.

- [ ] **Step 3: Verify red**

- [ ] **Step 4: Implement EntityQuery and planner paths**

Reuse the same List/array capacity on Refresh. Refresh is a single-threaded mutation boundary; existing `Entities` references observe the refreshed contents after the call returns.

- [ ] **Step 5: Delete old World.Query collection overloads and adapt tests**

Remove matcher extension wrappers. Integration systems use CreateQuery or Collector, not one-shot collection APIs.

- [ ] **Step 6: Verify full suite in both modes and commit**

```bash
git add Kernel/EntityQuery.cs Kernel/World.cs Test/EntityQueryTestUnit.cs Test/WorldTestUnit.cs Test/IntegrationTestUnit.cs
git commit -m "feat(core): add materialized entity queries"
```
