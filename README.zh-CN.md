<div align="center">

# CoreECS

**面向 C# 游戏的 state-first 实体–组件–系统（ECS）工具包**

轻量级 ECS，可与 Unity ECS 或其他方案并存 —— 基于 **archetype 存储**、**结构性变更收集器（Collector）** 与显式的 **CommandBuffer** 构建。

**[English](README.md)** · **简体中文**

[快速入门](docs/QUICK_START.zh-CN.md) · [许可证](LICENSE) · [NuGet](https://www.nuget.org/packages/CoreECS)

</div>

---

## 特性

| 领域 | 亮点 |
|------|------|
| **架构** | Archetype `Structure` 存储：行对齐 dense SoA 数组、sparse 组件存储、tag 位图 |
| **State-first** | `EntityCollector` 提供 `Flush()` 与 `Matching` / `Clashing` / `Changed` 缓冲区 |
| **查询** | 流式 `EntityMatcher`、非池化 `IEntityQuery`、批量 `s.RO<T>()` / `s.RW<T>()` Span |
| **组件** | Dense / Sparse / Tag 三类，`RO` / `RW` 引用，可选 `OnCreate` / `OnDestroy` |
| **系统** | 可嵌套分组与 `Before` / `After` 排序、`TickGroup` 掩码、`IInjectionProxy` 构造函数注入 |
| **CommandBuffer** | 记录结构性变更，一次显式 `Playback()` 批量应用 |
| **目标框架** | `net8.0` 与 `netstandard2.1` |

---

## 安装

```bash
dotnet add package CoreECS
```

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)（SDK 版本见仓库根目录 `global.json`）。

```bash
git clone https://github.com/Cardidi/CoreECS.git
cd CoreECS
dotnet build
dotnet test
```

**初次使用？** 请阅读 [**快速入门指南**](docs/QUICK_START.zh-CN.md) —— 涵盖 World 生命周期、组件、系统、匹配器、收集器与完整示例。

---

## 一览

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

收集器、标志位、匹配器与 DI 配置详见 [docs/QUICK_START.zh-CN.md](docs/QUICK_START.zh-CN.md)。

---

## 为什么是工具包，而不是框架？

卡牌、RPG、多人、独立原型等不同类型与规模的游戏，常会反复遇到相似的逻辑组织方式。单一「大一统」框架容易让设计变成「框架是否支持 X」，而不是「游戏本身需要什么」。

CoreECS 源于一款回合制卡牌项目：需要**可预测的状态**与**变更追踪**，同时用 Unity ECS 承担高吞吐模拟。Unity ECS 性能出色，但在偏状态驱动的流程上较僵硬；CoreECS 以**松耦合工具包**的形式填补这一空缺 —— 可作为模拟辅助、状态守门人或独立 ECS 循环，按项目需要组合即可。

---

## 核心概念

| 概念 | 作用 |
|------|------|
| **Entity（实体）** | 稳定 id，聚合组件（对外推荐 `Entity` 结构体，底层为 `ulong`） |
| **Component（组件）** | 数据结构（dense：`IComponent<T>`；sparse：`ISparseComponent<T>`；tag：`ITagComponent<T>`），逻辑放在系统中 |
| **System（系统）** | `ISystem` —— `OnCreate` / `OnTick` / `OnDestroy` |
| **World（世界）** | 生命周期（`OnRegister` / `OnSetup` / `OnCleanup`）、实体、组件、系统、收集器 |
| **Matcher（匹配器）** | `EntityMatcher` 按组件与实体掩码筛选 |
| **Collector（收集器）** | 跟踪匹配结果；缓冲区在 `Flush()` 后生效 |
| **Structure（结构）** | Archetype：相同 dense 组成 + 掩码的实体共享行对齐存储 |
| **Query（查询）** | `IEntityQuery` 匹配实体/结构快照（`Refresh()` 重建） |
| **Group（分组）** | 系统的命名排序桶；`Before` / `After` 锚点 |
| **CommandBuffer** | 记录 create / destroy / SetMask 命令；`Playback()` 按序应用 |
| **InjectionProxy** | 通过 `OnRegister` 为系统构造函数提供 DI |
| **Tick（帧/步）** | `BeginTick` → `Tick(mask)` → `EndTick` |
| **Mask（掩码）** | 实体/系统上的位标志，用于分步 Tick 与查询过滤 |

---

## 文档

| 文档 | 说明 |
|------|------|
| [**快速入门指南（中文）**](docs/QUICK_START.zh-CN.md) | 完整教程（中文）：World 搭建、组件、查询、系统、收集器、CommandBuffer 与破坏性变更 |
| [**Quick Start Guide (English)**](docs/QUICK_START.md) | English tutorial |
| [**AGENTS.md**](AGENTS.md) | 构建命令与 Agent/CI 贡献说明（英文） |

---

## 项目结构

```
CoreECS/
├── Kernel/                    # CoreECS 库（net8.0 + netstandard2.1）
├── Test/                   # NUnit 测试
├── docs/                   # 指南（快速入门等）
├── README.md               # 英文说明
├── README.zh-CN.md         # 中文说明（本文件）
└── ...
```

---

## 许可证

MIT —— 详见 [LICENSE](LICENSE)。
