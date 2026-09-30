# AGENTS.md

## Repository overview

CoreECS is a pure C# Entity-Component-System toolkit for games. It has no server, database, frontend, or Docker runtime. Storage is archetype-based, with dense, sparse, and tag components; the public surface also includes entity queries, change collectors, ordered systems, dependency injection, and an explicit `CommandBuffer`.

`CoreECS.sln` contains three projects:

| Project | Path | Purpose | Target |
| --- | --- | --- | --- |
| `Kernel` | `Kernel/Kernel.csproj` | The `CoreECS` library and NuGet package | `net8.0`; `netstandard2.1` |
| `Analyzers` | `Analyzers/Analyzers.csproj` | Roslyn analyzers for ref/span invalidation (`ECS0001`, `ECS0002`) | `netstandard2.0` |
| `Test` | `Test/Test.csproj` | NUnit tests for the library and analyzers | `net8.0` |

## Important paths

- `Kernel/`: public API and implementation.
  - Root files contain core public types such as `World`, `Entity`, `ComponentRef`, `EntityMatcher`, `EntityQuery`, and `CommandBuffer`.
  - `Kernel/Defines/`: interfaces, component contracts, and enums.
  - `Kernel/Managers/`: entity, component, matcher, and system management/scheduling.
  - `Kernel/Structures/`: archetype storage, component containers, registries, locations, and handlers.
  - `Kernel/Utils/`: pooling, DI, logging, signals, assertions, and dispatch guards.
- `Analyzers/`: analyzer implementation and release tracking. Read `Analyzers/README.md` before changing analyzer behavior.
- `Test/`: NUnit unit, integration, stress, performance, and analyzer tests. Tests generally use the `*TestUnit.cs` naming convention.
- `docs/QUICK_START.md` and `docs/QUICK_START.zh-CN.md`: user-facing API guides.
- `docs/superpowers/specs/` and `docs/superpowers/plans/`: dated design records and implementation plans; useful context, but verify claims against current code.
- `README.md` and `README.zh-CN.md`: English and Chinese project overviews.

## Toolchain and project settings

- Use the .NET 8 SDK. `global.json` requests `8.0.0`, rolls forward to the latest minor SDK, and disallows prereleases.
- `Kernel` uses C# 12, disables nullable reference analysis and implicit usings, and enables unsafe blocks.
- `Analyzers` uses C# 12, enables nullable reference analysis, disables implicit usings, and is a non-packable Roslyn component.
- `Test` enables nullable reference analysis and implicit usings. It suppresses `NU1701` and `CS0618`.
- Do not normalize these settings across projects; their differences are intentional.
- No repository-wide formatter or lint command is configured. Treat compiler and analyzer warnings as the primary static checks and follow the surrounding file style.

## Build and test

Run commands from the repository root:

```bash
dotnet restore CoreECS.sln
dotnet build CoreECS.sln
dotnet test CoreECS.sln --verbosity normal
dotnet build CoreECS.sln --configuration Release
dotnet test CoreECS.sln --configuration Release --no-build --verbosity normal
```

Useful targeted commands:

```bash
dotnet build Kernel/Kernel.csproj
dotnet build Analyzers/Analyzers.csproj
dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityQueryTestUnit
dotnet test Test/Test.csproj --filter TestCategory=Performance
```

The PR workflow restores, builds, and tests the full solution in Release mode on .NET 8. Before finishing a code change, prefer the equivalent Release build and test commands. For a narrow iteration, run the affected fixture first, then the full suite.

## Change guidelines

- Preserve multi-target compatibility in `Kernel`: library code must compile for both `net8.0` and `netstandard2.1`.
- Structural changes can invalidate raw refs and spans into archetype storage. Reacquire `ComponentRef<T>.RO`/`.RW` values and dense-column spans after entity/component/mask changes or `CommandBuffer.Playback()`. `ComponentRef<T>` handles themselves are designed to be re-resolved.
- Changes to structural-change APIs, raw-ref sources, or analyzer propagation may require coordinated updates in `Analyzers/StructuralChangeApis.cs`, analyzer tests, release notes, and `Analyzers/README.md`.
- Keep tests alongside the existing fixture that owns the behavior; create a new `*TestUnit.cs` only when it represents a distinct subsystem.
- Performance tests have deliberately generous CI bounds but still serve as regression gates. Do not weaken or rewrite their baselines without explaining the measured reason.
- Public API or behavior changes should be reflected in the relevant English and Chinese documentation when applicable.
- Do not treat dated design documents as authoritative over implementation and tests.

## NuGet and CI notes

- `Kernel/Kernel.csproj` defines the `CoreECS` package metadata and targets both supported library frameworks.
- `.github/workflows/pr-build-test.yml` is the reliable validation reference for pull requests.
- `.github/workflows/build-test-deployed.yml` still contains legacy `ECS/ECS.csproj` publish paths. Do not copy those paths into new commands; the current library project is `Kernel/Kernel.csproj`.

## Commit messages

Repository history predominantly follows Conventional Commits:

```text
<type>(<scope>): <imperative description>
```

Common types are `feat`, `fix`, `refactor`, `test`, `doc`, `ci`, and `chore`. Common scopes are `core`, `test`, `proj`, `extension`, `utils`, `nuget`, and `github_actions`; `ci` commits may omit a scope. Keep the subject concise and add a body when the reason or behavioral consequence is not obvious.

Examples from repository history:

- `feat(core): add event dispatch reentrancy guard`
- `fix(core): match ComponentRef by its new CoreECS namespace in analyzer`
- `test(test): record v3 event migration performance gate`
- `doc(proj): add entity/component helper extensions design`
- `ci: add GitHub Actions workflow for PR build and test`
