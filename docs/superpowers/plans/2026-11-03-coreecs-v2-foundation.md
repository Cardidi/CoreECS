# CoreECS v2 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the Kernel rename, establish the unpinned net8/C# 9 build matrix, and add the public EntityId value type in uint and ulong modes without changing component storage yet.

**Architecture:** Treat the Rider rename as user-owned input and complete only its missing references. Add EntityId as an unused foundational type first so its layout and dual-mode behavior can be proven before managers migrate from ulong IDs.

**Tech Stack:** C# 9, `net8.0;netstandard2.1`, NUnit, GitHub Actions; no pinned SDK version.

**Spec:** `docs/superpowers/specs/2026-11-03-coreecs-v2-simplified-design.md`

## Global Constraints

- Preserve assembly/root namespace/package ID `CoreECS`.
- Kernel targets `net8.0;netstandard2.1`; Test targets net8.0.
- Use `COREECS_ULONG_ENTITY` only as a Kernel/Test compile symbol; NuGet packs default uint only.
- EntityId raw layout is uint 20-bit index + 12-bit generation, or ulong 32 + 32.
- Default raw value 0 is invalid; live indexes and generations begin at 1; generation wraps while skipping 0.
- Do not alter product behavior beyond project/build paths and introducing the unused EntityId type.

## Review Focus

- Boundary raw values at the maximum index/generation must round-trip without corrupting adjacent bits; Task 2 tests both maxima.
- Generation wrap must skip 0 in both compile modes; Task 2 tests max→1.
- Default EntityId must never compare valid; Task 2 tests default and explicit zero.
- Rider rename must not change assembly/package identity; Task 1 inspects built metadata.
- The ulong source mode must not accidentally be included in NuGet; Task 3 inspects pack inputs and CI commands.

## Standard Verification

- Default full suite: `dotnet test CoreECS.sln --configuration Release`
- Ulong full suite: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`
- Kernel dual-target build: `dotnet build Kernel/Kernel.csproj --configuration Release`

---

### Task 1: Finish the Rider Kernel rename and upgrade the default build

**Files:**
- Modify: `Kernel/Kernel.csproj`
- Modify: `Test/Test.csproj`
- Modify: `CoreECS.sln`
- Delete: `global.json`
- Modify: `.github/workflows/pr-build-test.yml`
- Modify: `.github/workflows/build-test-deployed.yml`
- Modify: `AGENTS.md`
- Modify: `README.md`
- Modify: `README.zh-CN.md`

**Interfaces:**
- Consumes: the user's staged/unstaged Rider rename.
- Produces: a buildable `Kernel/Kernel.csproj` with AssemblyName/RootNamespace/PackageId `CoreECS`, target frameworks `net8.0;netstandard2.1`, C# 9, and no SDK pin. Kernel Nullable remains temporarily disabled until Plan 7 removes legacy code and enables it cleanly.

- [ ] **Step 1: Record the pre-change failure and stale references**

Run: `dotnet build CoreECS.sln --configuration Release`

Expected before deleting `global.json`: FAIL in environments without the exact pinned SDK 8.0.0 even when a compatible newer SDK can build net8.0.

Run: `rg 'ECS[/\\]|ECS\.csproj|net9\.0|dotnet-version: .9|global\.json' AGENTS.md README.md README.zh-CN.md .github Kernel Test CoreECS.sln global.json`

Expected: stale build/doc paths and .NET 8 configuration are reported.

- [ ] **Step 2: Normalize project configuration**

Set exact values:

- Delete `global.json`; AGENTS no longer claims a pinned or mandatory SDK version.
- Kernel: `TargetFrameworks=net8.0;netstandard2.1`, `LangVersion=9`, keep `Nullable=disable` temporarily, one PackageId entry, newline at EOF. Plan 7 owns the final Nullable enable after legacy migration bridges are gone.
- Test: `TargetFramework=net8.0` and existing Nullable enabled.
- workflows: install .NET 8 as the CI implementation environment, use `Kernel/Kernel.csproj` for pack/push, retain solution build/test; this CI choice is not a library SDK contract.
- docs/AGENTS layout references: `Kernel/Kernel.csproj`.

Do not add Benchmark yet; Plan 8 owns it.

- [ ] **Step 3: Verify rename identity and builds**

Run: `dotnet restore CoreECS.sln`

Run: `dotnet build Kernel/Kernel.csproj --configuration Release`

Run: `dotnet test Test/Test.csproj --configuration Release --no-restore`

Expected: PASS; built assembly remains `CoreECS.dll`; no source references to `ECS/ECS.csproj` remain.

- [ ] **Step 4: Commit the rename completion**

```bash
git add CoreECS.sln Kernel Test global.json .github AGENTS.md README.md README.zh-CN.md ECS/ECS.csproj
git commit -m "refactor(proj): rename library project to Kernel"
```

Exclude `.idea` and `obj` files.

### Task 2: Add EntityId and its packed traits

**Files:**
- Create: `Kernel/EntityId.cs`
- Create: `Test/EntityIdTestUnit.cs`

**Interfaces:**
- Consumes: C# 9 dual-target Kernel.
- Produces: `public readonly struct EntityId : IEquatable<EntityId>`; internal construction/decomposition helpers used by Plan 2 EntityTable.

Define conditionally:

```csharp
#if COREECS_ULONG_ENTITY
using EntityValue = System.UInt64;
#else
using EntityValue = System.UInt32;
#endif
```

Required public surface:

- conditional `Index` and `Generation` properties of `EntityValue` type;
- `bool IsValid`;
- equality, hash, `==`, `!=`, useful `ToString()`.

Required internal surface:

- `EntityValue Raw`;
- `static EntityId Create(EntityValue index, EntityValue generation)`;
- max index/generation constants;
- `static EntityValue NextGeneration(EntityValue current)` skipping zero.

- [ ] **Step 1: Write failing EntityId tests**

Add tests named:

- `EntityId_Default_IsInvalid`
- `EntityId_Create_RoundTripsIndexAndGeneration`
- `EntityId_MaxValues_RoundTripWithoutOverlap`
- `EntityId_Equality_UsesIndexAndGeneration`
- `EntityId_NextGeneration_SkipsZeroAfterWrap`
- `EntityId_ToString_ContainsIndexAndGeneration`

Assertions use compile-conditional expected maxima: `(1<<20)-1` / `(1<<12)-1`, or `uint.MaxValue` / `uint.MaxValue` in ulong mode.

- [ ] **Step 2: Run the focused test and confirm red**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityIdTestUnit`

Expected: FAIL because EntityId does not exist.

- [ ] **Step 3: Implement EntityId**

Use masks and shifts fixed by the spec. Reject index 0, generation 0, or values above masks in `Create` with `ArgumentOutOfRangeException`; default construction remains the only invalid zero value.

- [ ] **Step 4: Verify default-mode tests**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityIdTestUnit`

Expected: all EntityId tests PASS.

- [ ] **Step 5: Verify ulong-mode tests**

Run: `dotnet test Test/Test.csproj -p:DefineConstants=COREECS_ULONG_ENTITY --filter FullyQualifiedName~EntityIdTestUnit`

Expected: the same tests PASS with ulong maxima.

- [ ] **Step 6: Commit EntityId**

```bash
git add Kernel/EntityId.cs Test/EntityIdTestUnit.cs
git commit -m "feat(core): add versioned EntityId"
```

### Task 3: Add repeatable dual-mode verification commands

**Files:**
- Modify: `.github/workflows/pr-build-test.yml`
- Modify: `Test/Test.csproj`
- Test: `Test/EntityIdTestUnit.cs`

**Interfaces:**
- Consumes: EntityId and default Kernel project.
- Produces: CI jobs that test default uint and source-only ulong modes without packing ulong NuGet artifacts.

- [ ] **Step 1: Add a CI matrix test that initially exposes symbol propagation gaps**

Matrix values: `entityMode: [uint, ulong]`. For ulong pass the same `DefineConstants=COREECS_ULONG_ENTITY` to both Kernel and Test builds; for uint pass no custom symbol.

Expected initial matrix run/local equivalent: FAIL if Test and referenced Kernel compile with different symbols.

- [ ] **Step 2: Make symbol propagation explicit**

Use an MSBuild property such as `CoreECSUseUlongEntity=true` to append `COREECS_ULONG_ENTITY` inside both project files; do not overwrite existing constants. Publish workflow never sets the property.

- [ ] **Step 3: Run both complete test modes**

Run: `dotnet test CoreECS.sln --configuration Release`

Run: `dotnet test CoreECS.sln --configuration Release -p:CoreECSUseUlongEntity=true`

Expected: both PASS.

- [ ] **Step 4: Pack and inspect default NuGet only**

Run: `dotnet pack Kernel/Kernel.csproj --configuration Release --no-restore`

Expected: package produced without `CoreECSUseUlongEntity`; workflow contains no ulong pack step.

- [ ] **Step 5: Commit dual-mode CI**

```bash
git add .github/workflows/pr-build-test.yml Kernel/Kernel.csproj Test/Test.csproj
git commit -m "ci: test uint and ulong EntityId modes"
```
