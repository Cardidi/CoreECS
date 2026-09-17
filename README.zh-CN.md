<div align="center">

# CoreECS

**面向 C# 游戏的 state-first 实体–组件–系统（ECS）工具包**

轻量级 ECS，可与 Unity ECS 或其他方案并存 —— 基于 **ComponentStore**、**EntityGraph** 与**结构性变更收集器（Collector）** 构建。

**[English](README.md)** · **简体中文**

[快速入门](docs/QUICK_START.zh-CN.md) · [许可证](LICENSE) · [NuGet](https://www.nuget.org/packages/CoreECS)

</div>

---

## 特性

| 领域 | 亮点 |
|------|------|
| **架构** | `ComponentStore` + `EntityGraph`，灵活且紧凑的组件存储 |
| **State-first** | `EntityCollector` 提供 `Flush()` 与 `Matching` / `Clashing` / `Changed` 缓冲区 |
| **查询** | 流式 `EntityMatcher`（`OfAll`、`OfAny`、`OfNone`、掩码过滤） |
| **组件** | `RO` / `RW` 引用，可选 `OnCreate` / `OnDestroy`，扩展方法 |
| **系统** | 按注册顺序执行，`TickGroup` 掩码，`IInjectionProxy` 构造函数注入 |
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
| **Component（组件）** | 数据结构（`IComponent<T>`），逻辑放在系统中 |
| **System（系统）** | `ISystem` —— `OnCreate` / `OnTick` / `OnDestroy` |
| **World（世界）** | 管理生命周期、实体、组件、系统、收集器 |
| **Matcher（匹配器）** | `EntityMatcher` 按组件与实体掩码筛选 |
| **Collector（收集器）** | 跟踪匹配结果；缓冲区在 `Flush()` 后生效 |
| **InjectionProxy** | 通过 `OnRegister` 为系统构造函数提供 DI |
| **Tick（帧/步）** | `BeginTick` → `Tick(mask)` → `EndTick` |
| **Mask（掩码）** | 实体/系统上的位标志，用于分步 Tick 与查询过滤 |

---

## 文档

| 文档 | 说明 |
|------|------|
| [**快速入门指南（中文）**](docs/QUICK_START.zh-CN.md) | 完整教程：11 节，从 World 到可运行示例 |
| [**Quick Start Guide (English)**](docs/QUICK_START.md) | English tutorial |
| [**AGENTS.md**](AGENTS.md) | 构建命令与 Agent/CI 贡献说明（英文） |

---

## 项目结构

```
CoreECS/
├── ECS/                    # CoreECS 库（net8.0 + netstandard2.1）
├── Test/                   # NUnit 测试
├── docs/                   # 指南（快速入门等）
├── README.md               # 英文说明
├── README.zh-CN.md         # 中文说明（本文件）
└── ...
```

---

## 许可证

MIT —— 详见 [LICENSE](LICENSE)。
