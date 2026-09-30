# CoreECS

**面向 C# 游戏的轻量级实体组件系统（ECS）。**

[English](README.md) · 简体中文

[快速入门](docs/QUICK_START.zh-CN.md) · [NuGet](https://www.nuget.org/packages/CoreECS) · [许可证](LICENSE)

CoreECS 是一个面向 C# 游戏的轻量级 ECS 库。组件是普通结构体，按 archetype 存储——组件构成相同的实体排布在一起，连续内存、遍历高效——结构变更也始终是显式的，不会在背后悄悄发生。

按需选择三种方式处理实体：

- **查询快照**：看一个稳定的世界视图。
- **收集器**：告诉你谁进入、谁离开、谁的数据变了。
- **命令缓冲区**：把结构操作攒起来，准备好了一次应用。

它是库而不是引擎：游戏循环、生命周期和与自家引擎的对接，都由你自己掌握。

## 特性

- **三种组件布局：** archetype 中的 dense 列、按实体存放的 sparse 数据，以及只表示存在与否的 tag。
- **两种实体筛选方式：** 可刷新的 `IEntityQuery` 快照，以及事件驱动的 `IEntityCollector` 缓冲区。
- **直接数据访问：** 类型化 `ComponentRef<T>` 句柄，以及按 dense 列批量取出的 `ReadOnlySpan<T>` / `Span<T>` 视图。
- **显式批处理结构变更：** `CommandBuffer` 按记录顺序回放实体、组件与 mask 操作。
- **确定性的系统调度：** 分组、嵌套分组、`Before` / `After` 约束和 tick mask。
- **内置构造函数注入：** 默认基于 `Microsoft.Extensions.DependencyInjection`，也可替换注入容器。
- **ref 安全分析器：** `ECS0001` 与 `ECS0002` 检查跨结构变更继续使用裸 ref/span 的问题。
- **多目标框架：** `net8.0` 与 `netstandard2.1`。

## 安装

```bash
dotnet add package CoreECS
```

构建本仓库需要 .NET 8 SDK。

## 第一个 World

```csharp
using CoreECS;
using CoreECS.Defines;

public struct Position : IComponent<Position>
{
    public float X;
    public float Y;
}

public struct Velocity : IComponent<Velocity>
{
    public float X;
    public float Y;
}

var world = new World();
world.Startup();

var entity = world.CreateEntity();
entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });

var matcher = EntityMatcher.With.OfAll<Position, Velocity>();
using var query = world.CreateQuery(matcher);
query.Refresh();

foreach (var entityId in query.Entities)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}

world.Shutdown();
```

关键生命周期顺序：

```text
Startup → 创建/注册/使用 → BeginTick → Tick → EndTick → Shutdown
```

`World` 不是线程安全的：它返回的一切都只能在创建它的线程上使用。

## 它是怎么工作的

### 组件

所有组件都是实现 `IComponent<T>` 的结构体。

- `IComponent<T>`：存放于 archetype 列中的 dense 数据；增删会迁移实体。
- `ISparseComponent<T>`：存放于 dense 列之外的数据；不决定 archetype 归属。
- `ITagComponent<T>`：只表示是否存在，不携带数据，也不返回可用的 `ComponentRef<T>`。

实体 mask 同样参与 archetype 标识，因此 `Entity.SetMask` 可能迁移实体，但会保留其组件。

### 查询与收集器

需要查看稳定快照时使用 `IEntityQuery`：

```csharp
using var query = world.CreateQuery(EntityMatcher.With.OfAll<Position>());
query.Refresh();

foreach (var entityId in query.Entities)
{
    // 下次 Refresh() 前，快照内容保持不变。
}
```

需要按阶段处理成员关系与数据变化时使用 `IEntityCollector`：

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>());

collector.Flush();

foreach (var entityId in collector.Changed)
{
    // 处理本次 Flush() 发布的变化。
}
```

### Ref 安全

`ComponentRef<T>` 是可重新定位的句柄，跨结构变更依然有效；但 `.RO` / `.RW` 给出的裸 ref 和 `Structure` 给出的 span 直接指向内部存储，存储一动它们就失效。创建或销毁实体/组件、修改 mask、调用 `CommandBuffer.Playback()` 之前，先把它们用完或丢弃，之后再重新获取。

分析器安装方式与 `ECS0001` / `ECS0002` 完整规则见 [`Analyzers/README.md`](Analyzers/README.md)。

## 下一步

[快速入门](docs/QUICK_START.zh-CN.md) 包含：

1. World 生命周期与依赖注入；
2. dense、sparse 与 tag 组件；
3. 实体和组件操作；
4. matcher、query 与批量访问；
5. collector 与变更标志；
6. system、group 与 tick mask；
7. command buffer 与 ref 安全规则。

英文文档：[README.md](README.md) 和 [docs/QUICK_START.md](docs/QUICK_START.md)。

## 从源码构建

```bash
git clone https://github.com/Cardidi/CoreECS.git
cd CoreECS
dotnet restore CoreECS.sln
dotnet build CoreECS.sln --configuration Release
dotnet test CoreECS.sln --configuration Release --no-build
```

仓库结构：

```text
Kernel/       CoreECS 库
Analyzers/    Roslyn ref 安全分析器
Test/         NUnit 库与分析器测试
docs/         指南、设计与实施计划
```

## 许可证

[MIT](LICENSE)
