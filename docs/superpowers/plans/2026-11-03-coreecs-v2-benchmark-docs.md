# CoreECS v2 Benchmark, CI, and Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add repeatable BenchmarkDotNet coverage, finish CI/package validation, and publish accurate bilingual v2 documentation and migration guidance.

**Architecture:** Keep benchmark measurements outside NUnit and CI timing gates. CI validates functional behavior and internal complexity counters in uint/ulong modes; BenchmarkDotNet records throughput, allocation, fanout, and clean/dirty scaling on net9 default EntityId.

**Tech Stack:** BenchmarkDotNet, NUnit, GitHub Actions, Markdown, .NET 9.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Depends on all previous plans and the final public API.
- Benchmark project targets net9.0 default uint mode; ulong correctness remains CI-tested, not separately benchmarked initially.
- No Stopwatch millisecond thresholds in NUnit/CI.
- NuGet packages only the default uint Kernel build.
- User docs are English and Simplified Chinese plus a v1→v2 migration guide.

## Review Focus

- Benchmark methods must consume results so JIT cannot remove work; Task 1 uses Consumer/return values.
- Collector benchmarks must separate first central pump from subsequent per-Collector Flush; Task 2 has distinct cases.
- “1000 collectors” must distinguish relevant and unrelated fanout; Task 2 measures both.
- Package inspection must prove public assembly/package identity remains CoreECS and no ulong symbol is set; Task 3 inspects the nupkg.
- Docs must not claim EnTT parity or unsupported features; Task 4 uses source-link/API grep checks.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`
- Benchmark smoke: `dotnet run -c Release --project Benchmark/Benchmark.csproj -- --filter '*Smoke*'`

---

### Task 1: Create BenchmarkDotNet project and storage/query suites

**Files:**
- Create: `Benchmark/Benchmark.csproj`
- Create: `Benchmark/Program.cs`
- Create: `Benchmark/EntityLifecycleBenchmarks.cs`
- Create: `Benchmark/ComponentStorageBenchmarks.cs`
- Create: `Benchmark/QueryBenchmarks.cs`
- Modify: `CoreECS.sln`

**Interfaces:**
- Produces runnable benchmark categories for Entity create/destroy/EndTick; component Add/Get/RO/RW/Remove, cached refs, page growth/compact; Query common/rare All, Any, None, empty.

- [ ] **Step 1: Add project and smoke benchmark**

Run: `dotnet run -c Release --project Benchmark/Benchmark.csproj -- --filter '*Smoke*'`

Expected initially: project missing, then one benchmark executes after scaffold.

- [ ] **Step 2: Add parameterized entity/storage cases**

Use representative counts (small, 10k, 100k where runtime permits) and MemoryDiagnoser. Separate setup allocation from operation measurement.

- [ ] **Step 3: Add Query selectivity cases**

Build worlds where rare required term has 0.1%/1% prevalence and common term 90%; return result Count. Include internal diagnostics in a non-benchmark correctness test proving candidate pool selection.

- [ ] **Step 4: Run benchmark smoke and full tests**

- [ ] **Step 5: Commit**

```bash
git add Benchmark CoreECS.sln Test
git commit -m "chore(proj): add storage and query benchmarks"
```

### Task 2: Add Collector and DAG benchmark suites

**Files:**
- Create: `Benchmark/CollectorBenchmarks.cs`
- Create: `Benchmark/SystemDagBenchmarks.cs`
- Modify: `Test/StressTestUnit.cs`

**Interfaces:**
- Produces Collector 0/1/100/1000 RW and Flush cases; repeated RW coalescing; first pump vs subsequent Flush; relevant vs unrelated fanout; DAG clean vs dirty BeginTick.

- [ ] **Step 1: Remove obsolete Stopwatch assertions from Stress tests**

Retain stress behavior tests but remove fixed elapsed-millisecond gates. Internal diagnostic tests continue to assert O(1)/fanout counts.

- [ ] **Step 2: Add Collector benchmark fixtures**

Use separate benchmark methods for write only, write+first Flush/pump, second Collector Flush without new journal, and interleaved pump epochs. Consume Changed counts.

- [ ] **Step 3: Add DAG benchmark fixtures**

Parameterize systems/groups/edges. Measure clean BeginTick, one dirty edge change, and full graph rebuild; verify order outside timed region.

- [ ] **Step 4: Run smoke filters and full tests**

- [ ] **Step 5: Commit**

```bash
git add Benchmark Test/StressTestUnit.cs
git commit -m "chore(proj): add collector and DAG benchmarks"
```

### Task 3: Finalize CI and NuGet packaging

**Files:**
- Modify: `.github/workflows/pr-build-test.yml`
- Modify: `.github/workflows/build-test-deployed.yml`
- Modify: `Kernel/Kernel.csproj`
- Modify: `AGENTS.md`

**Interfaces:**
- Produces PR matrix for default/ulong tests, dual-target Kernel build, default-only package/publish, and documented local commands.

- [ ] **Step 1: Add local package identity test**

Pack default Kernel; inspect nupkg entries and nuspec for CoreECS package/assembly, net9.0 + netstandard2.1 assets, README, symbols, and absence of a second ulong package.

- [ ] **Step 2: Update workflows**

PR workflow restores once per matrix job and tests both entity modes. Publish workflow builds/tests default mode, packs `Kernel/Kernel.csproj`, and pushes `Kernel/bin/Release/*.nupkg`; use current action major versions and .NET 9.

- [ ] **Step 3: Verify workflow paths with grep and local commands**

No `ECS/ECS.csproj`, .NET 8 setup, or Analyzer project references remain.

- [ ] **Step 4: Commit**

```bash
git add .github Kernel/Kernel.csproj AGENTS.md
git commit -m "ci: validate and package CoreECS v2"
```

### Task 4: Rewrite bilingual docs and migration guide

**Files:**
- Rewrite: `README.md`
- Rewrite: `README.zh-CN.md`
- Rewrite: `docs/QUICK_START.md`
- Rewrite: `docs/QUICK_START.zh-CN.md`
- Create: `docs/MIGRATING_V1_TO_V2.md`
- Modify: `Kernel/Kernel.csproj` to keep `README.md` as the packed package readme after the rewrite

**Interfaces:**
- Produces user-facing documentation matching the final public API.

- [ ] **Step 1: Write migration guide from the spec breaking-change list**

Include EntityId struct, unique components, mandatory data Hooks, ITagComponent behavior, concrete Query/Collector, removed APIs/events/MinimalWorld, immutable mask, World restart stale-object rule, and code before/after examples.

- [ ] **Step 2: Rewrite Quick Starts with compilable samples**

Samples cover component and Tag definitions, World lifecycle, Entity operations, Query snapshots, Collector Flush, DAG registration/anchors, restart, and `COREECS_ULONG_ENTITY` source build note.

- [ ] **Step 3: Rewrite READMEs around state-first positioning**

Do not claim archetype storage, CommandBuffer, analyzer, parallelism, or EnTT-relative speed. Explain typed paged sparse-set and central-pump Collector in user terms.

- [ ] **Step 4: Compile documentation samples**

Create a temporary net9 console referencing Kernel, paste/run each primary sample, and delete the temporary files afterward. Build Kernel netstandard target separately.

- [ ] **Step 5: Run link/path/API grep audit**

No stale ECS path, removed API, old ulong EntityId signature, or unsupported feature claim remains.

- [ ] **Step 6: Commit**

```bash
git add README.md README.zh-CN.md docs Kernel/Kernel.csproj
git commit -m "doc(proj): document CoreECS v2"
```

### Task 5: Final branch verification

**Files:**
- No production changes expected; fix only verified defects in focused commits.

**Interfaces:**
- Consumes all v2 slices.
- Produces release-candidate evidence.

- [ ] **Step 1: Run default release verification**

Run: `dotnet restore CoreECS.sln`

Run: `dotnet build CoreECS.sln --configuration Release --no-restore`

Run: `dotnet test CoreECS.sln --configuration Release --no-build --verbosity normal`

- [ ] **Step 2: Run ulong release verification**

Run the same build/test commands with `-p:CoreECSUseUlongEntity=true`; do not pack.

- [ ] **Step 3: Run package and benchmark smoke**

Pack default Kernel and run one benchmark from each fixture.

- [ ] **Step 4: Audit repository state**

No `.idea`, `obj`, BenchmarkDotNet artifacts, temp projects, stale ECS paths, or unexpected uncommitted files.

- [ ] **Step 5: Record verification result**

Append exact commands, SDK, test totals, package path, and benchmark smoke list to the implementation handoff or release notes; commit only if a tracked handoff document is created.
