# AGENTS.md

## Cursor Cloud specific instructions

### Project overview

CoreECS is a pure C# library (no servers, no databases, no Docker). It consists of two projects in `CoreECS.sln`:

| Project | Path | Purpose |
|---------|------|---------|
| `Kernel` | `Kernel/Kernel.csproj` | Library (targets `net8.0` + `netstandard2.1`) |
| `Test` | `Test/Test.csproj` | NUnit tests (targets `net8.0`) |

### Prerequisites

- **.NET 8 SDK** is required. The `global.json` pins `8.0.0` with `rollForward: latestMinor`.
- The update script installs the SDK to `$HOME/.dotnet` and adds it to PATH via `~/.bashrc`.

### Common commands

| Action | Command |
|--------|---------|
| Restore | `dotnet restore` |
| Build | `dotnet build` |
| Test | `dotnet test --verbosity normal` |
| Build Release | `dotnet build --configuration Release` |

### Gotchas

- The .NET SDK is installed to `$HOME/.dotnet`, not `/usr/share/dotnet`. The update script ensures `DOTNET_ROOT` and `PATH` are set in `~/.bashrc`.
- No lint tool (e.g. `dotnet format`) is configured in the solution. Build warnings serve as the primary code-quality check.
- The library uses `AllowUnsafeBlocks` and `LangVersion 9`; the test project uses `ImplicitUsings: enable` and `Nullable: enable`—do not unify these settings.

### Commit message format

This repository follows [Conventional Commits](https://www.conventionalcommits.org/) for almost all changes. Prefer this format over plain sentence subjects.

**Subject line (required):**

```
<type>(<scope>): <short description>
```

| Field | Rules |
|-------|--------|
| `type` | One of: `feat`, `fix`, `refactor`, `doc`, `ci`, `chore` |
| `scope` | Area of change; common values: `core`, `test`, `proj`, `extension`, `utils`, `nuget`, `github_actions` |
| Description | Imperative, concise summary of *what* changed (e.g. `add LazyChange flags`, `fix empty matcher relevance`) |

**Type usage (from project history):**

| Type | When to use |
|------|-------------|
| `feat` | New behavior or API; also test-only changes that cover new behavior (use scope `test`) |
| `fix` | Bug fix or incorrect behavior correction; also test fixes or corrected test expectations (use scope `test`) |
| `refactor` | Internal restructuring without intended behavior change; also test refactors with no intended behavior change (use scope `test`) |
| `doc` | Documentation or comments only (use scope `proj` or `core`, not `doc`) |
| `ci` | CI/CD workflows |
| `chore` | Maintenance, build tooling, packaging, or project files that do not fit the above |

**Body (optional):** Add a blank line after the subject, then one or more paragraphs when the *why* or non-obvious behavior is not clear from the subject alone. Example from history:

```
fix(core): treat empty matcher as relevant to all component types

When EntityMatcher.With has no all/any/none conditions, it matches
all entities. IsRelevantComponent must return true for any component
type in this case, otherwise ChangeComponent flag (part of Default)
would silently drop all Changed entries for unconditional matchers.
```

**Co-authored-by (optional):** For collaborative or agent-assisted commits, append after the body:

```
Co-authored-by: Name <email@example.com>
```

**Examples:**

- `feat(core): add ChangeComponent flag to track only match-relevant component changes`
- `fix(core): rename ChangeComponent flag to ChangeMustBeRelatedComponent`
- `feat(test): add unit tests for ChangeComponent filtering and LazyChange deduplication across flushes`
- `doc(proj): update README`
- `ci: add GitHub Actions workflow for PR build and test`
- `refactor(proj): rename to CoreECS (TinyECS has been occupied in NuGet)`
