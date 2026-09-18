# CoreECS：Entity 辅助扩展与实体级 IsMatch

- 日期：2026-09-18
- 状态：待实现
- 目标：补齐 Entity/Component 常用辅助扩展，开放 Entity internal 访问面
- 关联：`Kernel/EntityExtension.cs`、`Kernel/EntityMatcherExtension.cs`、`Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`、`docs/QUICK_START.md`

## 1. 背景与目标

现状盘点：

- `EntityExtension` 只有 `TryGetComponent` / `GetOrCreateComponent`（2 个重载）。
- 拿到组件 `ref` 的唯一入口是 `entity.GetComponent<T>().RO` / `.RW`，写法冗长。
- matcher 只能用于 query / collector，没有单实体判定入口。
- `Entity` 的 `m_location` / `m_generation` / `m_componentManager` / `Orchestrator` 全部 private，程序集内其他类型无法直接复用内核能力。

本轮目标：

1. ref 直达：`entity.GetRO<T>()` / `entity.GetRW<T>()`。
2. 安全读取：`TryReadComponent<T>` / `ReadComponent<T>`（值拷贝）。
3. fail-fast：`RequireComponent<T>()`。
4. 条件删除：`TryDestroyComponent<T>()`。
5. 实体级 matcher 判定：`matcher.IsMatch(entity)`，语义与 query / collector 完全一致。
6. Entity 开放 internal 访问面，方便程序集内互调与后续扩展。

不影响任何现有 public API 的签名与行为（纯增量 + private → internal）。

## 2. 范围

### 2.1 本轮实现

- Entity internal 访问面（见第 3 节）。
- `EntityExtension` 新增 6 个扩展方法（见 4.1）。
- `EntityMatcherExtension` 新增 `IsMatch`（见 4.2）。
- `RefInvalidatedByStructuralChangeAnalyzer` 识别 `GetRO` / `GetRW` 作为 ref 来源（见第 6 节）。
- 单元测试、分析器测试、QUICK_START 中英文档更新（见第 7、8 节）。

### 2.2 明确不做（讨论后砍掉）

- `Entity.Destroy()`：直接用 `World.DestroyEntity(entity)`，使用频率低。
- `Entity.SetComponent<T>()`（upsert）：现有 `CreateComponent` / `GetOrCreateComponent` 已覆盖主要场景。
- 多泛型 `HasAllComponents<T1..Tn>()` / `HasAnyComponent<T1..Tn>()`：没有新增表达力，且与 `HasComponent<T>()` 链式组合、matcher 语义重叠；实体级判定统一走 `IsMatch`。
- `DestroyComponents()`（清空组件）：tag 无法通过 public API 枚举，语义容易误导，需求低。
- `Structure.RO<T>()` / `RW<T>()` 别名：只修正文档，不新增 API。

## 3. Entity internal 访问面

```csharp
internal EntityLocation Location => RequireLocation();   // 校验存活，无效抛异常
internal EntityLocation RawLocation => m_location;       // 不校验，供探活类操作
internal uint Generation => m_generation;
internal IWorld RawWorld => m_world;                     // 不校验；public World 对 default 实体抛异常
internal ComponentManager ComponentManager => m_componentManager;
internal ComponentOrchestrator Orchestrator { get; }     // 由 private 改为 internal
```

设计原则：

- 仅程序集内可见，NuGet 包消费者的 public 面不变。
- 扩展方法可使用 internal 成员，但"语义正确性优先于微优化"：能用公开方法清晰表达的仍走公开方法，只有公开 API 表达不了的（如 `IsMatch` 需要 Structure/row）才依赖 internal。
- `Location` / `RawLocation` 返回的是池化 `EntityLocation`，调用方只能即时读取，禁止缓存实例（沿用其类型注释的约定）。
- `EntityExtension` 的类注释从 "built on the public Entity API" 更新为 "built on the Entity API (public + internal)"。

## 4. API 设计

### 4.1 EntityExtension（全部为扩展方法）

| 方法 | 返回 | 语义 |
|---|---|---|
| `GetRO<T>()` | `ref readonly T` | 组件须存在且非 tag；只读，不动 revision |
| `GetRW<T>()` | `ref T` | 组件须存在且非 tag；取用时 bump revision + 触发变更事件（与 `ComponentRef.RW` 完全一致） |
| `TryReadComponent<T>(out T value)` | `bool` | 值拷贝；存在 → true + 当前值；tag → true + `default`（对齐 `TryGetComponent`）；缺失 → false |
| `ReadComponent<T>()` | `T` | 值拷贝；缺失或 tag 抛异常 |
| `RequireComponent<T>()` | `ComponentRef<T>` | fail-fast 版 `GetComponent`：缺失抛异常；tag 返回 default ref（存在性语义与 `GetComponent` 一致） |
| `TryDestroyComponent<T>()` | `bool` | 存在（含 tag）则销毁并返回 true；缺失返回 false |

异常语义：

- 缺失：`InvalidOperationException($"Entity {entity.EntityId} does not have component {typeof(T).Name}.")`
- tag 无数据：`InvalidOperationException($"Tag component {typeof(T).Name} carries no data.")`
- 无效/已销毁实体：由 `RequireLocation` 抛出 `InvalidOperationException("Entity has already been destroyed.")`，与现有 `HasComponent` 等一致。

tag 与缺失的统一语义表：

| 入口 | 缺失 | tag 存在 | dense/sparse 存在 |
|---|---|---|---|
| `TryGetComponent`（现状） | false + default ref | true + default ref | true + live ref |
| `TryReadComponent`（新） | false + default | true + `default` | true + 值拷贝 |
| `ReadComponent`（新） | 抛缺失 | 抛 tag 无数据 | 值拷贝 |
| `RequireComponent`（新） | 抛缺失 | default ref | live ref |
| `GetRO` / `GetRW`（新） | 抛缺失 | 抛 tag 无数据 | ref / ref readonly |

### 4.2 EntityMatcherExtension.IsMatch

```csharp
public static bool IsMatch(this IEntityMatcher matcher, Entity entity)
```

- `matcher` 为 null 抛 `ArgumentNullException`（沿用 `Assertion.ArgumentNotNull`）。
- 取 `entity.Location`（internal，无效实体抛异常，与 `HasComponent` 一致）。
- 判定：`(matcher.EntityMask & location.Structure.Mask) != 0UL && matcher.ComponentFilter(location.Structure, location.Row)`。
- 必须做 mask 相交检查，不能只调 `ComponentFilter`（与 `EntityQuery.Refresh` 的现有逻辑一致）。
- 自定义 `IEntityMatcher` 实现同样适用；零分配，热路径应缓存 matcher 实例。
- 语义与 query / collector 完全一致；允许用 `EntityMatcher.With`（空条件）判定恒真。

## 5. 实现要点

- `GetRO` / `GetRW` 走 `Orchestrator.GetComponentRef<T>(entityId)` 一次取 core；返回 null 时再用 `HasComponent<T>` 区分"缺失"与"tag"两种错误（仅错误路径二次查询）。
- RW 的 ref 返回需先落入局部变量再返回：

```csharp
public static ref T GetRW<T>(this Entity entity) where T : struct, IComponent<T>
{
    var core = entity.Orchestrator.GetComponentRef<T>(entity.EntityId);
    if (core == null) throw MissingOrTag<T>(entity);
    var componentRef = new ComponentRef<T>(core);
    return ref componentRef.RW;
}
```

- `ref readonly` 版本可直接对临时量取 `.RO`，但为统一风格同样先落局部。
- `RequireComponent` / `ReadComponent` / `TryReadComponent` / `TryDestroyComponent` 组合现有 public API 实现，不复制内核校验逻辑。
- `IsMatch` 使用 `entity.Location`；无效实体抛异常而非返回 false（避免静默掩盖生命周期 bug）。
- Kernel `LangVersion 12`、目标 `net8.0` + `netstandard2.1`：`ref` 返回扩展方法与 `ref readonly` 已验证可编译。

## 6. 分析器改动

`Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`：

- `TryResolveSource` 增加对 `EntityExtension.GetRO/GetRW` 调用的识别，作为新的 ref 来源：

```csharp
private static bool IsEntityRefAccessor(IMethodSymbol method)
{
    if (method.Name != "GetRO" && method.Name != "GetRW") return false;
    if (method.ContainingType?.ToDisplayString() != "CoreECS.EntityExtension") return false;
    return method.ReturnsByRef || method.ReturnsByRefReadonly;
}
```

- 诊断文案的 origin 显示为 `Entity.GetRO<T>()` / `Entity.GetRW<T>()`（与调用点写法一致）。
- 不改 `UnsafeMethodIndex`：`GetRO` / `GetRW` 自身不调用结构变更 API，它们只是新的 ref 来源，不是 unsafe 方法。
- 若不同步识别，`ref readonly var p = ref entity.GetRO<T>(); ...结构变更...; 用 p` 将漏报 ECS0001/ECS0002。

## 7. 测试计划

`Test/EntityExtensionTestUnit.cs` 扩充（复用现有 `PositionComponent` / `ManaComponent` / `PlayerTag`）：

- `GetRO` / `GetRW`：dense 与 sparse 的读、写、`Revision` 变化（RW 取用即 +1，RO 不变）。
- `GetRO` / `GetRW`：缺失抛缺失异常；tag 抛 tag 异常；无效实体抛异常。
- `TryReadComponent`：dense / sparse 拷贝值；tag true + default；缺失 false。
- `ReadComponent`：正常拷贝；缺失 / tag 抛异常。
- `RequireComponent`：正常返回 live ref；缺失抛异常；tag 返回 default ref。
- `TryDestroyComponent`：存在（dense/sparse/tag）true 且后续 `HasComponent` false；缺失 false。

`Test/EntityMatcherTestUnit.cs`（或就近新增文件）扩充 `IsMatch`：

- `OfAll` / `OfAny` / `OfNone` 命中与不命中；`With` 空条件恒真。
- mask 过滤：`WithMask` 不匹配时返回 false。
- 无效实体抛异常。

分析器测试（`Test/RefInvalidationAnalyzerTestUnit.cs` + `Test/AnalyzerTestSource.cs`）：

- `ref readonly var p = ref entity.GetRO<T>();` 后接结构变更并使用 `p` → ECS0001。
- `ref var p = ref entity.GetRW<T>();` 后接结构变更 → ECS0001。
- ref 参数在使用前发生结构变更 → ECS0002。
- ref 使用完毕后再结构变更 → 不报。

验证命令：`dotnet build` + `dotnet test`。

## 8. 文档

- `docs/QUICK_START.md` / `docs/QUICK_START.zh-CN.md` 第 5 节 "Extension helpers" 增补新方法与 `IsMatch` 示例。
- 修正文档与实现不一致处：`structure.RO<T>()` / `s.RO<T>()` 实际不存在，改为 `GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()`；`world.Query(matcher)` 实际为 `world.CreateQuery(matcher)`（`README.md`、`README.zh-CN.md`、`docs/QUICK_START*.md` 同步）。

## 9. 风险与取舍

- `GetRW` 取用即 bump revision 并可能触发 changed 事件，与 `ComponentRef.RW` 一致；文档需强调"只在确实要写时才取 RW，只读用 RO"。
- internal 面扩大耦合：仅程序集内可见，包消费者不受影响，但后续重构需兼顾这些内部调用点。
- 分析器与 API 必须同步落地，否则新入口漏报 ref 失效诊断。
- 多泛型 HasXXX 被砍后，实体级组合判定统一走 `matcher.IsMatch`；需要在热路径多次复用时缓存 matcher 实例。
