# CoreECS 快速入门

本指南覆盖 v2 的完整工作流：World 生命周期、数据与 Tag 组件、实体操作、查询、Collector、系统以及重启。

## 1. World 生命周期

```csharp
var world = new World();
world.Startup();

world.BeginTick();
world.Tick();
world.EndTick();

world.Shutdown();
```

成功 Shutdown 后可以再次 Startup。manager 与 DI 实例会复用；`OnRegister` 只执行一次，`OnSetup` / `OnCleanup` 每周期执行。上一个周期的所有对象都必须视为失效。

继承 `World` 注册服务、manager 和系统：

```csharp
public sealed class GameWorld : World
{
    protected override void OnRegister(
        IManagerRegister managers,
        IServiceCollection services)
    {
        services.AddSingleton<GameConfig>();
    }

    protected override void OnSetup()
    {
        RegisterGroup("Gameplay");
        RegisterSystem<MovementSystem>("Gameplay");
    }

    protected override void OnCleanup() { }
}
```

## 2. 数据组件

```csharp
public struct Position : IComponent<Position>
{
    public float X;
    public float Y;

    public void OnCreate(EntityId entityId) { }
    public void OnDestroy(EntityId entityId) { }
}
```

两个 Hook 都必须实现。Hook 可以修改组件自身字段，但执行期间任何实体/组件结构修改都会被拒绝。

创建、读取与销毁：

```csharp
var entity = world.CreateEntity();
var position = entity.CreateComponent(new Position { X = 1, Y = 2 });

ref readonly Position read = ref position.RO;
ref Position write = ref position.RW; // 增加 Revision
write.X += 10;

entity.DestroyComponent<Position>();
```

- 重复创建抛异常。
- 删除不存在的组件抛异常。
- 连续写多个字段时应缓存 `ref`：每次访问 `.RW` 都会增加 `Revision` 并可能记录变更。

## 3. Tag 组件

```csharp
public struct Stunned : ITagComponent<Stunned>
{
}
```

Tag 不分配 payload，Hooks 永远不会被调用。`CreateComponent<Stunned>()`、`HasComponent<Stunned>()`、`DestroyComponent<Stunned>()` 与普通组件一致。`GetComponent<Stunned>()` 返回有效句柄，其 `RO`/`RW` 指向共享默认值。

## 4. 实体操作

```csharp
Entity created = world.CreateEntity(mask: 0b0001);
Entity fetched = world.GetEntity(created.EntityId);

bool alive = created.IsValid;
ulong mask = created.Mask; // 创建后不可变

bool has = created.HasComponent<Position>();
bool found = created.TryGetComponent<Position>(out var positionRef);
bool existed = created.GetOrCreateComponent<Position>(
    out var ensured, new Position { X = 0, Y = 0 });

var buffer = new List<ComponentRef>();
int count = created.GetComponents(buffer); // 无分配 untyped 枚举

world.DestroyEntity(created);
world.DestroyEntity(created); // no-op
```

`Entity` 比较 World 身份与 `EntityId`。不同 World 可能产生相等的 `EntityId`。

## 5. Matcher 与 Query

```csharp
EntityMatcher matcher = EntityMatcher.With
    .OfAll<Position>()
    .OfAny<Velocity>()
    .OfNone<Stunned>();

EntityQuery query = world.CreateQuery(matcher);
int count = query.Count;

foreach (EntityId entityId in query.Entities)
{
    // 稳定快照
}

query.Refresh();
```

Matcher 在第一次绑定 Query/Collector 时冻结。筛选规则：

- `OfAll`：必须拥有全部组件。
- `OfAny`：至少拥有一个。
- `OfNone`：一个都不能有。
- `WithMask`：与实体 mask 按位相交（`(matcherMask & entityMask) != 0`）。

`CreateQuery(matcher, refreshImmediately: false)` 初始为空，直到 `Refresh()`。

## 6. Collector

```csharp
using EntityCollector collector = world.CreateCollector(
    EntityMatcher.With.OfAll<Position>(),
    EntityCollectorFlag.Default);

collector.Flush();

foreach (EntityId entityId in collector.Collected) { }
foreach (EntityId entityId in collector.Matching) { }
foreach (EntityId entityId in collector.Clashing) { }
foreach (EntityId entityId in collector.Changed) { }
```

Flags：

| Flag | 含义 |
|------|------|
| `RevisionAsChange` | 组件写入进入 `Changed`。 |
| `MatchAsChange` | 新成员进入 `Changed`。 |
| `ClashAsChange` | 离开成员进入 `Changed`。 |
| `RelatedComponentOnly` | 只有 matcher 相关组件的写入进入 `Changed`。 |
| `Default` | `RevisionAsChange \| MatchAsChange \| RelatedComponentOnly`。 |

`Flush()` 计算相对上次 Flush 的净成员变化。同一 phase 内先离开又进入不会产生 `Matching`/`Clashing`，但可能仍产生一次 `Changed`。

## 7. 系统与分组

```csharp
public sealed class MovementSystem : ISystem
{
    private readonly World m_world;

    public MovementSystem(World world) => m_world = world;

    public void OnCreate() { }
    public void OnTick(ulong tickMask) { }
    public void OnDestroy() { }
}
```

```csharp
world.RegisterGroup("Gameplay");
world.RegisterSystem<MovementSystem>("Gameplay")
    .After<InputSystem>()
    .Before("Render");
```

`TickGroup` 默认 `ulong.MaxValue`，可覆盖后用 `world.Tick(mask)` 过滤。

## 8. Tick 边界

```csharp
world.BeginTick();
// 结构性修改立即逻辑生效
world.Tick();
world.EndTick(); // 压缩存储，回收 EntityId
```

- 被删除组件立即从 Query 和 Matcher 中消失。
- packed slot 到 `EndTick` 才移动。
- 组件存活期间 `ComponentRef<T>` 跨压缩有效。
- 裸 ref 不保证跨 tick，请在下一 tick 重新获取。

## 9. 64 位 EntityId

从源码构建并测试：

```bash
dotnet test CoreECS.sln -p:CoreECSUseUlongEntity=true
```

默认布局：uint，20 位 index + 12 位 generation。ulong 布局：32 + 32。
