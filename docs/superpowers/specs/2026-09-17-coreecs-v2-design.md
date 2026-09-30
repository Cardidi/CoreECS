# CoreECS v2 设计文档

- 日期：2026-09-17
- 分支：`v2`
- 状态：待评审
- 目标框架：`net8.0` + `netstandard2.1`（保持不变）

## 1. 背景与目标

v1 采用"每组件类型一个 `ComponentStore`"的存储模型，实体通过 `EntityGraph` 持有组件引用列表；系统按注册顺序执行，仅支持 `TickGroup` 掩码过滤。

v2 将存储重构为 **archetype（Structure）模型**，并在此之上引入三组件类别、结构化查询、系统排序注册与 CommandBuffer。采用渐进式交付（内核先行），每个阶段结束时 `dotnet build` + `dotnet test` 全绿。

### 设计原则

- 存量组件代码零改动：现有 `IComponent<T>` 实现即 Dense 组件
- 当前阶段严禁非泛型组件接口（所有组件接口必须泛型自引用）
- 结构变更立即生效；遍历稳定性由 collector / query 快照兜底
- 用户可见 API 尽量与 v1 保持一致（`ComponentRef`、collector、命名等）

## 2. 接口与类型系统

### 2.1 三接口

```csharp
public interface IComponent<T> where T : struct, IComponent<T>            // Dense，决定 Structure 归属
{
    void OnCreate(ulong entityId) {}
    void OnDestroy(ulong entityId) {}
}

public interface ISparseComponent<T> : IComponent<T>
    where T : struct, ISparseComponent<T> { }                           // Sparse，不决定归属

public interface ITagComponent<T> : IComponent<T>
    where T : struct, ITagComponent<T> { }                                // Tag，不决定归属
```

- **kind 判定**：按最派生接口判定，优先级 `Tag > Sparse > Dense`
- 三种 kind 均调用 `OnCreate` / `OnDestroy`（Tag 使用默认空实现）
- 实体销毁时，其所有 kind 的组件都收到 `OnDestroy`

### 2.2 ComponentTypeRegistry

- 全局静态注册表：`Type → TypeId + Kind`，只增不减，线程安全
- `TypeId` 为单调递增 `uint`：Dense 的 TypeId 用于 Structure 组合键；Sparse / Tag 的 TypeId 用于容器索引与位图
- kind 与 ID 是类型的固有属性，与 World 实例无关；结构数据仍按 World 隔离

## 3. Structure 内核

### 3.1 组成键

`StructureKey = (排序后的 Dense TypeId 数组, Mask)`；`Mask` 参与 Archetype 分类。

- 注册表 `Dictionary<StructureKey, Structure>` 去重
- `SetMask` 会触发实体迁移
- `CreateEntity(mask)` 选择初始结构

### 3.2 Structure 内部布局

```
Structure（archetype）
├─ Mask（组成键的一部分，结构内所有实体共享）
├─ Dense 数据：每类型一个 T[]（行对齐）+ revision[]（按需）
├─ EntityLocation[] m_locations        // row → 实体 location
├─ SparseComponentContainer
│   └─ 每 Sparse 类型：row → 数据（dense 数据数组 + 存在标记）
└─ TagContainer
    └─ 每 row 一个 tag 位图（Tag TypeId → bit）
```

- 容量按需扩容；本阶段不做 chunk
- 行（row）是结构内实体索引，`s.RO/RW<T>()` 返回的 Span 与行对齐

### 3.3 实体迁移

触发：创建/删除 Dense 组件、`SetMask`、`CreateEntity`。

流程：

1. 计算目标 `StructureKey` → 查/建 Structure
2. 拷贝 Dense 数据、搬运 Sparse 数据、拷贝 Tag 位图
3. 旧结构 swap-remove（末行补洞，更新被移动实体的 `EntityLocation.Row`）
4. 更新实体 `EntityLocation.Structure/Row`

立即生效（A 语义）。

### 3.4 版本与 revision

- `EntityLocation.Generation`：实体句柄失效检测
- 每个组件实例有 version（引用有效性）与 revision（修改跟踪，仅 Dense / Sparse）
- 组件删除 → version 失效；Tag 增删只改位图并产生 change 事件

## 4. 实体与引用

### 4.1 EntityLocation（池化）

```csharp
internal sealed class EntityLocation
{
    public Structure Structure;
    public int Row;
    public uint Generation;
}
```

`Entity` 与 `ComponentRef<T>` 共享同一 location 对象；迁移/交换只改 location，所有引用自动跟随。

### 4.2 Entity

- `readonly struct`：`(world, entityId, EntityLocation, generation)`；`entityId` 为值拷贝，不依赖 location 存活
- 相等 / 哈希：`(world, entityId, generation)`（与 v1 一致）
- `IsValid`：location 存活且 generation 匹配
- 写 API 统一三 kind，命名沿用 v1：
  ```csharp
  entity.CreateComponent<Position>(new Position { ... });  // Dense：可能触发迁移
  entity.CreateComponent<Buff>();                          // Sparse：进 Sparse，不迁移
  entity.CreateComponent<Player>();                        // Tag：进位图，不迁移
  entity.DestroyComponent<Position>();
  entity.DestroyComponent<Player>();
  entity.HasComponent<Player>();                           // 三种 kind 通用
  ```
- 不添加 `AddTag` / `AddSparseComponent` 等变形
- `GetComponent<Tag>` 永远返回 `default`（`NotNull == false`），不中断

### 4.3 ComponentRef<T>

- 单点访问语义保留：内部为 `(EntityLocation, generation, TypeId, Version)`
- `RO` / `RW` / `Revision` / `NotNull` 语义不变；`RW` 触发 revision + change 事件
- 有效性 = location generation 匹配 + 组件 version 匹配；迁移与结构内 swap-remove 时引用自动保持有效（v1 语义）
- 批量访问不复用 `ComponentRef`，见 5.3

## 5. 查询与状态跟踪

### 5.1 IEntityMatcher

- `OfAll` / `OfAny` / `OfNone` / `WithMask` 语义不变
- 匹配顺序：结构级粗筛（Dense 集合 + Mask）→ 行级精筛（Tag 位图 / Sparse 存在性）

### 5.2 IEntityQuery（不池化，与 EntityCollector 一致）

```csharp
public interface IEntityQuery : IDisposable
{
    IEntityMatcher Matcher { get; }

    // 快照：Refresh() 后有效，遍历期间稳定
    IReadOnlyList<Structure> Structures { get; }
    IEnumerable<ulong> Entities { get; }

    void Refresh();
}
```

- 创建：`world.Query(matcher)`
- 删除 v1 的 `world.Query(matcher, ICollection<...>)` 重载

### 5.3 Structure 批量访问（SoA）

```csharp
var ro = s.RO<Position>();   // ReadOnlySpan<Position>，不标记
var rw = s.RW<Velocity>();   // Span<Velocity>，获取即标记该结构该类型全部 row revision + change 事件
var ids = s.Entities;        // ReadOnlySpan<ulong>，row → entityId
```

### 5.4 Collector

- 用户 API 不变：`Collected` / `Matching` / `Clashing` / `Changed` + `Flush` + `EntityCollectorFlag`
- 内部走结构级加速
- Tag / Sparse 的增删与 revision 变化同样进入 `Changed`（受 `RelatedComponentOnly` 约束）

## 6. 系统调度

### 6.1 模型

- 组 = 纯排序桶，不承载掩码；掩码仍留在系统上（`ISystem.TickGroup`）
- 组可嵌套；系统与组可混排注册；根层级为隐式默认组
- 注册组时指定插入锚定：`Early` = 当前层级最前，`Later` = 追加（默认）

### 6.2 API 草案

```csharp
public enum GroupInsertMode { Early, Later }

world.RegisterGroup("Physics");
world.RegisterGroup("Gameplay", GroupInsertMode.Early);
world.RegisterGroup("Render").After("Gameplay").Before<CleanupSystem>();
world.RegisterSystem<InputSystem>("Gameplay").Before<MovementSystem>();
world.RegisterSystem<MovementSystem>("Gameplay");
world.RegisterSystem<RootLevelSystem>();          // 未指定组 → 根层级
```

- `Before` / `After` 锚点：系统类型或组名，可跨层级
- 约束为声明式：注册先后不影响解析结果；无约束节点之间以注册序作稳定 tie-break；`Early` / `Later` 仅控制同层插入锚点
- 锚点允许前向引用（尚未注册的类型/组名）；Teardown 时统一解析，无法解析的约束记录错误并忽略
- 注册到未注册的组名 → 抛异常

### 6.3 解析与执行

- `TeardownSystems`（`BeginTick`）时：树按注册序展平 → 应用 Before/After 约束 → 拓扑排序 → 生成执行序列
- 成环：`Log.Err` + 回退到展平序，保证可运行
- **tick 内（Update 期间）系统注册图发生更改时，变更统一在下一个 `BeginTick` 应用并重算**：
  - 已注册系统：复用实例，按新顺序重新放置（不重建、不重复 `OnCreate`）
  - 新增系统：此时实例化并调用 `OnCreate`（DI 解析依赖）
  - 已注销系统：及时销毁（调用 `OnDestroy`）并从执行序列移除
  - 当前 tick 内已排定的执行序列不受影响；`Tick` 执行期间不重建序列

## 7. World 合并与生命周期

- 删除 `MinimalWorld`，`World` 成为唯一入口
- 核心 managers 内置；`OnRegister` 仍可注册自定义 manager（保留 toolkit 可扩展性）
- 钩子收敛为：
  - `OnRegister`：仅首次 `Startup` 调用（注册 managers / 服务）
  - `OnSetup`：每次 `Startup` 调用（可自行判断是否首次）
  - `OnCleanup`：每次 `Shutdown` 调用
- tick 三段保留：`BeginTick`（Teardown/排序）→ `Tick(mask)`（执行）→ `EndTick`（Cleanup/重排）
- 原 `OnTickBegin` / `OnTick` / `OnTickEnd` 虚钩子取消，由 `World` 内部驱动

## 8. CommandBuffer

```csharp
using var cmd = world.CreateCommandBuffer();
var e = cmd.CreateEntity(mask);
cmd.CreateComponent<Position>(e, new Position { X = 1 });
cmd.CreateComponent<Buff>(e);
cmd.CreateComponent<Player>(e);
cmd.DestroyComponent<Position>(e);
cmd.SetMask(e, 0b01);
cmd.DestroyEntity(e);
cmd.Playback();   // 按记录顺序立即应用；Playback 后可复用（清空记录）
                  // 未 Playback 直接 Dispose = 丢弃记录并释放资源
```

- 手动 `Playback`，支持 `Dispose`
- 记录期间零结构迁移；Playback 时批量应用
- `CreateEntity` 返回 CommandBuffer 内部占位实体，Playback 时解析为真实实体
- 用于大量数据变更场景

## 9. 分阶段交付

| 阶段 | 内容 | 验收 |
|---|---|---|
| 1 | 内核：三接口 + `ComponentTypeRegistry` + `Structure`/SoA + `EntityLocation` + 迁移 + `ComponentRef` 适配 + 现有测试迁移 | `dotnet build` / `dotnet test` 全绿 |
| 2 | CommandBuffer | 新增测试全绿 |
| 3 | 查询：`IEntityQuery` + `s.RO/RW<T>()` + collector 结构级加速 | 新增测试全绿 |
| 4 | 调度：`RegisterGroup` / `Before` / `After` / 拓扑排序 | 新增测试全绿 |
| 5 | World 合并与生命周期收敛 | 新增测试全绿 |
| 6 | 文档：README / QUICK_START 中英文更新 | 文档评审通过 |

## 10. v1 → v2 映射

| v1 | v2 |
|---|---|
| `EntityGraph` | `EntityLocation` + `Structure` 行 |
| `ComponentStore<T>` | `Structure` 内 SoA 数组 / `SparseComponentContainer` |
| `ComponentManager` | 组件类型注册 + Structure 管理 |
| `EntityManager` | `entityId → EntityLocation` 注册表 |
| `EntityMatchManager` | collector 管理（结构级加速） |
| `MinimalWorld` | 删除，并入 `World` |
| `EntityMatcher` | 保留，匹配加速 |
| `IComponent<T>` | 保留，语义 = Dense |

## 11. 兼容性说明

- 存量组件定义零改动
- `CreateComponent` / `DestroyComponent` / `GetComponent` / `HasComponent` 命名保留
- 破坏性变更：
  - `world.Query(matcher, ICollection<...>)` 删除（改用 `IEntityQuery`）
  - `MinimalWorld` 删除
  - `EntityGraph` / `ComponentStore<T>` 等内部类型不再公开

## 12. 已决事项

1. 存储模型：archetype（Structure = Dense SoA 数组 + `SparseComponentContainer` + `TagContainer`），Structure 存放在注册表中
2. 归属规则：仅 Dense（`IComponent<T>`）与 `Mask` 决定 Structure；Sparse / Tag 不影响
3. 接口层级：`ISparseComponent<T>` 与 `ITagComponent<T>` 继承 `IComponent<T>`；严禁非泛型
4. 引用稳定性：`EntityLocation` 池化锚点，迁移/交换自动重定位
5. 结构变更立即生效 + CommandBuffer 显式批量
6. 查询：`IEntityMatcher` + `IEntityQuery`（非池化、`Dispose`、`Refresh` 快照、`IEnumerable<ulong> Entities`）
7. 批量访问：`s.RO<T>()` / `s.RW<T>()` 返回 Span；`RW` 获取即整结构标记
8. 系统调度：组为纯排序桶、可嵌套、Early/Later 插入锚定、Before/After 跨层级 DAG、Teardown 拓扑排序；tick 内注册图变更在下一个 `BeginTick` 统一重算（复用已有实例、创建新增、清理注销）
9. World 生命周期：`OnRegister`（首次）→ `OnSetup`（每次）→ `OnCleanup`（每次）
10. 统一写 API：三种 kind 共用 `CreateComponent` / `DestroyComponent`，无 Tag 变形
11. `GetComponent<Tag>` 返回 `default`，不中断
12. 交付方式：渐进式，内核先行；双目标框架保持不变
