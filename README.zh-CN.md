# CoreECS

**面向 C# 游戏的 state-first 实体–组件–系统工具包。**

[English](README.md) · 简体中文

[快速入门](docs/QUICK_START.zh-CN.md) · [迁移指南](docs/MIGRATING_V1_TO_V2.md) · [许可证](LICENSE) · [NuGet](https://www.nuget.org/packages/CoreECS)

CoreECS 使用按组件类型划分的分页 sparse-set 存储组件，提供稳定的组件句柄，并通过 Collector 在显式 Flush 点发布实体变化。它是库而不是引擎：游戏循环和与自家技术栈的对接由你掌握。

## 特性

- **typed 分页 sparse-set**：每种组件一个 storage，O(1) membership，连续迭代，无 archetype 迁移。
- **稳定的 `ComponentRef<T>`**：扩容与 tick 末压缩后仍然有效，并能识别失效句柄。
- **state-first Collector**：`Collected`、`Matching`、`Clashing`、`Changed` 只在 `Flush()` 时发布。
- **物化查询**：`EntityQuery` 捕获稳定 `EntityId` 快照，按需 Refresh。
- **确定性系统 DAG**：组、嵌套组、`Before` / `After`、前向引用和稳定拓扑排序。
- **显式 tick 边界**：删除立即逻辑生效，`EndTick` 才压缩存储并回收 EntityId。
- **可重启 World**：manager 与依赖注入实例跨成功启停周期复用。
- **Tag 组件**：`ITagComponent<T>` 只保存 membership，不保存每实体 payload。
- **双 EntityId 模式**：默认 `uint`（20 位 index + 12 位 generation）；`COREECS_ULONG_ENTITY` 切换为 `ulong`（32 + 32）。
- **目标框架**：`net8.0` 与 `netstandard2.1`，C# 9。

## 安装

```bash
dotnet add package CoreECS
```

发布包使用默认 `uint` EntityId。需要 64 位布局时，请从源码以 `COREECS_ULONG_ENTITY` 编译。

## 第一个 World

```csharp
using CoreECS;
using CoreECS.Defines;

public struct Position : IComponent<Position>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

public struct Velocity : IComponent<Velocity>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}

var world = new World();
world.Startup();

var entity = world.CreateEntity();
entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });

using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>().OfAll<Velocity>());
collector.Flush();

world.BeginTick();
foreach (var entityId in collector.Collected)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}
world.Tick();
world.EndTick();

world.Shutdown();
```

## 组件

数据组件实现 `IComponent<T>`，必须显式实现两个 Hook。Hook 只拿到 `EntityId`，用于初始化或清理组件自身，不能修改实体结构。

```csharp
public struct Health : IComponent<Health>
{
    public int Value;

    public void OnCreate(EntityId entityId) => Value = 100;
    public void OnDestroy(EntityId entityId) { }
}
```

- 同一实体同一类型只有一个组件；重复创建抛异常。
- 删除不存在的组件抛异常；重复销毁实体是 no-op。
- 空标记组件可实现 `ITagComponent<T>`，不分配 payload：

```csharp
public struct Stunned : ITagComponent<Stunned>
{
}
```

框架永远不会调用 Tag 的 Hook。

## 查询

`EntityQuery` 是稳定的物化快照，不订阅世界，也不需要 Dispose。

```csharp
EntityQuery query = world.CreateQuery(
    EntityMatcher.With.OfAll<Position>().OfNone<Stunned>());

foreach (var entityId in query.Entities)
{
    // 下次 Refresh 前快照不变。
}
```

`CreateQuery(matcher, refreshImmediately: false)` 初始为空，直到调用 `Refresh()`。

## Collector

Collector 在一个 flush 阶段内跟踪成员关系和相关组件变化。

```csharp
using var changed = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default);

// ... 游戏逻辑写入 ...

changed.Flush();
foreach (var entityId in changed.Changed) { /* 重新处理 */ }
```

- `Collected`：上次 Flush 后的稳定成员。
- `Matching` / `Clashing`：相对上次 Flush 的净进入/离开。
- `Changed`：按 flags 需要重新处理的实体。
- `Flush()`：全局 pump 一次共享 change journal，并发布当前 Collector 的阶段结果。
- `Dispose()` 幂等；`World.Shutdown()` 会使存活 Collector 失效。

## 系统与分组

`ISystem` 必须实现 `OnCreate`、`OnTick`、`OnDestroy`。顺序通过声明式 DAG 解析。

```csharp
world.RegisterGroup("Physics");
world.RegisterGroup("Render");

world.RegisterSystem<InputSystem>("Physics");
world.RegisterSystem<MovementSystem>("Physics").After<InputSystem>();
world.RegisterSystem<RenderSystem>("Render").After("Physics");
```

- 组名全局唯一；每个具体 System 类型只能注册一次。
- 允许前向锚点，在 `BeginTick` 时解析。
- Tick 内修改注册图只影响下一次 `BeginTick`，当前 tick 使用已捕获顺序。
- 成环会记录错误，并对整图回退注册顺序。

## Tick 与生命周期

```text
Startup → BeginTick → Tick → EndTick → Shutdown
```

- 删除组件或实体立即逻辑生效。
- 物理压缩与 EntityId 回收到 `EndTick` 才发生。
- 裸 `RO` / `RW` 引用只在当前 tick 阶段有效，`EndTick` 后必须重新获取。
- 组件存活期间，`ComponentRef<T>` 跨压缩保持有效。
- 成功 `Shutdown()` 后，同一 `World` 可再次 `Startup()`；`OnRegister` 只执行一次，`OnSetup` / `OnCleanup` 每周期执行。
- `Shutdown()` 后必须丢弃所有旧 Entity、句柄、Query、Collector 和注册 handle。

## 构建

```bash
dotnet build CoreECS.sln --configuration Release
dotnet test CoreECS.sln --configuration Release
dotnet run -c Release --project Benchmark/Benchmark.csproj
```

64 位 EntityId：

```bash
dotnet test CoreECS.sln -p:CoreECSUseUlongEntity=true
```

## 仓库结构

```text
Kernel/       CoreECS 库
Test/         NUnit 测试
Benchmark/    BenchmarkDotNet 套件
docs/         指南与迁移说明
```

## 许可证

MIT。
