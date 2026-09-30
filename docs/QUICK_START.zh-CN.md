# CoreECS 快速入门

[English](QUICK_START.md) · 简体中文 · [项目概览](../README.zh-CN.md)

本文先构建一个移动实体的小示例，再介绍实际游戏循环中最重要的 API。

## 1. 安装与命名空间

```bash
dotnet add package CoreECS
```

大多数程序需要以下命名空间：

```csharp
using CoreECS;
using CoreECS.Defines;
```

`Structure`、管理器实现与调度句柄分别位于 `CoreECS.Structures` 和 `CoreECS.Managers`。

## 2. 定义组件

组件是普通结构体，实现哪个接口就按哪种方式存储。

```csharp
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

public struct Name : ISparseComponent<Name>
{
    public string Value;
}

public struct Player : ITagComponent<Player>
{
}
```

### 存储类别

| 类别 | 接口 | 存储与行为 |
| --- | --- | --- |
| Dense | `IComponent<T>` | 按行对齐的 archetype 列；增删会改变 archetype。 |
| Sparse | `ISparseComponent<T>` | 位于 dense 列之外的逐实体数据；增删不改变 archetype。 |
| Tag | `ITagComponent<T>` | 只有存在位，不携带组件值，也没有可用的 ref。 |

Dense 与 sparse 组件可以实现生命周期钩子：

```csharp
public struct Lifetime : IComponent<Lifetime>
{
    public ulong Owner;

    public void OnCreate(ulong entityId) => Owner = entityId;
    public void OnDestroy(ulong entityId) { }
}
```

如果 `OnCreate` 需要看到真正的初始值，就把值直接传给 `CreateComponent(value)`；先创建默认组件、再事后用 `.RW` 赋值，`OnCreate` 拿到的只会是 `default(T)`。

## 3. 启动 World

```csharp
var world = new World();
world.Startup();
```

创建实体、收集器、查询、命令缓冲区或注册系统前必须调用 `Startup()`；使用结束后调用 `Shutdown()`。

每个模拟步骤按以下顺序执行：

```csharp
world.BeginTick();
world.Tick();
world.EndTick();
```

`BeginTick()` 增加 `TickCount` 并应用待处理的调度变化；`Tick(mask)` 运行 `TickGroup` 与 mask 相交的系统；`EndTick()` 完成系统清理。

World 不是线程安全的：创建、驱动和关闭都要在同一个线程上。

## 4. 创建实体与组件

```csharp
var entity = world.CreateEntity();

entity.CreateComponent(new Position { X = 10, Y = 20 });
entity.CreateComponent(new Velocity { X = 1, Y = -1 });
entity.CreateComponent(new Name { Value = "Player One" });
entity.CreateComponent<Player>();
```

通过实体句柄读取、写入、检查和移除组件：

```csharp
if (entity.HasComponent<Position>())
{
    var positionHandle = entity.GetComponent<Position>();
    Console.WriteLine(positionHandle.RO.X);
    positionHandle.RW.X += 5;
}

if (entity.TryGetComponent<Name>(out var name))
    Console.WriteLine(name.RO.Value);

entity.DestroyComponent<Velocity>();
world.DestroyEntity(entity);
```

`GetOrCreateComponent` 在组件已存在时返回 `true`：

```csharp
var existed = entity.GetOrCreateComponent(
    out ComponentRef<Name> name,
    new Name { Value = "Unnamed" });
```

Tag 用 `HasComponent<T>()` 检查是否存在。Tag 不携带数据，因此创建或获取 tag 时只会返回默认组件引用。

### 实体句柄与 mask

`Entity` 句柄能跟随 archetype 迁移保持有效；实体一旦销毁，旧句柄的 `IsValid` 就是 `false`。

Mask 是由应用定义的位字段，可供 matcher 和 system tick 筛选：

```csharp
[Flags]
public enum EntityLayer : ulong
{
    Simulation = 1UL << 0,
    Presentation = 1UL << 1,
}

var simulated = world.CreateEntity((ulong)EntityLayer.Simulation);
simulated.SetMask((ulong)(EntityLayer.Simulation | EntityLayer.Presentation));
```

Mask 是 archetype key 的一部分。`SetMask` 可能迁移实体，但会保留 dense、sparse 和 tag 组件，也不会调用组件生命周期钩子。默认 mask 为 `ulong.MaxValue`。

## 5. 匹配实体

通过 `OfAll`、`OfAny` 和 `OfNone` 构建 matcher：

```csharp
var movingPlayers = EntityMatcher.With
    .OfAll<Position, Velocity>()
    .OfAll<Player>();

var visibleWithoutVelocity = EntityMatcher
    .WithMask((ulong)EntityLayer.Presentation)
    .OfAll<Position>()
    .OfNone<Velocity>();
```

规则如下：

- 所有 `OfAll` 条件都必须存在。
- 配置了 `OfAny` 时，至少一个条件必须存在。
- 所有 `OfNone` 条件都不能存在。
- `WithMask(mask)` 会先要求 `(entity.Mask & mask) != 0`。
- `EntityMatcher.With` 使用 `ulong.MaxValue`，对普通非零 mask 等同于不筛选 mask。
- 没有组件条件时，所有通过 mask 检查的实体都匹配。

## 6. 查询当前状态

`IEntityQuery` 持有快照。快照初始为空，仅在调用 `Refresh()` 时更新。

```csharp
using var query = world.CreateQuery(
    EntityMatcher.With.OfAll<Position, Velocity>());

query.Refresh();

foreach (var entityId in query.Entities)
{
    var current = world.GetEntity(entityId);
    ref var position = ref current.GetComponent<Position>().RW;
    ref readonly var velocity = ref current.GetComponent<Velocity>().RO;
    position.X += velocity.X;
    position.Y += velocity.Y;
}
```

使用 `query.Structures` 批量处理 dense 数据：

```csharp
foreach (var structure in query.Structures)
{
    var positions = structure.GetReadWriteDenseColumn<Position>();
    var velocities = structure.GetReadOnlyDenseColumn<Velocity>();

    for (var row = 0; row < structure.Count; row++)
    {
        positions[row].X += velocities[row].X;
        positions[row].Y += velocities[row].Y;
    }
}
```

`GetReadWriteDenseColumn<T>()` 会将 Structure 中每一行标记为已变更，因此应在每个 Structure 上只获取一次，而不是逐行获取。Structure span 仅适用于 dense 组件。

## 7. 使用 Collector 追踪变化

Collector 一直在监听变化，但只有你调用 `Flush()`，它才会把结果放出来。

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>());

// 对 World 执行一些操作……
collector.Flush();

foreach (var id in collector.Matching)
    Console.WriteLine($"Entered: {id}");

foreach (var id in collector.Clashing)
    Console.WriteLine($"Left: {id}");

foreach (var id in collector.Changed)
    Console.WriteLine($"Reprocess: {id}");
```

各缓冲区含义：

| 缓冲区 | 最近一次 `Flush()` 后的内容 |
| --- | --- |
| `Collected` | 当前匹配的全部实体 |
| `Matching` | 本阶段进入的实体 |
| `Clashing` | 本阶段离开的实体 |
| `Changed` | 根据 collector flags 选出、需要重新处理的实体 |

默认 flags 为：

```csharp
EntityCollectorFlag.RevisionAsChange |
EntityCollectorFlag.MatchAsChange |
EntityCollectorFlag.RelatedComponentOnly
```

离开的实体始终出现在 `Clashing`；加入 `ClashAsChange` 可同时将其镜像到 `Changed`：

```csharp
using var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);
```

只看成员进出的话，用 `EntityCollectorFlag.None` 就够了；不再需要的 collector 记得 `Dispose()`。

## 8. 定义与调度系统

System 实现 `ISystem`，构造函数参数由 World 的注入代理解析。

```csharp
public sealed class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityQuery m_query;

    public MovementSystem(World world) => m_world = world;

    public ulong TickGroup => 1UL << 0;

    public void OnCreate()
    {
        m_query = m_world.CreateQuery(
            EntityMatcher.With.OfAll<Position, Velocity>());
    }

    public void OnTick(ulong tickMask)
    {
        m_query.Refresh();
        foreach (var structure in m_query.Structures)
        {
            var positions = structure.GetReadWriteDenseColumn<Position>();
            var velocities = structure.GetReadOnlyDenseColumn<Velocity>();
            for (var row = 0; row < structure.Count; row++)
            {
                positions[row].X += velocities[row].X;
                positions[row].Y += velocities[row].Y;
            }
        }
    }

    public void OnDestroy() => m_query.Dispose();
}
```

在 Startup 后注册系统：

```csharp
world.RegisterGroup("Simulation");
world.RegisterSystem<MovementSystem>("Simulation");

world.RegisterGroup("Presentation").After("Simulation");
world.RegisterSystem<RenderSystem>("Presentation");
```

注册句柄可声明 `Before<T>()`、`After<T>()`、`Before("Group")` 或 `After("Group")`。向 `RegisterGroup` 传入父组名称即可创建嵌套组。允许前向引用；无法解析的 anchor 会被记录并忽略，环形约束则回退到注册顺序。

Tick 内发生的调度变化会在下一次 `BeginTick()` 生效。实体和组件操作默认立即生效，除非显式记录到 command buffer。

使用 tick mask 运行部分系统：

```csharp
world.BeginTick();
world.Tick(1UL << 0);
world.EndTick();
```

## 9. 记录结构性操作

想把结构操作攒起来、稍后按顺序一次性执行，就用 `CommandBuffer`：

```csharp
using var commands = world.CreateCommandBuffer();

var spawned = commands.CreateEntity((ulong)EntityLayer.Simulation);
commands.CreateComponent(spawned, new Position { X = 0, Y = 0 });
commands.CreateComponent(spawned, new Velocity { X = 2, Y = 0 });
commands.CreateComponent<Player>(spawned);

commands.Playback();
```

- 记录命令时不会修改 World。
- `Playback()` 按记录顺序立即执行命令，然后清空缓冲区以便复用。
- `CreateEntity()` 返回的 placeholder 只属于当前 command buffer，并且只在待回放批次中有效。
- 未回放便调用 `Dispose()` 会丢弃待处理命令。
- 即使回放抛出异常，记录也会被清空，缓冲区仍可复用。

## 10. 避免失效的 ref 与 span

结构变更可能移动实体或重新分配 dense 列。不要让裸 ref 或 span 跨越这类操作继续存活。

不安全：

```csharp
ref var position = ref entity.GetComponent<Position>().RW;
entity.CreateComponent<Velocity>();
position.X = 10; // 裸 ref 可能已指向失效存储。
```

安全：

```csharp
var positionHandle = entity.GetComponent<Position>();
entity.CreateComponent<Velocity>();
positionHandle.RW.X = 10; // 句柄会重新解析当前位置。
```

把以下操作当成结构边界：

- 创建或销毁实体；
- 添加或移除 dense 组件；
- 修改实体 mask；
- 调用 `CommandBuffer.Playback()`；
- 间接执行上述操作的方法调用。

`Analyzers/` 中的分析器会以 `ECS0001` 与 `ECS0002` 报告常见违规。详见 [Analyzers/README.md](../Analyzers/README.md)。

## 11. 自定义启动与依赖注入

继承 `World` 可注册服务与管理器：

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class GameWorld : World
{
    protected override void OnRegister(
        IManagerRegister register,
        IServiceCollection services)
    {
        services.AddSingleton<GameClock>();
    }

    protected override void OnSetup()
    {
        RegisterSystem<MovementSystem>();
    }

    protected override void OnCleanup()
    {
    }
}
```

`OnRegister` 仅在第一次 Startup 构建注入代理前运行；`OnSetup` 在每次成功 Startup 后运行；`OnCleanup` 在每次 Shutdown 前运行。已 Shutdown 的 `World` 不能重启；需要再来一轮时，新建一个实例。

内置容器会注册 `IWorld`、具体 World 类型和各 World manager。需要接入其他容器时，可重写 `GetInjectionProxyFactory()`。

## 12. 完整示例

```csharp
using System;
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

public sealed class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityQuery m_query;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_query = m_world.CreateQuery(
            EntityMatcher.With.OfAll<Position, Velocity>());
    }

    public void OnTick(ulong tickMask)
    {
        m_query.Refresh();
        foreach (var structure in m_query.Structures)
        {
            var positions = structure.GetReadWriteDenseColumn<Position>();
            var velocities = structure.GetReadOnlyDenseColumn<Velocity>();
            for (var row = 0; row < structure.Count; row++)
            {
                positions[row].X += velocities[row].X;
                positions[row].Y += velocities[row].Y;
            }
        }
    }

    public void OnDestroy() => m_query.Dispose();
}

public static class Program
{
    public static void Main()
    {
        var world = new World();
        world.Startup();
        world.RegisterSystem<MovementSystem>();

        var entity = world.CreateEntity();
        entity.CreateComponent(new Position());
        entity.CreateComponent(new Velocity { X = 1, Y = 0.5f });

        for (var frame = 0; frame < 3; frame++)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();

            ref readonly var position = ref entity.GetComponent<Position>().RO;
            Console.WriteLine($"Frame {frame}: ({position.X}, {position.Y})");
        }

        world.Shutdown();
    }
}
```

[项目概览](../README.zh-CN.md) · [English](QUICK_START.md)
