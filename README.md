<div align="center">

# CoreECS

**State-first Entity–Component–System toolkit for C# games**

**English** · [简体中文](README.zh-CN.md)

Lightweight ECS you can embed beside Unity ECS or other stacks — built around **archetype storage**, **structural-change collectors**, and an explicit **CommandBuffer**.

[Quick Start Guide](docs/QUICK_START.md) · [快速入门（中文）](docs/QUICK_START.zh-CN.md) · [License](LICENSE) · [NuGet](https://www.nuget.org/packages/CoreECS)

</div>

---

## Features

| Area | Highlights |
|------|------------|
| **Architecture** | Archetype `Structure` storage: row-aligned dense SoA arrays, discrete component stores, tag bitmaps |
| **State-first** | `EntityCollector` with `Flush()`, `Matching` / `Clashing` / `Changed` buffers |
| **Queries** | Fluent `EntityMatcher`, non-pooled `IEntityQuery`, batch `s.RO<T>()` / `s.RW<T>()` spans |
| **Components** | Dense / discrete / tag kinds, `RO` / `RW` refs, optional `OnCreate` / `OnDestroy` |
| **Systems** | Nested groups with `Before` / `After` ordering, `TickGroup` masks, constructor DI via `IInjectionProxy` |
| **CommandBuffer** | Record structural changes and apply them in one explicit `Playback()` |
| **Targets** | `net8.0` and `netstandard2.1` |

---

## Installation

```bash
dotnet add package CoreECS
```

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) (see `global.json` for SDK pinning).

```bash
git clone https://github.com/Cardidi/CoreECS.git
cd CoreECS
dotnet build
dotnet test
```

**New to the API?** Follow the [**Quick Start Guide**](docs/QUICK_START.md) — world lifecycle, components, systems, matchers, collectors, and a full sample.

---

## At a Glance

```csharp
using CoreECS;

var world = new World();
world.Startup();

world.RegisterSystem<MovementSystem>();

var entity = world.CreateEntity();
entity.CreateComponent(new PositionComponent { X = 0, Y = 0 });
entity.CreateComponent(new VelocityComponent { X = 10, Y = 5 });

using var cmd = world.CreateCommandBuffer();
var spawned = cmd.CreateEntity();
cmd.CreateComponent(spawned, new PositionComponent { X = 1, Y = 2 });
cmd.Playback();

world.BeginTick();
world.Tick();
world.EndTick();

world.Shutdown();
```

See [docs/QUICK_START.md](docs/QUICK_START.md) for collectors, flags, matchers, and DI setup.

---

## Why a Toolkit, Not a Framework?

Game logic often repeats across genres and budgets — card games, RPGs, multiplayer, indie prototypes. A monolithic framework can steer design toward “does the framework support X?” instead of “what does the game need?”

CoreECS grew from a turn-based card project that needed **predictable state** and **change tracking**, while Unity ECS handled high-throughput simulation. Unity ECS is strong on performance but rigid for state-driven workflows; CoreECS fills that gap as a **loosely coupled toolkit** you compose where it fits — simulation helper, state guardian, or standalone ECS loop.

---

## Key Concepts

| Concept | Role |
|---------|------|
| **Entity** | Stable id grouping components (`Entity` struct over `ulong`) |
| **Component** | Data structs (`IComponent<T>` dense, `IDiscreteComponent<T>`, `ITagComponent<T>`); logic lives in systems |
| **System** | `ISystem` — `OnCreate` / `OnTick` / `OnDestroy` |
| **World** | Lifecycle (`OnRegister` / `OnSetup` / `OnCleanup`), entities, components, systems, collectors |
| **Matcher** | `EntityMatcher` filters by components and entity mask |
| **Collector** | Tracks matcher matches; defers buffers until `Flush()` |
| **Structure** | Archetype: entities sharing dense composition + mask, stored row-aligned |
| **Query** | `IEntityQuery` snapshot over matching entities/structures (`Refresh()`) |
| **Group** | Named ordering bucket for systems; `Before` / `After` anchors |
| **CommandBuffer** | Records create/destroy/mask commands; `Playback()` applies them in order |
| **InjectionProxy** | DI for system constructors (`OnRegister`) |
| **Tick** | `BeginTick` → `Tick(mask)` → `EndTick` |
| **Mask** | Bit flags on entities/systems for filtered ticks and queries |

---

## Documentation

| Document | Description |
|----------|-------------|
| [**Quick Start Guide**](docs/QUICK_START.md) | Full tutorial (English): world setup, components, queries, systems, collectors, CommandBuffer, breaking changes |
| [**快速入门指南**](docs/QUICK_START.zh-CN.md) | 完整教程（中文） |
| [**README（中文）**](README.zh-CN.md) | 项目说明中文版 |
| [**AGENTS.md**](AGENTS.md) | Build commands and contributor notes for agents/CI |

---

## Project Layout

```
CoreECS/
├── Kernel/       # CoreECS library (net8.0 + netstandard2.1)
├── Test/         # NUnit tests
├── docs/              # Guides (Quick Start, …)
├── README.md          # English (this file)
└── README.zh-CN.md    # 简体中文
```

---

## License

MIT — see [LICENSE](LICENSE).
