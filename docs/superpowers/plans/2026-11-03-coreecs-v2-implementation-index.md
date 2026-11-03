# CoreECS v2 Implementation Plan Index

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement these plans in dependency order. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current CoreECS implementation with the approved simplified v2 through eight working, reviewable vertical slices.

**Architecture:** Keep one product implementation on `v2-pure`. Build the low-level EntityId and typed paged sparse-set first, switch public component APIs with a short-lived compatibility bridge, then replace query, Collector, scheduling, and World lifecycle before adding benchmarks and documentation.

**Tech Stack:** C# 9, `net8.0;netstandard2.1`, NUnit, BenchmarkDotNet, Microsoft.Extensions.DependencyInjection; no pinned SDK version.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Execute plans in the order listed below; later plans consume interfaces produced by earlier plans.
- Work only in the current `v2-pure` worktree.
- Preserve the user's existing Rider `ECS/` → `Kernel/` rename; do not reset or recreate it.
- Public assembly, root namespace, and NuGet PackageId remain `CoreECS`.
- Kernel targets `net8.0;netstandard2.1`; Test and Benchmark target `net8.0`; language version is C# 9; Nullable is enabled; no `global.json` SDK pin.
- Default EntityId is uint 20/12; `COREECS_ULONG_ENTITY` is ulong 32/32; NuGet packages only uint; CI tests both.
- ComponentRef.Revision/handler revision are uint by default and ulong under the same symbol; journal sequence/epoch remain ulong.
- No CommandBuffer, Analyzer, archetype/Structure, Sparse component category, Typed Query, public storage, sorting, custom storage, parallel scheduling, serialization, or public component/entity change events.
- Every production change follows TDD and ends in a focused Conventional Commit.
- Do not commit `.idea` files or stale `obj/` outputs.

## Dependency Order

1. [`2026-11-03-coreecs-v2-foundation.md`](2026-11-03-coreecs-v2-foundation.md) — rename cleanup, unpinned net8 build, EntityId.
2. [`2026-11-03-coreecs-v2-storage.md`](2026-11-03-coreecs-v2-storage.md) — type registry, exponential packed pages, sparse index, handlers, entity table, typed storages.
3. [`2026-11-03-coreecs-v2-component-api.md`](2026-11-03-coreecs-v2-component-api.md) — component contracts, ComponentRef, Entity, managers, delayed compaction.
4. [`2026-11-03-coreecs-v2-query.md`](2026-11-03-coreecs-v2-query.md) — frozen EntityMatcher and materialized EntityQuery.
5. [`2026-11-03-coreecs-v2-collector.md`](2026-11-03-coreecs-v2-collector.md) — central journal pump, inverted indexes, net membership Collector.
6. [`2026-11-03-coreecs-v2-system-dag.md`](2026-11-03-coreecs-v2-system-dag.md) — groups, anchors, stable topo sort, dirty execution cache.
7. [`2026-11-03-coreecs-v2-world-lifecycle.md`](2026-11-03-coreecs-v2-world-lifecycle.md) — remove MinimalWorld, rollback/restart, manager and DI integration.
8. [`2026-11-03-coreecs-v2-benchmark-docs.md`](2026-11-03-coreecs-v2-benchmark-docs.md) — BenchmarkDotNet, CI matrices, user docs, migration guide.

## Slice Gate

Do not begin the next plan until the current plan has:

1. all focused tests green;
2. full `dotnet test CoreECS.sln --configuration Release` green in default mode;
3. Kernel Release build green for both target frameworks;
4. ulong mode build/test green once Plan 1 introduces the matrix command;
5. no new warnings attributable to the slice;
6. a reviewable commit history matching the plan tasks.

## Existing Worktree State

At plan-writing time, the user has already moved tracked source files from `ECS/` to `Kernel/`, added `Kernel/Kernel.csproj`, and updated the solution/Test project references. Those changes remain uncommitted user work. Plan 1 reviews and finishes them; no plan may discard them.

The design spec itself is committed in:

- `25c7bf8 doc(proj): add simplified v2 design`
- `4a9cf3c doc(proj): refine tag and paging design`

## Review Focus

- Temporary migration bridges must be removed by Plan 5 and verified absent by Plan 7.
- Every slice must preserve default and ulong-mode builds once its touched API compiles.
- User-owned Rider rename changes must be committed only through Plan 1, never reset.
- No later plan may expose storage internals to solve a local performance problem.
- Any new public capability not present in the spec stops execution and returns to design review.
