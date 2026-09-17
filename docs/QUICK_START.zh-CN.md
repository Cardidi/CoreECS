# 快速入门指南

> 从零开始使用 CoreECS：从 `World` 搭建到收集器与完整可运行示例。

**[English](QUICK_START.md)** · **简体中文**

[← 返回 README（中文）](../README.zh-CN.md) · [README (English)](../README.md)

## 目录

1. [创建 World](#1-创建-world)
2. [定义组件](#2-定义组件)
3. [创建实体](#3-创建实体)
4. [为实体添加组件](#4-为实体添加组件)
5. [访问组件](#5-访问组件)
6. [移除组件](#6-移除组件)
7. [定义系统](#7-定义系统)
8. [管理系统](#8-管理系统)
9. [实体匹配器](#9-实体匹配器)
10. [实体收集器](#10-实体收集器--高级筛选与变更追踪)
11. [完整示例](#11-完整示例)

---

## 1. 创建 World

`World` 是实体、组件与系统的根容器。

```csharp
using CoreECS;

var world = new World();
world.Startup();
```

### `Startup()` 之前

**不要**：

- 创建实体
- 添加组件
- 注册系统

**可以**通过自定义 `World` 子类做准备：

- 重写 `RegisterServices` 注册 DI 服务（在首次 `Startup()` 时构建）
- 重写生命周期钩子（`OnRegister`、`OnConstruct`、`OnStart`、Tick/关闭等）或注册额外管理器

`Startup()` 之后可通过 `World.InjectionProxy` 解析服务（首次 `Startup()` 完成前为 `null`）。

> **线程安全：** World 非线程安全，请在单线程（通常是主线程/游戏线程）访问。

使用完毕后调用 `World.Shutdown()` 释放资源。

---

## 2. 定义组件

组件为实现 `IComponent<T>` 的纯数据结构：

```csharp
public struct PositionComponent : IComponent<PositionComponent>
{
    public float X;
    public float Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X;
    public float Y;
}

public struct HealthComponent : IComponent<HealthComponent>
{
    public float Value;
}
```

可选生命周期钩子：

```csharp
public struct LifecycleComponent : IComponent<LifecycleComponent>
{
    public bool OnCreateCalled;
    public bool OnDestroyCalled;

    public void OnCreate(ulong entityId) => OnCreateCalled = true;
    public void OnDestroy(ulong entityId) => OnDestroyCalled = true;
}
```

---

## 3. 创建实体

存储层以 `ulong` 标识实体；对外推荐使用 `Entity` 结构体。

```csharp
var entity = world.CreateEntity();
var anotherEntity = world.GetEntity(entityId);
```

### 实体掩码

用位掩码标记实体类型，供匹配器过滤：

```csharp
enum EntityType
{
    Actor    = 1 << 1,
    Terrain  = 1 << 2,
}

var actor = world.CreateEntity((ulong)EntityType.Actor);
```

默认 `CreateEntity()` 使用 `ulong.MaxValue`（与任意匹配器掩码兼容）。

---

## 4. 为实体添加组件

```csharp
var velocityRef = entity.CreateComponent<VelocityComponent>();
velocityRef.RW.X = 1;
velocityRef.RW.Y = 1;

entity.CreateComponent<HealthComponent>().RW.Value = 100;

// 推荐：一步写入初始值（OnCreate 在该值上调用）
var positionRef = entity.CreateComponent(new PositionComponent { X = 10, Y = 20 });

// 避免：OnCreate 在 default(T) 上调用，随后 RW 整体覆盖
var positionRef2 = entity.CreateComponent<PositionComponent>();
positionRef2.RW = new PositionComponent { X = 10, Y = 20 };
```

---

## 5. 访问组件

只读用 `RO`，写入用 `RW`（写入会标记修订，并可驱动收集器的 `RevisionAsChange`）。

```csharp
var positionRef = entity.GetComponent<PositionComponent>();
Console.WriteLine($"Position: ({positionRef.RO.X}, {positionRef.RO.Y})");

bool hasHealth = entity.HasComponent<HealthComponent>();
var allComponents = entity.GetComponents();
```

### RO / RW 说明

| 访问 | 行为 |
|------|------|
| `RO` | 只读；热路径优先使用 |
| `RW` | 可写；触发修订追踪（收集器上的 `RevisionAsChange`） |

### 扩展方法

```csharp
if (entity.TryGetComponent<PositionComponent>(out var pos))
    Console.WriteLine($"({pos.RO.X}, {pos.RO.Y})");

bool existed = entity.GetOrCreateComponent<VelocityComponent>(out var vel);
if (!existed)
    vel.RW = new VelocityComponent { X = 1, Y = 1 };

entity.GetOrCreateComponent(out var health, new HealthComponent { Value = 100 });
```

`GetOrCreateComponent`：组件已存在返回 `true`，新建返回 `false`。

---

## 6. 移除组件

```csharp
entity.DestroyComponent(positionRef);
entity.DestroyComponent<HealthComponent>();
```

---

## 7. 定义系统

系统实现 `ISystem`，通常通过收集器处理实体。

- 在 `RegisterServices` 中注册依赖；World 通过 `IInjectionProxy` 解析构造函数参数。
- 用 `TickGroup` 分组，通过 `World.Tick(tickMask)` 过滤执行（`(system.TickGroup & tickMask) != 0`）。
- 在 `OnCreate` 中创建收集器，读取缓冲区前调用 `Flush()`，在 `OnDestroy` 中 `Dispose()`。

```csharp
public class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityCollector m_movingEntities;

    public ulong TickGroup => ulong.MaxValue;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>());
    }

    public void OnTick(ulong tickMask)
    {
        m_movingEntities.Flush();
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            position.RW.X += velocity.RW.X;
            position.RW.Y += velocity.RW.Y;
        }
    }

    public void OnDestroy() => m_movingEntities?.Dispose();
}
```

---

## 8. 管理系统

注册顺序即执行顺序（先入先执行）。避免在 `BeginTick()` 与 `EndTick()` 之间注册/注销系统 —— 变更会排队到下一次 `BeginTick()`。实体与组件操作**不会**被延迟。

```csharp
var world = new World();
world.Startup();
world.RegisterSystem<MovementSystem>();

var movementSystem = world.FindSystem<MovementSystem>();

while (running)
{
    world.BeginTick();
    world.Tick();
    world.EndTick();
}
```

---

## 9. 实体匹配器

```csharp
var positionOnly = EntityMatcher.With.OfAll<PositionComponent>();

var positionOrVelocity = EntityMatcher.With
    .OfAny<PositionComponent>()
    .OfAny<VelocityComponent>();

var noHealth = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfNone<HealthComponent>();

var complex = EntityMatcher.With
    .OfAll<PositionComponent>()
    .OfAll<VelocityComponent>()
    .OfNone<HealthComponent>();

var byMask = EntityMatcher.WithMask((ulong)EntityType.Actor);
```

**掩码规则**

- `EntityMatcher.With` → `EntityMask == ulong.MaxValue`（不按掩码过滤）。
- `WithMask(m)` → 先满足 `(entity.Mask & m) != 0`，再应用组件规则。
- 未设置 `OfAll` / `OfAny` / `OfNone` → 通过掩码检查的任意实体均可匹配。

---

## 10. 实体收集器 — 高级筛选与变更追踪

收集器跟踪满足匹配器的实体，并在每个 `Flush()` 阶段汇总变更。

### 基本用法

```csharp
var collector = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>());

collector.Flush();
for (var i = 0; i < collector.Collected.Count; i++)
{
    var entity = world.GetEntity(collector.Collected[i]);
    // ...
}
```

新代码请使用 `Flush()`，勿用已废弃的 `IEntityCollector.Change()`。

### 缓冲区（每次 `Flush()` 之后）

| 缓冲区 | 含义 |
|--------|------|
| `Collected` | 当前在收集器内的实体 |
| `Matching` | 本阶段新进入 |
| `Clashing` | 本阶段离开 |
| `Changed` | 需重新处理的子集（由标志位控制） |

每帧/每阶段读取任何缓冲区前，先调用一次 `Flush()`。

### 标志位

`EntityCollectorFlag.Default` 会镜像到 `Changed`：

- 结构性**进入**（`MatchAsChange`）
- 与匹配器相关的**增删组件**（`RelatedComponentOnly`）
- 与匹配器相关的**数据修订**（`RevisionAsChange` + `RelatedComponentOnly`）

`Default` **不包含**离开事件（查 `Clashing`，或加上 `ClashAsChange` 镜像到 `Changed`）。

```csharp
var @default = world.CreateCollector(EntityMatcher.With.OfAll<PositionComponent>());

var withClash = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.Default | EntityCollectorFlag.ClashAsChange);

var membershipOnly = world.CreateCollector(
    EntityMatcher.With.OfAll<PositionComponent>(),
    EntityCollectorFlag.None);
```

### 变更追踪示例

```csharp
var entity = world.CreateEntity();
entity.CreateComponent<PositionComponent>();
collector.Flush();

foreach (var id in collector.Matching)
    Console.WriteLine($"Joined: {id}");
foreach (var id in collector.Clashing)
    Console.WriteLine($"Left: {id}");
```

| 标志位 | 对 `Changed` 的影响 |
|--------|---------------------|
| `RevisionAsChange` | 数据修订（含于 `Default`） |
| `MatchAsChange` | 新进入（含于 `Default`） |
| `ClashAsChange` | 离开（不含于 `Default`） |
| `RelatedComponentOnly` | 仅匹配器相关组件事件（含于 `Default`） |
| `None` | `Changed` 为空；使用 `Matching` / `Clashing` / `Collected` |

### 最佳实践

1. 读取缓冲区前务必 `Flush()`。
2. 迭代 `Collected` 时若可能改变成员关系，用索引 `for` 而非 `foreach`。
3. 在 `OnDestroy` 中对收集器 `Dispose()`。
4. 按工作流选择标志位；若离开事件须出现在 `Changed` 中，添加 `ClashAsChange`。

---

## 11. 完整示例

```csharp
using System;
using CoreECS;
using CoreECS.Defines;

public struct PositionComponent : IComponent<PositionComponent>
{
    public float X, Y;
}

public struct VelocityComponent : IComponent<VelocityComponent>
{
    public float X, Y;
}

public class MovementSystem : ISystem
{
    private readonly World m_world;
    private IEntityCollector m_movingEntities;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate()
    {
        m_movingEntities = m_world.CreateCollector(
            EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>());
    }

    public void OnTick(ulong tickMask)
    {
        m_movingEntities.Flush();
        for (var i = 0; i < m_movingEntities.Collected.Count; i++)
        {
            var entity = m_world.GetEntity(m_movingEntities.Collected[i]);
            var position = entity.GetComponent<PositionComponent>();
            var velocity = entity.GetComponent<VelocityComponent>();
            position.RW.X += velocity.RW.X * 0.016f;
            position.RW.Y += velocity.RW.Y * 0.016f;
        }
    }

    public void OnDestroy() => m_movingEntities?.Dispose();
}

class Program
{
    static void Main()
    {
        var world = new World();
        world.Startup();
        world.RegisterSystem<MovementSystem>();

        var entity = world.CreateEntity();
        entity.CreateComponent<PositionComponent>().RW = new PositionComponent { X = 0, Y = 0 };
        entity.CreateComponent<VelocityComponent>().RW = new VelocityComponent { X = 10, Y = 5 };

        for (var i = 0; i < 100; i++)
        {
            world.BeginTick();
            world.Tick();
            world.EndTick();
            var pos = entity.GetComponent<PositionComponent>();
            Console.WriteLine($"Frame {i}: ({pos.RW.X:F2}, {pos.RW.Y:F2})");
        }

        world.Shutdown();
    }
}
```

---

**[English](QUICK_START.md)** · **简体中文**

[← 返回 README（中文）](../README.zh-CN.md) · [README (English)](../README.md)
