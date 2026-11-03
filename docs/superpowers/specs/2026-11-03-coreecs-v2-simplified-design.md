# CoreECS v2 简化重构设计

- 日期：2026-11-03
- 目标分支：`v2-pure`
- 基线：当前 `master` 语义与实现
- 参考：旧 `v2` 分支的行为、测试与性能经验；EnTT 的 typed sparse-set、分页存储和 pay-for-what-you-use 原则
- 状态：已批准（2026-11-03；后续 Tag、分页、Collector 水位线与 Revision 类型修订均经确认）

## 1. 意图

CoreECS v2 不是把旧 `v2` 分支移植回来，也不是制作 C# 版 EnTT。

本次工作从当前分支重新实现一次破坏性 v2：保留 CoreECS 的 state-first 特征（稳定组件句柄、revision、Collector、阶段发布、系统 DAG 和可嵌入 World），删除旧 v2 为 archetype、CommandBuffer、分析器、复杂事件重入和多组件类别付出的过度设计；同时吸收 EnTT 在 typed storage、分页内存、稀疏索引、查询候选选择和按需付费方面的经验。

最终目标是一个更小、更容易推理、热路径更短、性能可以持续测量的 CoreECS，而不是功能最多的 ECS。

## 2. 产品定位

CoreECS 继续定位为：

- 面向 C# 游戏逻辑的 state-first ECS 工具包；
- 适合卡牌、RPG、Buff、任务、UI 状态和其他需要阶段变化追踪的业务；
- 可以独立使用，也可以与高吞吐模拟 ECS 共存；
- 单线程、显式生命周期、可预测阶段边界；
- 不以超过 EnTT、Unity Entities 或其他高吞吐 ECS 的绝对性能为宣传目标。

性能成功标准是消除可确认的结构性瓶颈，并用可重复 Benchmark 防止回退。

## 3. 目标

1. 使用 typed paged sparse-set 作为统一组件存储。
2. 一个实体最多拥有一个同类型组件。
3. 保留可跨扩容、延迟压缩和 tick 边界继续有效的 `ComponentRef<T>`。
4. 保留 revision 与 `.RW` 自动标记语义，同时让高频 RW 写路径不随 Collector 数线性增长。
5. 保留 `Collected` / `Matching` / `Clashing` / `Changed` / `Flush()` 的 state-first Collector。
6. Query 创建时立即产生当前世界的稳定 `EntityId` 快照，并可显式 `Refresh()`。
7. 保留旧 v2 的系统注册 DAG：组、嵌套组、前后约束、前向引用和稳定拓扑排序。
8. 删除 `MinimalWorld`，收敛为单一、可继承、可重启的 `World`。
9. 同时支持 `net8.0` 与 `netstandard2.1`。
10. 发布的 NuGet 使用默认 `uint` EntityId；源码构建可通过 `COREECS_ULONG_ENTITY` 选择 `ulong` EntityId。
11. 建立独立 BenchmarkDotNet 项目，并在 CI 中验证两种 EntityId 编译模式。

## 4. 非目标

v2 首版明确不实现：

- CommandBuffer；
- Roslyn ref 安全分析器；
- archetype / Structure / chunk 存储；
- 旧 v2 的 Sparse 组件类别；
- Typed multi-component Query；
- 公开 `ComponentStorage<T>`、分页数组或 Span 批量存储 API；
- owning group；
- 组件排序；
- 自定义或可替换 storage；
- 自动并行系统调度；
- 序列化、世界快照或网络复制；
- 公共 component/entity add/remove/change 事件；
- 同实体同类型多组件实例；
- 跨线程 World 读写；
- 对 EnTT API 或实现细节的刻意模仿。

后续真实 workload 和 Benchmark 可以触发这些能力的独立设计，但不得在本次重构中预留复杂的半成品抽象。

## 5. 反目标

以下结果即使功能可运行也视为失败：

- 为兼容 master 或旧 v2 而保留大量适配层；
- 重新引入旧 v2 的 orchestrator、Structure cache、三组件类别或 ref analyzer；
- 高频 `.RW` 按 Collector 数同步扇出；
- Query 对所有请求都扫描全部实体；
- 每个组件实例除已确认的 handler 外再产生额外托管对象；
- 每个实体分配独立组件 List；
- 测试锁定私有容器形态而不是语义或复杂度不变量；
- 用不稳定的 Stopwatch 毫秒阈值作为跨机器 CI 性能门禁；
- 让旧 v2 的测试数量决定新设计范围。

## 6. 已考虑的实施路径

### 方案 A：当前实现内纵向替换（选定）

在当前 `v2-pure` 分支和唯一产品实现中，按依赖顺序逐层替换。每个切片同步迁移相关测试，必要时只保留短命 internal adapter。

优点：始终只有一套产品实现；不会形成第二套长期架构；符合“从当前分支重做”的目标。

代价：前几阶段需要谨慎安排 API 破坏和测试迁移。

### 方案 B：并行 Next 内核后一次切换（未选）

新建临时项目/namespace，完成后一次替换旧实现。

未选原因：会临时维护两套 World/Entity/Component API，最终仍需要大爆炸切换。

### 方案 C：旧 v2 做减法再移植（未选）

以旧 v2 为代码起点，删除不需要的能力。

未选原因：旧 v2 的 handler、matcher、collector 和 Structure 深度耦合，做减法很容易保留原有过度设计。

## 7. 项目与构建

### 7.1 项目布局

最终 solution 包含：

```text
Kernel/       CoreECS 库
Test/         NUnit 功能、性质与 internal 诊断测试
Benchmark/    BenchmarkDotNet（net8.0）
docs/         用户文档、设计与迁移指南
```

Rider 已在当前工作树完成 `ECS/` → `Kernel/` 的主要移动：

- `CoreECS.sln` 已引用 `Kernel/Kernel.csproj`；
- `Test/Test.csproj` 已引用 Kernel；
- 公开 assembly、root namespace 与 NuGet PackageId 继续是 `CoreECS`。

实施第一步还需处理：

- workflow、`AGENTS.md`、README 中残留的 `ECS/` 路径；
- `Kernel.csproj` 重复 `PackageId`；
- Rider/旧项目留下的构建缓存；
- 项目文件末尾换行等机械卫生问题。

这些属于当前用户重命名工作的收尾，不改变本设计。

### 7.2 目标框架与语言

- Kernel：`net8.0;netstandard2.1`
- Test：`net8.0`
- Benchmark：`net8.0`
- 不固定 SDK 版本；删除 `global.json`，任何能够构建上述目标框架与 C# 9 的兼容 SDK 均可
- `LangVersion`：固定 C# 9，以满足 Unity 源码兼容要求
- Kernel 与 Test 启用 Nullable
- Kernel 保持 `AllowUnsafeBlocks`，仅在确有热路径或兼容实现需要时使用

### 7.3 EntityId 编译模式

默认构建：

- 底层值为 `uint`；
- 20-bit index + 12-bit generation；
- 约 1,048,575 个 index（保留无效值后略少）；
- generation 跳过 0，回绕后继续使用。

定义 `COREECS_ULONG_ENTITY`：

- 底层值为 `ulong`；
- 32-bit index + 32-bit generation；
- index 与 generation 的公开属性类型同步扩大。

`EntityId` 是 public readonly struct：

- 默认值 0 无效；
- live index 从 1 开始；
- 相等性与哈希由 index + generation 决定；
- 不包含 World 标识；
- 不向用户承诺内部 bit mask 或原始整数值是持久化格式。

NuGet 只打包默认 uint 模式。CI 对 uint 与 `COREECS_ULONG_ENTITY` 两种模式运行同一套测试；ulong 使用方需自行编译源码。

Component revision 使用同一个条件符号：默认模式下 `ComponentRef.Revision` 与 handler revision 为 `uint`；`COREECS_ULONG_ENTITY` 模式下同步为 `ulong`。revision 初始为 0，每次 RW 后递增并在回绕时跳过 0。journal sequence/epoch 始终使用 ulong，不随 EntityId 模式改变。

## 8. 组件契约

### 8.1 数据组件

```csharp
public interface IComponent<T>
    where T : struct, IComponent<T>
{
    void OnCreate(EntityId entityId);
    void OnDestroy(EntityId entityId);
}
```

- 两个 Hook 没有默认实现；所有组件必须显式实现。
- Hook 只接收 `EntityId`，不接收 World、Entity、manager 或 context。
- Hook 用于初始化/清理组件自身，不用于任意结构操作。
- Hook 内禁止任何实体组件结构修改。
- Hook 异常由框架捕获并记录；结构操作继续完成，不做事务回滚。

调用时点：

- `OnCreate`：初值已经写入真实 storage 后，对 storage 中的实例调用；Hook 对自身字段的修改必须保留。
- `OnDestroy`：逻辑失效与 payload 清理之前，对 storage 中的实例调用。
- 实体拥有多个组件时，不保证跨组件类型的 OnDestroy 顺序；每个 Hook 恰好调用一次。

### 8.2 Tag 组件

```csharp
public interface ITagComponent<T> : IComponent<T>
    where T : struct, ITagComponent<T>
{
    void IComponent<T>.OnCreate(EntityId entityId) { }
    void IComponent<T>.OnDestroy(EntityId entityId) { }
}
```

- Tag 是显式 marker，不通过运行时反射自动猜测。
- 注册时验证 T 没有实例字段；不符合时抛配置错误。
- `ITagComponent<T>` 通过 C# 9 default interface implementation 显式满足 `IComponent<T>` 的两个 Hook，因此 Tag struct 不需要手写空方法。
- C# 不允许给派生接口中的显式基接口实现添加 `sealed`。框架通过已解析的 Tag storage kind 保证永远不调用 Tag Hook；即使 Tag struct 自己声明同名方法，dispatcher 也必须跳过。
- 自引用泛型约束保证 Tag 类型与 Hook 签名中的 T 一致。
- Tag 与普通组件使用同一套 Create/Destroy/Has/Get API。
- Tag storage 只保存 membership、handler、EntityId 和 revision 等元数据，不保存每实体 T payload。
- `GetComponent<Tag>()` 返回有效 `ComponentRef<Tag>`。
- Tag RO 返回该 T 的共享 default；RW 先增加当前实体 Tag 的 revision/Changed，再返回共享 default。
- Tag 的 OnCreate/OnDestroy 永远不被框架调用。

v2 只有“数据组件”和“Tag 的内部存储优化”，没有旧 v2 的 Dense/Sparse/Tag 三套公开操作模型。

### 8.3 组件唯一性与操作

- 一个实体最多拥有一个 T。
- 重复 `CreateComponent<T>` 抛 `InvalidOperationException`。
- 不提供隐式 replace/emplace-or-replace；已有数据通过 ComponentRef.RW 修改。
- `DestroyComponent<T>` 在组件不存在时抛异常。
- `DestroyEntity` 对无效或已销毁 Entity 安全 no-op。
- `CreateComponent<T>(in T initialValue)` 减少大 struct 复制；无参数重载从 default 开始。

保留 helper：

- `TryGetComponent<T>`；
- `GetOrCreateComponent<T>`；
- GetOrCreate 的 bool 延续旧语义：true 表示原本已存在，false 表示本次创建。

公开组件枚举只保留无分配 untyped 形式：

```csharp
int GetComponents(ICollection<ComponentRef> output);
```

删除数组便利重载和所有泛型复数 `GetComponents<T>` API。

## 9. Component TypeId

- 使用进程级、只增不减的 `Type → uint TypeId` 注册表。
- 多个 World 共享 TypeId。
- 通过 `ComponentType<T>` 泛型静态缓存消除热路径字典查找。
- TypeId 不保证跨进程、跨版本稳定，不用于序列化或网络协议。
- 注册表负责记录普通组件或 Tag storage kind。
- World 本身仍为单线程；全局首次类型注册需要最小线程安全保护，避免多个 World 初始化竞态。

## 10. Typed paged sparse-set storage

### 10.1 每类型一个 storage

每个普通组件类型拥有一个 typed storage：

```text
ComponentStorage<T>
├─ sparse pages: EntityId.Index → packed slot + generation validation
├─ component pages: packed slot → T
├─ entity pages: packed slot → EntityId
├─ handler pages
└─ tombstone/deferred removal state
```

普通 storage 的 packed 列只有 T payload、EntityId 和 ComponentHandler 引用。Tag storage 省略 T payload，只保留 EntityId 与 handler。revision、BindGeneration、lastJournalEpoch 和 Entity intrusive-list 链接直接存入 handler，不再建立独立 packed 列或 component version page；EntityId generation 负责实体身份。

### 10.2 分页与 SoA

- payload 与元数据按列拆分，不使用 `Group<T>[]` AoS。
- 同一 storage 的所有 packed 列共享完全相同的页面边界。
- 第 0 页大小为 `2^2 = 4`，第 1 页为 `2^3 = 8`，之后为 16、32、64……。
- 已分配 `n = totalPageCount` 个 packed 页面时，总容量为 `2^(n + 2) - 4`。
- 下一页大小为 `2^(n + 2)`；容量按近似翻倍增长，但旧页面永不搬迁。
- packed slot 可利用 `slot + 4` 的最高有效位 O(1) 算出 page index，再减去该页之前的累计容量得到 page offset。
- 新增组件只追加到当前尾页或新页，不移动既有页面。
- 已分配页面与容量保留到 World Shutdown；EndTick 不主动 shrink，也不提供 public Trim。
- 首版不公开 World 或 per-component page 配置。

### 10.3 稀疏索引

- sparse index 使用固定大小、按需分配的页，避免每种组件为完整 EntityId index 空间分配平坦数组。
- 指数页只用于按 packed slot 连续增长的列，不用于按 EntityId.Index 随机访问的 sparse map；否则单个高 index 会迫使稀有组件分配巨大空页。
- membership、GetComponent 与 ComponentRef 重定位为均摊 O(1)。
- contains 必须同时验证 EntityId generation，不能只比较 index。
- Query 的候选规模使用 live count，而不是包含 tombstone 的 allocated count。

### 10.4 延迟物理删除

调用 Destroy 后：

1. 立即执行 OnDestroy；
2. 立即使组件或实体逻辑失效；
3. 立即从查询和 matcher 语义中消失；
4. 槽位保留 tombstone；
5. tick 内新增只追加，不复用本 tick tombstone；
6. EndTick 批量 swap-remove/compact；
7. 更新 sparse 映射和被搬迁组件的 handler offset；
8. EntityId index 在 EndTick 后才进入 free list。

这保证 tick 内删除不会移动已有组件。分页保证 tick 内扩容也不会移动旧页面。

裸 RO/RW ref 只保证当前 tick 阶段内有效；EndTick 压缩后必须重新获取。用户不应把 ref 保存进 ref struct 或跨 tick 使用。稳定的 `ComponentRef<T>` 可以长期持有。

## 11. Component handler 与 ComponentRef

### 11.1 Handler

- 每个组件实例拥有一个池化 handler。
- storage 槽位持有 handler；Entity 组件目录也由 handler 连接。
- handler 捕获/缓存 typed storage、packed offset、EntityId、TypeId、revision、lastJournalEpoch 与 Entity intrusive-list 链接。
- handler 每次从池绑定时增加 `BindGeneration`。
- `ComponentRef` 捕获 handler 引用和 BindGeneration；回池再利用不会让 stale ref 复活。
- EndTick 搬迁时更新 handler offset。

### 11.2 Entity 组件目录

handler 在同一 Entity 内组成 intrusive 双向链表：

- Entity table 只保存链表头；
- 添加/移除组件目录 O(1)；
- DestroyEntity 只遍历该实体实际拥有的组件；
- 不扫描所有 storage；
- 不为每个 Entity 分配 `List<ComponentHandler>`。

### 11.3 公开 ComponentRef API

保留：

- typed `ComponentRef<T>` 与 untyped `ComponentRef`；
- `NotNull`；
- `EntityId`；
- `Revision`；
- `RO` / `RW`；
- `Inspect`；
- `Typed`；
- typed/untyped 显式或隐式转换。

删除 unsafe/noSafeCheck 类型转换入口；类型转换始终验证。

仍存活组件在页面扩容和 EndTick 压缩后，已有 ComponentRef 必须继续有效且 EntityId 不变。组件逻辑删除后，旧 ComponentRef 立即失效。

### 11.4 RW 与 revision

- 每次取得 `.RW` 都增加 revision，即使用户最终没有修改数据。
- Revision 的公开与内部整数类型跟随 `COREECS_ULONG_ENTITY`：默认 uint，条件模式 ulong；回绕跳过 0。
- `.RO` 不改变 revision。
- 同一 `(EntityId, TypeId)` 在同一 journal pump epoch 中只追加一个 change reason。
- revision 可以增加 N 次，而 Collector Changed 只出现一次。
- 无 revision Collector 时，RW 通过 interest flag 跳过 journal 路径。

## 12. Entity 与 Entity table

`Entity` 是 readonly struct，保存 World 引用和 EntityId。

- EntityId 只在所属 World 内有意义。
- 两个不同 World 可以产生相等的 EntityId。
- Entity 相等性与哈希比较 World 引用 + EntityId。
- `Entity.IsValid` 查询所属 World 的 entity table。
- Entity mask 在创建时指定，创建后不可修改。

Entity table：

- index → 当前 generation、alive/dead、mask、组件 handler 链表头；
- 使用 free list 回收 index；
- index 只在 EndTick 后回收；
- generation 回绕时跳过 0 并继续使用。

World Shutdown/Startup 重启时允许 EntityId table 重置。契约要求用户在 Shutdown 后丢弃所有旧 Entity、ComponentRef、EntityQuery、EntityCollector 和 registration handle；框架不增加 World epoch 来识别跨重启 stale 值。

## 13. Matcher

### 13.1 公开模型

只支持内置 public `EntityMatcher`；删除 public `IEntityMatcher`。

```csharp
EntityMatcher.With
    .OfAll<Position>()
    .OfAny<Velocity>()
    .OfNone<Disabled>();

EntityMatcher.WithMask(mask);
```

- matcher 在第一次绑定到 Query 或 Collector 时 freeze；之后修改抛异常。
- 重复添加同一 term 幂等。
- 空 component matcher 匹配所有 live entities（仍受 mask 约束）。
- `IsRelevantComponent` 对空 matcher 视所有组件类型相关，以保持 RelatedComponentOnly 语义。
- matcher 在 freeze 时把 Type term 解析为 TypeId plan。

### 13.2 Mask

- Entity mask 是普通 immutable 元数据，不参与 storage 分类。
- mask 匹配保持当前“任一 bit 相交”语义：`(matcherMask & entityMask) != 0`。
- 不增加 AnyMask/AllMask 两套 API。

## 14. EntityQuery

`EntityQuery` 是 public sealed class，不实现 IDisposable。

工厂：

```csharp
EntityQuery CreateQuery(
    EntityMatcher matcher,
    bool refreshImmediately = true);
```

公开成员：

- `EntityMatcher Matcher`；
- `IReadOnlyList<EntityId> Entities`；
- `int Count`；
- indexer；
- `void Refresh()`。

语义：

- `refreshImmediately: true` 时，CreateQuery 调用内立即物化当前世界快照。
- false 时初始为空，直到 Refresh。
- 快照在下次 Refresh 前不自动变化。
- 已销毁实体的旧 EntityId 仍留在当前快照中，但 generation 防止它指向同 index 的新实体。
- Query 不订阅 World，不需要 Dispose。
- 结果顺序不稳定，不保证创建顺序。

构建算法：

- OfAll：从 live count 最小的 required storage 驱动候选，再检查其他 all/any/none term。
- OfAny：对相关 storage 进行去重并集，再检查完整 matcher。
- none-only 或空 matcher：扫描 live entity table。
- mask 过滤尽早执行。
- 最终结果物化到 Query 自己可复用的缓冲。

删除：

- 旧 `World.Query(matcher, ICollection<...>)`；
- matcher extension query 包装；
- old-v2 `Structures` 暴露；
- public interface `IEntityQuery`。

## 15. EntityCollector

### 15.1 公开 API

`EntityCollector` 是 public sealed class，实现 IDisposable。

公开成员：

- `EntityMatcher Matcher`；
- `EntityCollectorFlag Flag`；
- `IReadOnlyList<EntityId> Collected`；
- `IReadOnlyList<EntityId> Matching`；
- `IReadOnlyList<EntityId> Clashing`；
- `IReadOnlyList<EntityId> Changed`；
- `void Flush()`；
- `void Dispose()`。

删除 public interface `IEntityCollector` 和 obsolete `Change()`。

flags 保留：

- None；
- RevisionAsChange；
- MatchAsChange；
- ClashAsChange；
- RelatedComponentOnly；
- Default = RevisionAsChange | MatchAsChange | RelatedComponentOnly。

### 15.2 初始状态与 phase

- Collector 创建时扫描当前世界。
- 当前匹配实体进入 pending Matching。
- 第一次 Flush 后才进入 Collected 并通过 Matching 发布。
- 一个 Collector phase 是该 Collector 自己的两次 Flush 之间。
- Collected 是上一次 Flush 后的稳定 membership。
- Matching/Clashing 只发布相对上次 Collected 与本次 Flush 最终世界状态的净变化。
- phase 内 add/remove/add 等抖动不发布中间 membership 历史。
- 如果起点和终点都匹配，但 matcher-relevant 组件实例/组合发生过变化，则仍可按 flags 进入 Changed。
- Matching/Clashing 按 MatchAsChange/ClashAsChange 可同时镜像到 Changed。

### 15.3 中央 journal pump

不采用旧 v2 的“每 Collector cursor 扫同一全局 journal”。采用：

```text
ComponentRef.RW
  → revision +1
  → 同 pump epoch 首次 (EntityId, TypeId) 才 append 全局 journal

任意 Collector.Flush
  → EntityMatchManager.PumpChanges() 只扫描尚未 pump 的 journal 一次
  → TypeId 倒排索引分发 reason 到所有相关 Collector pending
  → 仅调用 Flush 的 Collector 结算并公开发布
```

不在 EndTick 自动 pump。没有 Collector Flush 时，journal 保留；同 epoch 去重使重复 RW 不无限增长。

另一个 Collector 触发 pump 时，当前 Collector 可以收到 pending reason，但其公开 buffers 不变化，直到它自己 Flush。

### 15.4 倒排索引

manager 维护：

- structural subscribers：matcher All/Any/None term 的 TypeId → Collector；
- revision subscribers：RelatedComponentOnly Collector 的 TypeId → Collector；
- wildcard revision collectors：RevisionAsChange 且未启用 RelatedComponentOnly；
- lifecycle-sensitive collectors：空 matcher、none-only 等需要 entity create/destroy 的 Collector。

结构变化相对少：

- add/remove/destroy 不立即执行完整 matcher；
- 只把相关 `(Collector, EntityId)` 标为 pending，并合并 reason flags；
- 每个 Collector 在自己的 Flush 时，对每个 pending EntityId 基于最终世界状态只匹配一次。

### 15.5 Pending 与 membership

- Collected 使用 packed EntityId list + sparse membership index。
- pending 使用 EntityId sparse-set + reason flags。
- journal 与 pending 必须携带完整 index+generation。
- 同一个 EntityId 在一个 Collector phase 中只占一个 pending slot。
- 每次发布使用复用容量的前后缓冲；稳态不产生临时集合分配。

### 15.6 Revision 结算

- journal 只记录 EntityId + TypeId 等最小 reason 数据，不复制 old/new component value。
- Flush 时最终不匹配的实体，不因之前的 revision 进入 Changed。
- 先发生 revision、之后进入 matcher，Flush 时最终匹配，则 revision 可进入 Changed。
- RelatedComponentOnly 按 entry TypeId 与冻结 matcher plan 判断。
- 一个实体多个 relevant TypeId reason 最终只在 Changed 出现一次。

### 15.7 Collector 创建水位线

Collector 保存 CreatedSequence。中央 pump 分发时跳过早于该 sequence 的 journal entry：

- late Collector 不看到创建前的 revision；
- 创建 Collector 不隐式触发 pump；
- 旧 Collector 仍能在后续 pump 收到创建前已有 entry。
- 注册新 Collector 时先执行一次不消费 entry 的 `journal.AdvanceEpoch()`，再捕获 CreatedSequence；这会逻辑失效所有 handler 的旧 lastJournalEpoch，使同一组件在 Collector 创建后的再次 RW 必须追加新 entry。
- 必须同时测试“两个不同实体区分创建前后写入”和“同一实体同一组件在创建前后各写一次”，避免末端 EntityId 去重掩盖水位线/coalescing 错误。

### 15.8 Dispose 与 Shutdown

- Dispose 幂等。
- Dispose 从所有倒排索引和 manager 注册中移除 Collector，并释放/清空 buffers。
- World Shutdown 自动使所有 Collector 失效并注销。
- Shutdown 后再次 Dispose 是 no-op。
- disposed Collector 的其他成员访问抛 ObjectDisposedException。

## 16. 系统 DAG

保留旧 v2 的完整注册能力：

- root 默认组；
- 命名组；
- 嵌套组；
- GroupInsertMode.Early / Later；
- SystemRegistration 与 GroupRegistration；
- system/group Before/After；
- system type 或 group name 锚点；
- 允许前向引用；
- 稳定拓扑排序，无约束节点保持 flatten 注册序；
- cycle：记录错误并对整张图回退 flatten 注册序；
- 未解析锚点：记录错误并忽略该约束；
- group name 在 World 内全局唯一；
- 每个 concrete System type 最多一个实例；
- 重复注册立即抛异常；
- 不提供 UnregisterGroup；
- 可注销具体 System。

### 16.1 缓存与修改时点

- BeginTick 仅在 DAG dirty 时重建执行缓存。
- DAG clean 时不得重复 flatten/toposort/copy。
- 当前 Tick 始终使用 BeginTick 已捕获的执行缓存。
- Tick 期间允许修改下一版 DAG，但不影响本 tick 顺序。
- 同 tick 对同一 System 多次 Register/Unregister/移动组，只应用最终期望状态。
- 从未实例化且最终取消的 System 不调用 OnCreate/OnDestroy。

Tick 外 RegisterSystem：立即通过 DI 实例化并调用 OnCreate，DAG 标脏；下一次 BeginTick 才进入执行缓存。

Tick 内 RegisterSystem：下一次 BeginTick 才实例化并 OnCreate。

Tick 内 UnregisterSystem：当前缓存仍可执行它；tick 结束后 OnDestroy，下一 BeginTick 不再出现。

### 16.2 ISystem

```csharp
public interface ISystem
{
    ulong TickGroup => ulong.MaxValue;
    void OnCreate();
    void OnTick(ulong tickMask);
    void OnDestroy();
}
```

- 三个方法必须实现，无默认空体。
- TickGroup 保留默认 MaxValue。
- OnCreate/OnTick/OnDestroy 异常捕获记录，其他 System 继续；失败 System 不自动注销。
- 只保留 public OnSystemBeginExecute / OnSystemEndExecute 观察事件。
- 事件回调不得修改 DAG；违反时抛清晰异常。
- 事件回调异常捕获记录，不影响 System 执行。
- 删除 public teardown/cleanup 事件。

DAG 只决定顺序，不自动并行执行。

## 17. World 与 manager 生命周期

### 17.1 单一 World

- 删除 MinimalWorld。
- `World` 是唯一入口，public class，可直接实例化也可继承。
- 核心 manager 固定内置，用户不能替换底层存储、匹配与调度 manager。
- 保留自定义 manager 注册和可替换 IInjectionProxy。

World hooks：

```csharp
protected virtual void OnRegister(
    IManagerRegister managers,
    IServiceCollection services) { }

protected virtual void OnSetup() { }
protected virtual void OnCleanup() { }
```

- OnRegister：第一次成功 Startup 的构建过程中调用一次。
- OnSetup：每次 Startup 调用。
- OnCleanup：每次成功 Startup 所对应的正常 Shutdown 调用。

### 17.2 Custom manager

保留旧四阶段 IWorldManager：

- OnManagerCreated；
- OnWorldStarted；
- OnWorldEnded；
- OnManagerDestroyed。

manager 与 DI 实例在一次成功首次 Startup 后跨正常 Startup/Shutdown 周期复用。为了保持旧四阶段契约，复用的 manager 在每个正常周期仍依次收到 OnManagerCreated → OnWorldStarted → OnWorldEnded → OnManagerDestroyed；这里的 OnManagerDestroyed 表示该 World 周期结束，不表示托管对象一定被 GC 回收。每次 Shutdown 清空 entity/component/storage/collector/system DAG 运行状态；下一次 OnSetup 重新注册 group/system。

### 17.3 Startup 回滚与重试

Startup 是事务边界：

- OnRegister、service/DI 构建、manager 创建/启动或 OnSetup 任一步异常，Startup 向上传播失败；
- 框架逆序停止本次已启动 manager，销毁本次新建 manager/DI/注册状态，并恢复为可重试状态；
- 第一次 Startup 未完整成功时，不记为“已成功注册”，下一次从 OnRegister 重新开始；
- 已经经历过成功生命周期的 World 在后续 OnSetup 启动失败时，回滚本周期 started 状态并允许再次 Startup；既有首次注册与 DI/manager 实例可继续复用；
- 只有 OnSetup 完整成功后，正常 Shutdown 才调用对应 OnCleanup；OnSetup 中途抛异常时框架不调用 OnCleanup，OnSetup 必须自行清理其失败路径；
- 回滚 manager 时产生的异常记录后继续回滚，Startup 最终重新抛出最初失败异常；
- 用户 Hook 可能产生的外部副作用不由框架回滚，文档要求 Hook 自身可安全重试。

World 状态顺序严格：非法的重复 Startup、嵌套 BeginTick、未 BeginTick 的 Tick/EndTick 等抛 InvalidOperationException。Startup 失败是唯一允许重试的初始化路径。

### 17.4 重启

- 同一 World 支持正常 Startup → Shutdown → Startup。
- Shutdown 调用 OnCleanup、OnWorldEnded，销毁 systems、失效 Collectors、清空 DAG、entities、storages 和本周期数据。
- group/anchor/system registration 不跨周期保留。
- EntityId table 可重置。
- 所有旧对象必须由用户丢弃；不增加 lifecycle epoch。

## 18. 异常、日志与线程模型

- Component Hook：捕获记录，结构操作继续。
- System lifecycle/tick：捕获记录，其他系统继续。
- System Begin/End 观察事件：捕获记录，系统继续。
- World Startup：异常向上传播并回滚。
- API 契约错误（重复组件、非法生命周期、重复系统/组、disposed 对象）：抛明确异常。
- World、storage、Query.Refresh、Collector.Flush、DAG 修改全部为单线程模型。
- 不在 v2 内部为 World 操作加入锁。

## 19. 测试策略

### 19.1 测试迁移原则

- 迁移 master 中仍成立的行为测试。
- 从旧 v2 选择验证保留能力的测试。
- 删除锁定 archetype、Structure cache、CommandBuffer、Analyzer 或旧性能常量的测试。
- 不以旧 v2 全量测试数量为目标。
- 使用 `InternalsVisibleTo(Test)` 暴露只读诊断计数，验证算法量级，不反射私有集合。

### 19.2 Collector 已审计变化

四个现有测试需修改净 membership 期望：

- `EntityCollector_ClashThenMatchBetweenFlushes_KeepsCollectedAndDedupsChanged`
- `EntityCollector_OfNone_BounceForbidden_DedupsChanged`
- `EntityCollector_OfAllAndOfNone_StillMatchedChanges_MarkChangedOnce`
- `EntityCollector_CombinedMatcher_StillMatchedMixedChanges_DedupChanged`

新的期望：起点和终点均匹配时，Matching/Clashing 为空；matcher-relevant 结构变化仍可使 Changed 包含实体。

旧 v2 `CollectorAccelerationTestUnit` 中锁定 StructureEvaluationCount/per-Structure cache 的测试重写为 pending EntityId 每次 Flush 只匹配一次的性质测试。

旧 `PerformanceBaselineTestUnit` 的固定数值基线删除，改由 BenchmarkDotNet 和 internal 复杂度计数覆盖。

### 19.3 Collector 最小性质测试

必须覆盖：

1. 创建扫描与第一次 Flush；
2. 第一次 Flush 前最终不匹配；
3. unmatched add/remove 抖动；
4. matched remove/add 抖动，membership 不变但 relevant composition Changed；
5. 净进入与净退出；
6. N 次 RW：revision +N、journal reason 1 条、Changed 1 条；
7. write → exit；
8. write → enter；
9. 两个 Collector 反向 Flush；
10. `write W1 → C1.Flush → write W2 → C2.Flush → C1.Flush`；
11. late Collector CreatedSequence，用两个实体区分创建前后写入；
12. 同一实体同一组件在 late Collector 创建前后各 RW 一次，创建后写入不得被旧 epoch 合并；
13. EndTick 不 pump；
14. TypeId 倒排索引不触达无关 Collector；
15. Dispose 幂等；
16. Shutdown 后 Collector 自动失效且 Dispose 安全；
17. full EntityId generation 防止旧事件污染复用 index。

## 20. Benchmark 策略

新增独立 BenchmarkDotNet 项目，默认 net8/uint EntityId，覆盖：

- Entity 创建、逻辑销毁、EndTick 回收；
- Component Add/Get/RO/RW/Remove；
- ComponentRef 缓存访问；
- page 扩容与 EndTick compact；
- Query 常见 component 与稀有 component term；
- All/Any/None 与空 matcher；
- Collector 数 0/1/100/1000 下的 RW；
- 同一组件重复 RW 的 journal epoch 合并；
- 首个中央 pump 与后续 Collector Flush；
- 少量/大量相关 fanout；
- 大量不相关 Collector；
- DAG dirty BeginTick 与 clean BeginTick；
- World Startup/Shutdown/restart。

验收：

- RW 写端不随 Collector 数直接线性扫描；
- 同 pump epoch `(EntityId, TypeId)` journal reason 为 1；
- 不相关 Collector 不接收 pending reason；
- Query 稀有 All term 从最小 pool 驱动；
- DAG clean 时不重建缓存；
- 代表性 workload 不慢于 master；
- 记录旧 v2 可复现 workload 数据用于参考，但不要求逐项超过。

CI 不使用固定纳秒/毫秒门槛；CI 通过功能测试和 internal 诊断计数锁定复杂度不变量。

## 21. 文档交付

同步重写：

- README.md；
- README.zh-CN.md；
- docs/QUICK_START.md；
- docs/QUICK_START.zh-CN.md；
- 新增 v1 → v2 破坏性迁移指南。

不迁移旧 v2 的超长实施计划作为用户文档。新设计文档和后续实施计划保留在 `docs/superpowers/`。

文档必须明确：

- state-first 定位；
- EntityId 两种源码构建模式；
- ref 只在 tick 内有效，ComponentRef 可长期持有；
- Query 是快照，Collector 是长期阶段观察器；
- Collector 只在 Flush 发布；
- Mask immutable 且为任一 bit 相交；
- World 单线程但可重启；
- Shutdown 后旧对象必须丢弃；
- 不支持的 v2 non-goals。

## 22. 破坏性变更摘要

- `ECS/` 项目迁移为 `Kernel/`；公开 assembly/namespace/package 仍为 CoreECS。
- target 保持 net8.0 + netstandard2.1，删除 SDK 版本固定。
- EntityId 从 ulong 改为 struct；默认内部 uint。
- 同类型组件从可重复变为唯一。
- IComponent OnCreate/OnDestroy 强制实现，参数改为 EntityId。
- 新增 ITagComponent marker。
- 删除 IEntityMatcher；Query/Collector 接收 EntityMatcher。
- Query/Collector 从 interface 返回改为 public sealed concrete class。
- 删除 Generic plural GetComponents；只保留 untyped non-alloc 输出。
- 删除 MinimalWorld。
- 删除旧 World.Query collection overloads 与 matcher query extension。
- 删除 Collector.Change 别名。
- 删除 entity mask 修改 API。
- 删除 public component/entity change events。
- 删除 unsafe ComponentRef cast。
- Shutdown 后旧对象不再可用。

## 23. 纵向交付切片

具体提交级计划在设计批准后另写。设计层面的顺序是：

1. 收尾 Kernel 重命名、构建矩阵、EntityId 与基础契约。
2. Type registry、handler pool、Entity table、paged sparse-set storage。
3. ComponentRef、Entity 与 World 基础组件生命周期。
4. EntityMatcher 与 EntityQuery。
5. EntityCollector 中央 journal pump、倒排索引与性质测试。
6. System DAG、execution cache 与 tick 内收敛。
7. World 合并、启动回滚、重启、custom manager/DI。
8. Benchmark、双 EntityId CI、文档与迁移指南。

每个切片遵循：失败测试 → 最小实现 → 全量测试 → Release 双 target build。不得先搭完整新内核再一次切换。

## 24. 留给实施计划决定的细节

以下不改变用户语义，由实施计划或 Benchmark 决定：

- sparse index 固定页的具体大小（packed 列的指数页公式已确定）；
- sparse entry 的精确编码；
- journal buffer 的初始容量与增长策略；
- pending reason flags 的内部位布局；
- handler pool 的初始容量；
- internal 诊断计数器的命名；
- Startup rollback 的私有辅助类拆分；
- 文件级目录布局与 internal 类型命名；
- Benchmark 参数规模与 diagnoser 配置。

任何这些细节若要求增加新的公开概念、改变已定语义或恢复 Non-goals，必须回到设计评审。
