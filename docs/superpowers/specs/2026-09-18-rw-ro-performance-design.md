# CoreECS v2 RW/RO 性能优化设计（Plan B：collector 延迟结算）

- 日期：2026-09-18
- 状态：已实现（2026-09-18；执行期修订见第 9 节）
- 关联：PR #17（v2 archetype 内核）、`docs/prompts/roslyn-ref-safety-analyzer-prompt.md`（分析器另开任务）
- 目标分支：`v2`

## 1. 背景

v2 把组件存储重构为 archetype（`Structure`）行式 SoA 后，`ComponentRef<T>.RO/RW` 的单次访问成本明显上升。压测（`Test/StressTestUnit.cs`，`[Category("Performance")]`）实测：

- 无 collector：单次 RW ≈ 152ns（repeated getter 306ns/轮 vs cached ref 154ns/轮）
- 有 revision collector：单次 RW ≈ 256ns，其中 collector 记账 ≈ 100ns
- 非缓存 RO 估计 ≈ 70–110ns

单次 dense RW 目前要经过：

1. `ChangeRevision` → `NotNull`：结构/generation/row 检查 + `HasDense` 二分 + `GetDenseVersion` 二分
2. `ChangeDenseRevision`：再一次 `IndexOfDense` 二分 + revision bump + observer 链
3. `RequireStructure` → `NotNull` 再来一遍
4. `GetDenseRef<T>`：`ComponentTypeRegistry.GetOrRegister<T>()`（`ConcurrentDictionary<Type,...>` 查表）+ `IndexOfDense` 二分
5. 信号链：`Structure.Observer` → `ComponentManager.OnComponentChanged.Emit` → `EntityManager.OnEntityChangeComp.Emit` → 每个 collector 一次匹配/去重（O(collector 数)）

`GetComponent<T>()` 还会每次 `new ComponentRefCore`。

## 2. 目标与验收

### 2.1 性能验收

非缓存访问，同一实体、无结构变更：

| 场景（世界内 collector 数，全部 `RevisionAsChange`） | 指标 | 目标 |
|---|---|---|
| 0 个（无监听） | 非缓存 `RW` / `RO` 耗时比 | < 1.2 |
| 100 个 | 同上 | < 1.5 |
| 1000 个 | 同上 | < 2.0 |

说明：比率 = `RW` 单次耗时 / `RO` 单次耗时，两者都是每次重新调用 `.RO` / `.RW`（不缓存 ref），循环内累加/写入防止 JIT 消除；取多轮 best-of。

Flush 性能：

| 场景 | 定义 | 验收 |
|---|---|---|
| F1 | 1000 个 revision collector，1 个实体每 phase 写 K 次，测 Flush 结算 | 只与合并后的条目数相关（K 次写合并为 1 条） |
| F2 | 1000 个 collector × 1000 个变更实体，测 Flush 结算 | 按 (条目 × collector) 扩展，记录数据 |
| F3 | 同一 workload 的「写入 + Flush」总耗时 | 不高于改造前同 workload 实测总耗时（best-of，容差 ≤ 1.1×） |

### 2.2 功能验收（硬门禁）

- 全量测试通过（当前 547 个）
- **所有 collector 相关测试用例零改动**（`Test/EntityCollectorTestUnit.cs`、`Test/CollectorAccelerationTestUnit.cs` 及 Stress/Integration 中 collector 用例）
- 公共 API 不变；`Matching` / `Clashing` / `Collected` 的语义与时机不变；`Changed` 仅在 5.6 节列出的边界上变化

### 2.3 非目标

- Roslyn 分析器（见 `docs/prompts/roslyn-ref-safety-analyzer-prompt.md`，另开 spec/计划）
- 跨线程并发安全（沿用单线程假设）
- v1 facade 与公开签名变更

## 3. 现状审计：collector 测试与延迟结算的兼容性

已通读 `EntityCollectorTestUnit`（68 例）、`CollectorAccelerationTestUnit` 及 Stress/Integration/World 中所有 collector 用例。结论：

- 没有任何用例 pin 住 5.6 节列出的会变化的边界（"写入后同 phase 内失去匹配且无 `ClashAsChange` 却断言 `Changed` 含该实体"、"写入后才进入匹配且无 `MatchAsChange` 却断言 `Changed` 为空"、"写入后才创建 collector"、"Changed 顺序"）。
- 最接近边界的两个用例因带 `ClashAsChange` / `MatchAsChange` 而结果不变：
  - `EntityCollector_ChangeComponent_MultipleRelevantComponents_BetweenFlushes_DedupChanged`（写 Position 后加 Velocity 破坏匹配，带 `ClashAsChange`）
  - `EntityCollector_DefaultFlags_DeduplicatesMatchingAndRevisionInSamePhase`（加组件 + 写入同 phase，`Default` 含 `MatchAsChange`）
- `StructureBatchAccessTestUnit.RW_MarksEveryLiveRowOnceAndOnlyItsOwnColumn` 与 `StructureTestUnit.RW_BumpsRevisionAndNotifiesObserver` pin 住 **span RW 逐行通知 observer**，因此 span 路径保留逐行 `OnComponentChanged`，不做 bulk 事件（见 5.5）。

实现必须满足以下条件才能保持零改动：

1. 结算发生在 `Flush()` 换缓冲之前；
2. journal 条目保留变更组件类型（`RelatedComponentOnly` 过滤）；
3. collector 创建时记录水位线，不结算创建前的变更；
4. 结算判定与现 `_changeCollector` 的 revision 分支逐条对齐（mask、alreadyCollected、Matches、relevance）；
5. 硬门禁：测试失败只能改实现，不能改 collector 用例。

## 4. 设计

### 4.1 组件引用核心池化（存储槽位持有 + 绑定代数）

现状 `ComponentRefCore` 是每次 `GetComponent`/`CreateComponent` 新建的不可变 class。改为：

- **一组件实例一 core，core 由存储槽位持有**：
  - dense：`Structure` 增加 `ComponentRefCore[][] m_denseCores`（与 `m_denseData` 同构，按 slot/row 对齐）
  - sparse：`SparseStore<T>` 增加 `ComponentRefCore[] m_cores`（按 row 对齐）
  - tag 无 core（无数据）
- **生命周期**：
  - `ComponentOrchestrator.AddDenseComponent/AddSparseComponent` 绑定 core 到目标槽位
  - `Structure.Append` 清理回收行、`SwapRemove` 交换 core 引用、`Grow` 扩容 core 数组、`CopyDenseTo` 复制 core 引用
  - `SparseStore.Set/Remove/RemoveRowSwap/ClearSlot/CopyRowTo` 同步维护 core
  - `RemoveDenseComponentCore` / `RemoveSparseComponentCore` / `DestroyEntity` 释放被移除组件的 core
- **池**：`Kernel/Utils` 新增内部 `ComponentRefCorePool`（单线程栈式池，非 `ConcurrentQueue`）；`Release` 时清空字段避免持有 `EntityLocation` 引用
- **`ComponentRefCore` 变为可变**，字段：
  - `EntityLocation Location`、`uint LocationGeneration`、`uint TypeId`、`ComponentKind Kind`、`uint Version`
  - `uint BindGeneration`：每次绑定 +1，用于识别池复用
  - 快路径缓存：`Structure CachedStructure`、`int CachedSlot`（dense）、`SparseStore CachedSparseStore`（sparse）
- **句柄捕获代数**：`ComponentRef` / `ComponentRef<T>` 增加 `internal readonly uint CoreGeneration`，创建时捕获 `Core.BindGeneration`
- **失效判定**：`NotNull = Core != null && Core.BindGeneration == CoreGeneration && Core.NotNull`。池复用会 bump `BindGeneration`，过期句柄不会"复活"，保持现有 NRE 语义
- **相等性/哈希**：`ComponentRefCoreComparer` 改为 `ReferenceEquals(Core, other.Core) && CoreGeneration == other.CoreGeneration`；默认 ref 相等。有效 ref 语义不变（同一实例的 `CreateComponent`/`GetComponent`/`Typed` 返回同一 core + 同一代数），需保证现有相等性测试全绿
- **`GetComponentRef<T>` 零分配**：解析 slot 后返回槽位 core；若为空（测试或直接构造场景）则从池取并绑定

### 4.2 单次访问热路径

- `ComponentTypeRegistry` 增加泛型静态缓存：

  ```csharp
  private static class Cache<T> where T : struct, IComponent<T>
  {
      public static readonly ComponentTypeInfo Info = GetOrRegister(typeof(T));
  }
  public static ComponentTypeInfo GetOrRegister<T>() where T : struct, IComponent<T> => Cache<T>.Info;
  ```

  消灭热路径的 `ConcurrentDictionary` 查表。
- `Structure` 增加按 slot 的 internal 访问器，全部 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`：
  - `ref T GetDenseRefAt<T>(int slot, int row)`、`uint GetDenseVersionAt(int slot, int row)`、`uint GetDenseRevisionAt(int slot, int row)`
  - `uint BumpDenseRevisionAt(int slot, int row)`（bump 不通知）、`void NotifyChanged(int row, uint typeId)`
- `ComponentRefCore` 快路径：
  - `var s = Location.Structure;`
  - `ReferenceEquals(s, CachedStructure)` 时直接用 `CachedSlot`；否则 `s.IndexOfDense(TypeId)` 并更新缓存（迁移罕见）
  - 行范围 + version 直接数组读取校验
  - sparse 用 `CachedSparseStore` + `((SparseStore<T>)store).Get(row)`
- `RO` / `RW` 使用上述访问器；`RW` 保持"先 bump/发信号、后解析 live location"的既有修复顺序（`Kernel/Defines/ComponentRef.cs`）
- `Revision`、`EntityId`、`NotNull` 同样走快路径

### 4.3 信号链兴趣短路与扁平 relay

现状链路的空转成本高（`Signal.Emit` 的 swap/循环/try-catch）。改为：

- `ComponentManager.KernelObserver.OnComponentChanged`：

  ```csharp
  var entityId = structure.Entities[row];
  if (m_manager.OnComponentChanged.HasReceivers)   // 仅公共订阅者
      m_manager.OnComponentChanged.Emit(entityId, ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
  m_manager.ChangeSink?.OnRevisionChanged(entityId, typeId);   // 内部直连，无 Signal.Emit
  ```

- `ComponentManager` 增加 internal `IComponentChangeSink ChangeSink`（World 在装配时接到 `EntityManager`）
- `EntityManager` 实现 sink：
  - `if (OnEntityChangeComp.HasReceivers) OnEntityChangeComp.Emit(entityId, GetById(typeId).Type, ...)`（公共语义不变，仍逐事件同步）
  - 转发给 `EntityMatchManager.OnRevisionChanged(entityId, typeId)`
- `EntityMatchManager` 不再订阅 `EntityManager.OnEntityChangeComp` 处理 revision（避免 Signal 开销）；add/remove/destroy 仍走现有信号
- 无监听（无公共订阅者、无 collector）时，单次 RW 的链路为：observer 接口调用 + 两个 bool 检查，然后返回
- `EntityMatchManager.OnRevisionChanged`：`if (m_revisionTrackingCollectorCount == 0) return;` 再写 journal

### 4.4 变更日志（journal）与 Flush 结算

`EntityMatchManager` 持有：

```csharp
private struct RevisionEntry { public ulong EntityId; public uint TypeId; public Type Type; }
private readonly List<RevisionEntry> m_journal = new();
private int m_journalBase;                 // 已压缩的头部长度
private readonly List<Collector> m_revisionCollectors = new();   // 仅 RevisionAsChange
```

- **追加与合并**（`OnRevisionChanged`）：
  - 取 `location.PendingRevisionIndex`；若其 ≥ `m_journalBase`、指向的条目 `EntityId` 相同且 `TypeId` 相同，则跳过（同一 (实体, 类型) 每 phase 只记一条）
  - 否则追加 `RevisionEntry`（`Type = ComponentTypeRegistry.GetById(typeId).Type`，只查一次），并更新 `location.PendingRevisionIndex`
  - 不同 (实体, 类型) 追加新条目；重复类型由结算时的 `MarkChanged` 去重
- **每 collector 游标与水位线**：
  - 仅 revision-tracking collector 有 `int JournalCursor`
  - collector 创建时 `JournalCursor = m_journal.Count`（不结算创建前的变更）
  - dispose 时从 `m_revisionCollectors` 移除
- **压缩**：每次结算后取所有 tracking collector 游标最小值 `min`；当 `min - m_journalBase` 超过阈值（如 1024）时 `RemoveRange` 头部并调整 `m_journalBase` 与游标；`PendingRevisionIndex` 的越界/错配由条目校验兜底
- **结算**（在 `Collector.Flush()` 换缓冲之前，仅 tracking collector）：

  ```
  for (i = Cursor; i < m_journal.Count; i++) {
      var e = m_journal[i];
      if (!table.TryGetLocation(e.EntityId, out loc) || loc.Structure == null) continue;  // 已销毁
      if ((Matcher.EntityMask & loc.Structure.Mask) == 0) continue;
      if (HasChangeComponent && !Matcher.IsRelevantComponent(e.Type)) continue;
      var alreadyCollected = (ContainsInBuffer(COLLECTED) || ContainsInBuffer(CHANGE_MATCHING))
                             && !ContainsInBuffer(CHANGE_CLASHING);
      if (!alreadyCollected) continue;
      if (!Matches(loc.Structure, loc.Row)) continue;
      MarkChanged(e.EntityId);
  }
  Cursor = m_journal.Count;
  ```

  与现 `_changeCollector` 的 revision 分支逐条对应。
- **结算去重优化**：`Changed` 去重从 `HashSet<ulong>` 改为 O(1) stamp（每 collector 维护 `uint[] m_changedStamp` + phase epoch，`Flush` 时 epoch+1），降低 F2 的 1M 次结算成本
- **结构变更（add/remove/destroy）保持同步**，`Matching` / `Clashing` / `Collected` 时机不变
- `EntityMatchManager.OnManagerDestroyed` 清空 journal 与列表

### 4.5 span 批量路径

- `GetReadOnlyDenseColumn<T>()`：一次 `Cache<T>` 查表 + 直接返回 span（现状已基本如此，改为静态缓存）
- `GetReadWriteDenseColumn<T>()`：**保留逐行 `Observer.OnComponentChanged`**（测试 pin 住该内部契约），但逐行成本从 O(collector 数) 降为 O(1)（4.3 的短路 + 4.4 的 journal 合并）；不做 bulk 事件
- 逐行写入同一 (实体, 类型) 的 journal 合并、以及 F2 的结算成本，由 4.4 的游标/去重承担

### 4.6 行为边界

判定时点从"写入瞬间"变为"Flush 结算"：

| 同一 phase 内 | 现在 | 本设计 |
|---|---|---|
| 写入后、Flush 前实体失去匹配（迁移/删组件/销毁） | `Changed` 含它 | `Changed` 不含（进 `Clashing`） |
| 写入后、Flush 前实体才进入匹配 | `Changed` 不含 | `Changed` 含（同时进 `Matching`） |
| 写入后、Flush 前才创建 collector | 不受影响 | 水位线屏蔽创建前的变更 |
| 同一实体多次写入 | 一条（去重） | 一条（去重） |

- 顺序：revision 类条目在 Flush 时统一追加，与 match/clash 类条目的相对顺序会变（内容集合在常规路径不变）
- `RelatedComponentOnly`：按条目记录的类型集合判定"任一相关"
- 不变：`Flush` 后读取、去重、`Matching`/`Clashing` 时机、公共 `OnEntityChangeComp` 信号仍逐事件同步、常规路径的 `Changed` 内容

## 5. 测试与基准

### 5.1 新增基准（`Test/StressTestUnit.cs` 或新文件，`[Category("Performance")]`）

- `RoVsRw_NoCollectors` / `RoVsRw_100Collectors` / `RoVsRw_1000Collectors`：非缓存 `.RO` vs `.RW`，best-of 多轮，断言比率目标
- F1 `Flush_OneEntityManyWrites_1000Collectors`：记录并断言合并后条目数
- F2 `Flush_1000Entities_1000Collectors`：记录结算耗时
- F3 `Pipeline_WritesPlusFlush_NotSlowerThanBaseline`：断言总时间 ≤ 改造前基线（基线数字在实现前用当前代码测得并写入测试常量/文档，容差 1.1×）

### 5.2 新增功能测试（新文件，不改现有 collector 用例）

- journal 合并（同实体同类型只记一条；不同类型各记一条）
- 创建水位线（collector 创建前的写入不结算）
- 销毁/迁移后结算（按 4.6 新语义）
- `RelatedComponentOnly` 多类型 relevance
- 池化 core 的失效语义（销毁后旧 ref 抛 NRE；池复用后仍失效）
- 相等性：同一实例 `Create`/`Get`/`Typed` 相等，哈希稳定

### 5.3 回归门禁

- `dotnet test` 全量绿（当前 547 + 新增）
- collector 用例零改动（`git diff` 校验 `Test/EntityCollectorTestUnit.cs`、`Test/CollectorAccelerationTestUnit.cs` 无改动）
- Release 构建无新增警告

## 6. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 池复用导致过期句柄"复活" | `BindGeneration` 代数护栏 + 专项测试 |
| 延迟结算改变边界语义 | 已完成测试审计（第 3 节）；水位线、类型集合、判定逐条对齐；硬门禁 |
| journal 无限增长（collector 长期不 Flush） | 游标推进后压缩；文档说明 collector 需定期 Flush |
| 相等性从值语义变为（core 引用 + 代数） | 有效 ref 行为不变；跑现有相等性测试；实现前先验证 |
| span 逐行 observer 开销 | 保留契约，靠短路与 journal 合并降本；F3 总时间验收 |
| 无监听时仍有 observer 接口调用 | 接受（约几 ns）；如不达标再考虑 `Observer` 置空策略 |

## 7. 实施顺序（供计划拆分）

1. 池化 core：`ComponentRefCore` 可变化 + 代数护栏 + 存储槽位持有 + 池 + 失效/相等性测试
2. 热路径：registry 静态缓存 + `Structure` slot 访问器 + `ComponentRefCore` 快路径 + 内联
3. 信号链：兴趣短路 + `ChangeSink` 扁平 relay + `EntityMatchManager` 改直连
4. journal 与结算：合并、游标、水位线、压缩、stamp 去重 + 功能测试
5. span 路径接入与公共信号门控
6. 基准：RO/RW 比率、F1/F2/F3，跑全量回归

## 8. 参考

- `Kernel/Defines/ComponentRef.cs`、`Kernel/Structures/ComponentRefCore.cs`
- `Kernel/Structures/Structure.cs`、`Kernel/Structures/SparseStore.cs`、`Kernel/Structures/ComponentOrchestrator.cs`
- `Kernel/Managers/EntityMatchManager.cs`、`Kernel/Managers/EntityManager.cs`、`Kernel/Managers/ComponentManager.cs`
- `Kernel/Structures/ComponentTypeRegistry.cs`
- `Test/EntityCollectorTestUnit.cs`、`Test/CollectorAccelerationTestUnit.cs`、`Test/StructureBatchAccessTestUnit.cs`、`Test/StressTestUnit.cs`
- `docs/prompts/roslyn-ref-safety-analyzer-prompt.md`

## 9. 执行期修订（最终实现与初稿的差异）

以下为实现过程中经评审确认的设计修订，最终代码以本节为准：

1. **relay 形态**：初稿的 `IComponentChangeSink` 接口被直接委托取代——`ComponentManager.ChangeSink` 是 `Action<ulong, uint, EntityLocation>`，由 `World` 装配到 `EntityManager.OnRevisionChanged`，避免接口分派并携带实体 location（写路径不再查实体表）。
2. **journal 条目**：`RevisionEntry` 只存 `EntityId + TypeId`；`Type` 在结算时按需解析（仅当存在 `RelatedComponentOnly` 的 revision collector，计数 `m_relevanceGatedRevisionCollectors`），写路径不再做注册表查表。
3. **合并（coalescing）**：初稿的 `>= m_journalBase` 检查改为 `m_coalesceFloor`（所有 collector 游标的最大值）加位置归属校验；floor 上升或 journal 清空时通过 `EntityTable.InvalidatePendingRevisions()` 失效所有 pending 标记，避免后创建的 collector 漏掉创建后的写入。
4. **兴趣位缓存**：新增 `Signal<T>.ReceiversChanged` 内部钩子与 `Structure.HasChangeInterest` / `HasMutatingChangeHandlers` 缓存位；无监听时 RW 直接跳过 observer 链，有公共可变处理器时每次都通知并在通知后重新解析 live location。
5. **RW 快路径**：`TryBumpDenseRevision` / `TryBumpSparseRevision` 融合校验与 revision bump；同一 (entity,type) 已有 pending journal 条目且无公共可变处理器时跳过冗余通知；handler 可能在通知中迁移/销毁实体，因此 location 在 `Emit` 前捕获、`mutating` 标志在通知前捕获、add 路径在信号前快照 `BindGeneration`（重入时返回死句柄而不是别名句柄）。
6. **sparse 快路径**：`RO/RW` 的 sparse 分支改用缓存 store（`GetSparseStore` + `SparseStore<T>.Get`）。
7. **验收数据**（Debug，Apple M3 Max，SDK 8.0.425）：
   - 非缓存 RW/RO 比率：0 个 collector ≈ 0.87–0.95x、100 个 ≈ 1.0x、1000 个 ≈ 1.0–1.05x（限 1.2 / 1.5 / 2.0）
   - 流水线 F3：12463ms → ≈ 11ms（约 1100x）
   - 全量测试 594 通过；collector 用例零改动；公共 API 不变
   - 详细数据与原始输出见 `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`

已知遗留（非本次范围）：`AddDenseComponent` 的 `InvokeDenseCreate` 在 handler 于 add 信号中销毁/迁移实体时仍可能对已失效行调用（先于本次改造存在，Debug 下触发断言）；tag add 仍分配一个不池化的 core。
