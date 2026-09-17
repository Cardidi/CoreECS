# CoreECS v2 RW/RO 性能优化实施计划（Plan B：collector 延迟结算）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把非缓存 `ComponentRef<T>.RO/RW` 的单次成本降到与 collector 数量无关（RW/RO 比率 0/100/1000 collector 分别 < 1.2/1.5/2.0），并把 revision 变更的 collector 分发延迟到 `Flush()` 批量结算，同时保持全量测试（含 collector 用例零改动）全绿。

**Architecture:** 四个相互独立的改造：① `ComponentRefCore` 池化并由存储槽位持有（绑定代数护栏）；② `ComponentTypeRegistry` 泛型静态缓存 + `Structure` 按 slot 访问器 + 内联；③ 信号链兴趣短路与扁平 relay；④ `EntityMatchManager` 变更日志 + 每 collector 游标/水位线，`Flush()` 前批量结算。

**Tech Stack:** C#（Kernel：net8.0 + netstandard2.1，`LangVersion 9`，`AllowUnsafeBlocks`），NUnit 测试（net8.0，ImplicitUsings），`~/.dotnet/dotnet`（SDK 8.0.425）。

**Spec:** `docs/superpowers/specs/2026-09-18-rw-ro-performance-design.md`

**硬门禁：** `Test/EntityCollectorTestUnit.cs`、`Test/CollectorAccelerationTestUnit.cs` 以及 Stress/Integration 中的 collector 用例**不得修改**；任何失败只能改实现。

---

## 文件结构

| 文件 | 职责 | 动作 |
|---|---|---|
| `Kernel/Utils/ComponentRefCorePool.cs` | core 对象池（栈式，单线程） | 新建 |
| `Kernel/Structures/ComponentRefCore.cs` | 可变 core + 绑定代数 + 快路径缓存 | 修改 |
| `Kernel/Defines/ComponentRef.cs` | 句柄捕获代数、失效判定、相等性 | 修改 |
| `Kernel/Structures/Structure.cs` | dense core 数组、slot 访问器、span 路径 | 修改 |
| `Kernel/Structures/SparseStore.cs` | sparse core 数组与生命周期 | 修改 |
| `Kernel/Structures/SparseComponentContainer.cs` | 释放行 core 的辅助方法 | 修改 |
| `Kernel/Structures/ComponentOrchestrator.cs` | core 绑定/释放、非泛型 GetComponentRef | 修改 |
| `Kernel/Structures/EntityLocation.cs` | `PendingRevisionIndex` | 修改 |
| `Kernel/Structures/ComponentTypeRegistry.cs` | 泛型静态缓存 | 修改 |
| `Kernel/Defines/IComponentChangeSink.cs` | 内部变更直连接口 | 新建 |
| `Kernel/Managers/ComponentManager.cs` | 兴趣短路 + sink 转发 | 修改 |
| `Kernel/Managers/EntityManager.cs` | 实现 sink、连接 MatchManager | 修改 |
| `Kernel/Managers/EntityMatchManager.cs` | 变更日志、游标、结算、stamp 去重 | 修改 |
| `Kernel/World.cs` | 装配 sink 与 MatchManager | 修改 |
| `Kernel/Entity.cs` | `CollectComponents` 复用存储 core | 修改 |
| `Test/*` | 各任务新增测试与基准 | 新建 |

---

## Task 0: 记录改造前性能基线

**Files:**
- Create: `Test/PerformanceBaselineTestUnit.cs`
- Create: `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`

- [ ] **Step 1: 写基准测试（只打印，不设硬断言）**

```csharp
using System.Diagnostics;
using CoreECS.Defines;

namespace CoreECS.Test
{
    /// <summary>
    /// Pre-optimization baseline for RW/RO access and Flush settlement.
    /// Task 9/10 turn the printed numbers into assertions.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public class PerformanceBaselineTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public float X;
            public float Y;
        }

        private const int AccessIterations = 200_000;
        private const int Rounds = 4;
        private const int CollectorFanout = 1000;

        [Test]
        public void Baseline_NonCachedRoVsRw_ByCollectorCount()
        {
            foreach (var collectors in new[] { 0, 100, 1000 })
            {
                var ro = MeasureAccess(collectors, readOnly: true);
                var rw = MeasureAccess(collectors, readOnly: false);
                Console.WriteLine(
                    $"[baseline] collectors={collectors} RO={ro:F3}ms RW={rw:F3}ms ratio={rw / ro:F3}x");
            }
        }

        [Test]
        public void Baseline_FlushSettlement()
        {
            var f1 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1, writesPerEntity: AccessIterations);
            Console.WriteLine($"[baseline] F1 collectors={CollectorFanout} entities=1 writes={AccessIterations} flush={f1:F3}ms");

            var f2 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1000, writesPerEntity: 1);
            Console.WriteLine($"[baseline] F2 collectors={CollectorFanout} entities=1000 flush={f2:F3}ms");
        }

        [Test]
        public void Baseline_PipelineWritesPlusFlush()
        {
            var total = MeasurePipeline(collectors: CollectorFanout, writes: AccessIterations);
            Console.WriteLine($"[baseline] F3 collectors={CollectorFanout} writes={AccessIterations} total={total:F3}ms");
        }

        private static double MeasureAccess(int collectorCount, bool readOnly)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectors = CreateCollectors(world, collectorCount);
                var entity = world.CreateEntity();
                var position = entity.CreateComponent<Position>();
                FlushAll(collectors);

                var sum = 0f;
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < AccessIterations; i++)
                {
                    if (readOnly) sum += position.RO.X;
                    else position.RW.X = i;
                }
                sw.Stop();

                FlushAll(collectors);
                foreach (var c in collectors) c.Dispose();
                world.Shutdown();
                Assert.IsTrue(sum >= 0f || !readOnly);
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static double MeasureFlush(int collectors, int changedEntities, int writesPerEntity)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectorList = CreateCollectors(world, collectors);
                var entities = new List<Entity>();
                for (var i = 0; i < changedEntities; i++)
                {
                    var e = world.CreateEntity();
                    e.CreateComponent<Position>();
                    entities.Add(e);
                }
                FlushAll(collectorList);

                for (var w = 0; w < writesPerEntity; w++)
                {
                    for (var i = 0; i < entities.Count; i++)
                        entities[i].GetComponent<Position>().RW.X = w;
                }

                var sw = Stopwatch.StartNew();
                FlushAll(collectorList);
                sw.Stop();

                foreach (var c in collectorList) c.Dispose();
                world.Shutdown();
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static double MeasurePipeline(int collectors, int writes)
        {
            return MeasureBest(() =>
            {
                var world = new World();
                world.Startup();
                var collectorList = CreateCollectors(world, collectors);
                var entity = world.CreateEntity();
                var position = entity.CreateComponent<Position>();
                FlushAll(collectorList);

                var sw = Stopwatch.StartNew();
                for (var i = 0; i < writes; i++)
                    position.RW.X = i;
                FlushAll(collectorList);
                sw.Stop();

                foreach (var c in collectorList) c.Dispose();
                world.Shutdown();
                return TicksToMs(sw.ElapsedTicks);
            });
        }

        private static List<IEntityCollector> CreateCollectors(World world, int count)
        {
            var result = new List<IEntityCollector>(count);
            for (var i = 0; i < count; i++)
            {
                result.Add(world.CreateCollector(
                    EntityMatcher.With.OfAll<Position>(),
                    EntityCollectorFlag.RevisionAsChange));
            }
            return result;
        }

        private static void FlushAll(List<IEntityCollector> collectors)
        {
            for (var i = 0; i < collectors.Count; i++) collectors[i].Flush();
        }

        private static double MeasureBest(Func<double> scenario)
        {
            scenario();
            var best = scenario();
            for (var i = 1; i < Rounds; i++)
            {
                var current = scenario();
                if (current < best) best = current;
            }
            return best;
        }

        private static double TicksToMs(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }
}
```

- [ ] **Step 2: 运行基线并保存输出**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~PerformanceBaselineTestUnit" --verbosity normal`

Expected: 3 个测试通过；输出含 `[baseline] ...` 行。然后：

1. 把完整输出粘进 `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`（含日期、机器、数值）。
2. 把输出中的实测值回填到测试文件的 `PerformanceBaselineValues`（Task 10 用它做断言；默认 0 只表示"尚未采集"）。

```csharp
/// <summary>Pre-optimization numbers captured by Task 0 (see baseline doc).</summary>
internal static class PerformanceBaselineValues
{
    public static double F3Ms = 0d;   // ← Task 0 Step 2 回填
}
```

- [ ] **Step 3: Commit**

```bash
git add Test/PerformanceBaselineTestUnit.cs docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md
git commit -m "feat(test): add pre-optimization rw/ro and flush baseline"
```

---

## Task 1: ComponentRefCore 池化原语（可变 core + 绑定代数 + 句柄护栏）

**Files:**
- Create: `Kernel/Utils/ComponentRefCorePool.cs`
- Modify: `Kernel/Structures/ComponentRefCore.cs`
- Modify: `Kernel/Defines/ComponentRef.cs`
- Test: `Test/ComponentRefCorePoolTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Defines;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefCorePoolTestUnit
    {
        [SetUp]
        public void Setup() => ComponentRefCorePool.Clear();

        [Test]
        public void Pool_ReusesReleasedCore_AndBumpsBindGeneration()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);
            var firstBind = core.BindGeneration;

            ComponentRefCorePool.Release(core);
            var reused = ComponentRefCorePool.Get();
            reused.Bind(location, location.Generation, 7u, ComponentKind.Dense, 2u);

            Assert.AreSame(core, reused);
            Assert.Greater(reused.BindGeneration, firstBind);
        }

        [Test]
        public void Handle_StaleAfterPoolRebind_IsNotNullFalse()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);
            var handle = new ComponentRef(core);

            ComponentRefCorePool.Release(core);
            var reused = ComponentRefCorePool.Get();
            reused.Bind(location, location.Generation, 7u, ComponentKind.Dense, 2u);

            Assert.IsFalse(handle.NotNull);
            Assert.AreEqual(0UL, handle.EntityId);
        }

        [Test]
        public void Handles_SameCoreAndGeneration_AreEqual_AndHashStable()
        {
            var location = EntityLocation.Pool.Get();
            var core = ComponentRefCorePool.Get();
            core.Bind(location, location.Generation, 7u, ComponentKind.Dense, 1u);

            var first = new ComponentRef(core);
            var second = new ComponentRef(core);

            Assert.IsTrue(first.Equals(second));
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefCorePoolTestUnit" --verbosity minimal`
Expected: 编译失败（`ComponentRefCorePool`、`Bind`、`BindGeneration` 不存在）。

- [ ] **Step 3: 新建池**

`Kernel/Utils/ComponentRefCorePool.cs`:

```csharp
using System.Collections.Generic;
using CoreECS.Structures;

namespace CoreECS.Utils
{
    /// <summary>
    /// Single-threaded stack pool for component ref cores. Released cores keep their
    /// <see cref="ComponentRefCore.BindGeneration"/> so stale handles can never alias
    /// a recycled core.
    /// </summary>
    internal static class ComponentRefCorePool
    {
        private static readonly Stack<ComponentRefCore> s_pool = new();

        public static ComponentRefCore Get()
        {
            return s_pool.Count > 0 ? s_pool.Pop() : new ComponentRefCore();
        }

        public static void Release(ComponentRefCore core)
        {
            if (core == null) return;
            core.Reset();
            s_pool.Push(core);
        }

        public static void Clear() => s_pool.Clear();
    }
}
```

- [ ] **Step 4: 改造 `ComponentRefCore`**

`Kernel/Structures/ComponentRefCore.cs`：字段改为可写属性，新增 `BindGeneration` 与快路径缓存字段、`Bind`/`Reset`；保留原构造函数签名（`Test/ComponentRefCoreTestUnit.cs` 依赖它）。

```csharp
internal sealed class ComponentRefCore
{
    public EntityLocation Location { get; private set; }
    public uint Generation { get; private set; }
    public uint TypeId { get; private set; }
    public ComponentKind Kind { get; private set; }
    public uint Version { get; private set; }

    /// <summary>Bumped on every bind; stale handles capture the old value.</summary>
    public uint BindGeneration { get; private set; }

    internal Structure CachedStructure { get; private set; }
    internal int CachedSlot { get; private set; }
    internal SparseStore CachedSparseStore { get; private set; }

    public ComponentRefCore()
    {
        CachedSlot = -1;
    }

    public ComponentRefCore(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
    {
        CachedSlot = -1;
        Bind(location, generation, typeId, kind, version);
    }

    internal void Bind(EntityLocation location, uint generation, uint typeId, ComponentKind kind, uint version)
    {
        Location = location;
        Generation = generation;
        TypeId = typeId;
        Kind = kind;
        Version = version;
        BindGeneration = unchecked(BindGeneration + 1);
        CachedStructure = null;
        CachedSlot = -1;
        CachedSparseStore = null;
    }

    internal void Reset()
    {
        Location = null;
        TypeId = 0u;
        Kind = default;
        Version = 0u;
        CachedStructure = null;
        CachedSlot = -1;
        CachedSparseStore = null;
    }

    // NotNull / EntityId / Revision / ChangeRevision 保持现有实现（Task 4 再做快路径）
}
```

- [ ] **Step 5: 句柄捕获代数 + 失效护栏（相等性切换留到 Task 2）**

`Kernel/Defines/ComponentRef.cs`：

1. `ComponentRef` / `ComponentRef<T>` 增加 `internal readonly uint CoreGeneration;`，构造函数捕获 `core?.BindGeneration ?? 0`；增加：

```csharp
private bool IsAlive => Core != null && Core.BindGeneration == CoreGeneration;

public bool NotNull => IsAlive && Core.NotNull;
public ulong EntityId => NotNull ? Core.EntityId : 0UL;
public ulong Revision => NotNull ? Core.Revision : 0UL;
```

2. `RuntimeType` / `Inspect` / `Typed` / `Untyped` / `RequireStructure` 的失效判断统一用 `NotNull`（即包含代数校验）。

3. **本任务不改 `ComponentRefCoreComparer` / `Equals` / `GetHashCode`**：它们仍按现有值语义比较 `(Location, Generation, TypeId, Kind, Version)`。原因：`CreateComponent`/`GetComponent` 此刻仍各自新建 core，引用相等会打破现有相等性测试；等 Task 2 让存储槽位共享 core 后再切换（Task 2 Step 8）。

- [ ] **Step 6: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefCorePoolTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过（`ComponentRefCoreTestUnit` 因保留构造函数无需改动）。

- [ ] **Step 7: Commit**

```bash
git add Kernel/Utils/ComponentRefCorePool.cs Kernel/Structures/ComponentRefCore.cs Kernel/Defines/ComponentRef.cs Test/ComponentRefCorePoolTestUnit.cs
git commit -m "refactor(core): pool component ref cores with bind-generation guard"
```

---

## Task 2: 存储槽位持有 core（dense/sparse 生命周期 + 编排器绑定释放）

**Files:**
- Modify: `Kernel/Structures/Structure.cs`
- Modify: `Kernel/Structures/SparseStore.cs`
- Modify: `Kernel/Structures/SparseComponentContainer.cs`
- Modify: `Kernel/Structures/ComponentOrchestrator.cs`
- Modify: `Kernel/Entity.cs`
- Test: `Test/ComponentRefPoolingTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefPoolingTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }
        private struct Velocity : IComponent<Velocity> { public int X; }
        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void GetComponent_ReturnsSameCoreAcrossCalls()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>().RW.X = 1;

            var first = entity.GetComponent<Position>();
            var second = entity.GetComponent<Position>();

            Assert.AreSame(first.Core, second.Core);
            Assert.IsTrue(first.Equals(second));
        }

        [Test]
        public void CreateComponent_And_GetComponent_ShareCore()
        {
            var entity = _world.CreateEntity();
            var created = entity.CreateComponent<Position>();
            var fetched = entity.GetComponent<Position>();

            Assert.AreSame(created.Core, fetched.Core);
        }

        [Test]
        public void Migration_PreservesCoreInstanceAndValue()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 7;
            var core = position.Core;

            entity.CreateComponent<Velocity>();   // dense migration

            Assert.AreSame(core, position.Core);
            Assert.IsTrue(position.NotNull);
            Assert.AreEqual(7, entity.GetComponent<Position>().RW.X);
        }

        [Test]
        public void SwapRemove_PreservesOtherEntityCore()
        {
            var removed = _world.CreateEntity();
            var kept = _world.CreateEntity();
            removed.CreateComponent<Position>().RW.X = 1;
            var keptPosition = kept.CreateComponent<Position>();
            keptPosition.RW.X = 2;
            var keptCore = keptPosition.Core;

            _world.DestroyEntity(removed);

            Assert.AreSame(keptCore, keptPosition.Core);
            Assert.IsTrue(keptPosition.NotNull);
            Assert.AreEqual(2, keptPosition.RW.X);
        }

        [Test]
        public void DestroyComponent_StaleHandleIsCut()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            entity.DestroyComponent(position);

            Assert.IsFalse(position.NotNull);
            Assert.Throws<NullReferenceException>(() => { _ = position.RW.X; });
        }

        [Test]
        public void SparseComponent_CoreIsStoredAndReleased()
        {
            var entity = _world.CreateEntity();
            var mana = entity.CreateComponent<Mana>();
            var fetched = entity.GetComponent<Mana>();
            Assert.AreSame(mana.Core, fetched.Core);

            entity.DestroyComponent(mana);
            Assert.IsFalse(mana.NotNull);
        }

        [Test]
        public void GetComponents_ReusesStoredCores()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            var mana = entity.CreateComponent<Mana>();

            var all = entity.GetComponents();

            var positionEntry = all.First(r => r.Inspect<Position>());
            var manaEntry = all.First(r => r.Inspect<Mana>());
            Assert.AreSame(position.Core, positionEntry.Core);
            Assert.AreSame(mana.Core, manaEntry.Core);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefPoolingTestUnit" --verbosity minimal`
Expected: FAIL（`GetComponent` 每次新建 core，`AreSame` 失败；`GetComponents` 同理）。

- [ ] **Step 3: `Structure` 持有 dense cores**

在 `Kernel/Structures/Structure.cs`：

1. 字段与构造函数（与 `m_denseData` 同构）：

```csharp
private readonly ComponentRefCore[][] m_denseCores;
// ctor 循环内：
m_denseCores = new ComponentRefCore[denseCount][];
for (var i = 0; i < denseCount; i++)
{
    // ...existing...
    m_denseCores[i] = new ComponentRefCore[InitialCapacity];
}
```

2. `Grow()` 内对每列 `Array.Resize(ref m_denseCores[i], newCapacity);`

3. `Append(...)` 的清理循环内：

```csharp
var recycled = m_denseCores[i][row];
if (recycled != null)
{
    ComponentRefCorePool.Release(recycled);
    m_denseCores[i][row] = null;
}
```

4. `SwapRemove(int row)`：

```csharp
if (row != last)
{
    for (var i = 0; i < m_denseData.Length; i++)
    {
        Array.Copy(m_denseData[i], last, m_denseData[i], row, 1);
        m_denseVersions[i][row] = m_denseVersions[i][last];
        m_denseRevisions[i][row] = m_denseRevisions[i][last];
        m_denseCores[i][row] = m_denseCores[i][last];
    }
    m_entityIds[row] = m_entityIds[last];
    m_locations[row] = m_locations[last];
    m_locations[row].Row = row;
}

for (var i = 0; i < m_denseCores.Length; i++) m_denseCores[i][last] = null;
```

> 注意：`[last]` 的 core 归零**不释放**——所有权已随 `CopyDenseTo` 或 swap 移动。

5. `CopyDenseTo(...)` 内：

```csharp
target.m_denseCores[targetSlot][targetRow] = m_denseCores[i][sourceRow];
```

6. 新增 internal 访问器：

```csharp
internal ComponentRefCore GetDenseCore(int slot, int row) => m_denseCores[slot][row];
internal void SetDenseCore(int slot, int row, ComponentRefCore core) => m_denseCores[slot][row] = core;

internal void ReleaseDenseCoresAt(int row)
{
    for (var i = 0; i < m_denseCores.Length; i++)
    {
        var core = m_denseCores[i][row];
        if (core == null) continue;
        ComponentRefCorePool.Release(core);
        m_denseCores[i][row] = null;
    }
}
```

文件顶部加 `using CoreECS.Utils;`。

- [ ] **Step 4: `SparseStore` 持有 cores**

`Kernel/Structures/SparseStore.cs`：

1. 非泛型基类新增抽象成员：

```csharp
public abstract ComponentRefCore GetCore(int row);
public abstract void SetCore(int row, ComponentRefCore core);
public abstract void ReleaseCore(int row);
```

2. `SparseStore<T>`：

```csharp
private ComponentRefCore[] m_cores = new ComponentRefCore[InitialCapacity];
// EnsureCapacity 内：
Array.Resize(ref m_cores, newCapacity);
// Remove(int row) 内在清理前：
ComponentRefCorePool.Release(m_cores[row]);
m_cores[row] = null;
// RemoveRowSwap(int row) 内在 row != last 分支：
m_cores[row] = m_cores[last];
// ClearSlot(last) 内：
ComponentRefCorePool.Release(m_cores[row]);
m_cores[row] = null;
// CopyRowTo(...) 在存在分支：
typed.m_cores[targetRow] = m_cores[sourceRow];
// 覆写：
public override ComponentRefCore GetCore(int row) => m_cores[row];
public override void SetCore(int row, ComponentRefCore core) => m_cores[row] = core;
public override void ReleaseCore(int row) { ComponentRefCorePool.Release(m_cores[row]); m_cores[row] = null; }
```

文件顶部加 `using CoreECS.Utils;`。

- [ ] **Step 5: `SparseComponentContainer` 释放行 cores**

`Kernel/Structures/SparseComponentContainer.cs` 新增：

```csharp
internal void ReleaseCoresAt(int row)
{
    foreach (var store in m_stores.Values) store.ReleaseCore(row);
}
```

- [ ] **Step 6: `ComponentOrchestrator` 绑定/释放 + 非泛型 GetComponentRef**

`Kernel/Structures/ComponentOrchestrator.cs`：

1. `GetComponentRef<T>` 改为委托非泛型重载：

```csharp
public ComponentRefCore GetComponentRef<T>(ulong entityId) where T : struct, IComponent<T>
{
    var info = ComponentTypeRegistry.GetOrRegister<T>();
    return GetComponentRef(entityId, info.TypeId, info.Kind);
}

public ComponentRefCore GetComponentRef(ulong entityId, uint typeId, ComponentKind kind)
{
    if (!m_table.TryGetLocation(entityId, out var location)) return null;
    var structure = location.Structure;
    if (structure == null) return null;

    switch (kind)
    {
        case ComponentKind.Dense:
        {
            var slot = structure.IndexOfDense(typeId);
            if (slot < 0) return null;
            var core = structure.GetDenseCore(slot, location.Row);
            if (core == null)
            {
                core = ComponentRefCorePool.Get();
                core.Bind(location, location.Generation, typeId, ComponentKind.Dense,
                    structure.GetDenseVersion(typeId, location.Row));
                structure.SetDenseCore(slot, location.Row, core);
            }
            return core;
        }
        case ComponentKind.Sparse:
        {
            if (!structure.HasSparse(typeId, location.Row)) return null;
            var store = structure.SparseOrNull.GetStore(typeId);
            var core = store.GetCore(location.Row);
            if (core == null)
            {
                core = ComponentRefCorePool.Get();
                core.Bind(location, location.Generation, typeId, ComponentKind.Sparse,
                    store.GetVersion(location.Row));
                store.SetCore(location.Row, core);
            }
            return core;
        }
        default:
            return null;
    }
}
```

2. `AddDenseComponent<T>` 在 `target.SetDenseValue(...)` 后绑定并返回：

```csharp
var targetSlot = target.IndexOfDense(info.TypeId);
var core = ComponentRefCorePool.Get();
core.Bind(location, location.Generation, info.TypeId, ComponentKind.Dense, version);
target.SetDenseCore(targetSlot, targetRow, core);
// ...existing observer/hook...
return core;
```

3. `AddSparseComponent<T>` 在 `structure.SetSparse(...)` 后绑定：

```csharp
var store = structure.SparseOrNull.GetStore(info.TypeId);
var core = ComponentRefCorePool.Get();
core.Bind(location, location.Generation, info.TypeId, ComponentKind.Sparse, version);
store.SetCore(location.Row, core);
return core;
```

4. `RemoveDenseComponentCore`：在 `CopyDenseTo` 之后、`SwapRemove` 之前释放被移除类型的 core：

```csharp
var removedSlot = current.IndexOfDense(typeId);
var removedCore = current.GetDenseCore(removedSlot, sourceRow);
current.SetDenseCore(removedSlot, sourceRow, null);
ComponentRefCorePool.Release(removedCore);
```

5. `DestroyEntity`：在 `location.Structure?.SwapRemove(location.Row);` 之前：

```csharp
var structure = location.Structure;
if (structure != null)
{
    structure.ReleaseDenseCoresAt(location.Row);
    structure.SparseOrNull?.ReleaseCoresAt(location.Row);
}
```

- [ ] **Step 7: `Entity.CollectComponents` 复用存储 core**

`Kernel/Entity.cs`：`CollectComponents` 改为实例方法，用编排器取 core：

```csharp
private void CollectComponents(EntityLocation location, Structure structure, int row, ICollection<ComponentRef> results)
{
    var orchestrator = Orchestrator;
    var denseTypeIds = structure.DenseTypeIds;
    for (var i = 0; i < denseTypeIds.Count; i++)
    {
        var core = orchestrator.GetComponentRef(m_entityId, denseTypeIds[i], ComponentKind.Dense);
        if (core != null) results.Add(new ComponentRef(core));
    }

    var sparse = structure.SparseOrNull;
    if (sparse == null) return;

    foreach (var typeId in sparse.TypeIds)
    {
        if (!structure.HasSparse(typeId, row)) continue;
        var core = orchestrator.GetComponentRef(m_entityId, typeId, ComponentKind.Sparse);
        if (core != null) results.Add(new ComponentRef(core));
    }
}
```

（两处调用点由 `CollectComponents(location, structure, row, results)` 改为 `CollectComponents(...)` 不变，方法去 `static`。）

- [ ] **Step 8: 过期句柄加固 + 相等性切换**

存储槽位现在共享 core（同一组件实例的 `CreateComponent`/`GetComponent`/`GetComponents`/`Typed` 都拿同一对象），但池化引入两个必须先堵的洞：

1. **`RW` 在 bump 前校验句柄代数**：`Kernel/Defines/ComponentRef.cs` 的 `ComponentRef<T>.RW` 当前先调 `Core.ChangeRevision()` 再 `RequireStructure()`。过期句柄的 core 被回收复用后，`ChangeRevision` 会误 bump 新组件的 revision。改为：

```csharp
public ref T RW
{
    get
    {
        if (!NotNull) throw new NullReferenceException("Component Reference is cut.");
        Core.ChangeRevision();

        // Resolve after the change notification: a handler may migrate or destroy
        // the entity, so the live structure and row must be read afterwards.
        var structure = RequireStructure();
        var row = Core.Location.Row;
        // ...switch 保持现状（dense 用 TryGetDenseSlot + GetDenseRefAt）
    }
}
```

2. **`Typed` / `Untyped` 传播当前句柄代数**：新增 internal 构造 `ComponentRef(ComponentRefCore core, uint coreGeneration)` 与 `ComponentRef<T>(ComponentRefCore core, uint coreGeneration)`，`Typed` / `Untyped` 用当前句柄的 `CoreGeneration` 构造，避免 `noSafeCheck: true` 从被复用的 core 重新捕获新代数。

3. **强化过期句柄测试（本步的 TDD 锚点）**：`Handle_StaleAfterPoolRebind_IsNotNullFalse` 当前测不出护栏（location 未绑定 structure）。改为：
   - 构造一个真实 world/structure，把 core 绑到 dense 组件并记录 revision
   - release + rebind 到另一个组件后断言：
     a. 旧句柄 `NotNull == false`（去掉护栏会变成 true，具备判别力）
     b. 旧句柄 `RW` 抛 `NullReferenceException`，且**新组件 revision 不变**（验证第 1 条修复）
     c. 旧句柄 `Typed(noSafeCheck: true)` 仍是死句柄（验证第 2 条传播）

   顺带小修：`ComponentRefCore.Reset()` 设 `Generation = 0`；更新 `ComponentRefCore` 类注释（已可变/池化）；`ComponentRefCorePool` 文档注明"每个 core 至多 release 一次"。

然后切换 `ComponentRefCoreComparer` 到 core 引用 + 绑定代数：

```csharp
internal static class ComponentRefCoreComparer
{
    public static bool Equals(ComponentRefCore left, uint leftGeneration, ComponentRefCore right, uint rightGeneration)
    {
        return ReferenceEquals(left, right) && leftGeneration == rightGeneration;
    }

    public static int GetHashCode(ComponentRefCore core, uint coreGeneration)
    {
        if (core == null) return 0;
        unchecked { return (RuntimeHelpers.GetHashCode(core) * 397) ^ (int)coreGeneration; }
    }
}
```

并把 `ComponentRef` / `ComponentRef<T>` 的 `Equals` / `GetHashCode` 改为：

```csharp
public bool Equals(ComponentRef other) =>
    ComponentRefCoreComparer.Equals(Core, CoreGeneration, other.Core, other.CoreGeneration);
public override int GetHashCode() =>
    ComponentRefCoreComparer.GetHashCode(Core, CoreGeneration);
```

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过（`ComponentTestUnit` / `EntityTestUnit` 的相等性用例依赖共享 core，Task 2 Step 3-7 已满足）。

- [ ] **Step 9: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefPoolingTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过；若 `ComponentTestUnit` 的相等性测试失败，检查 core 是否被同一实例复用（不应出现不同 core 表示同一组件）。

- [ ] **Step 10: Commit**

```bash
git add Kernel/Structures/Structure.cs Kernel/Structures/SparseStore.cs Kernel/Structures/SparseComponentContainer.cs Kernel/Structures/ComponentOrchestrator.cs Kernel/Entity.cs Test/ComponentRefPoolingTestUnit.cs
git commit -m "refactor(core): store pooled ref cores in dense and sparse slots"
```

---

## Task 3: `ComponentTypeRegistry` 泛型静态缓存

**Files:**
- Modify: `Kernel/Structures/ComponentTypeRegistry.cs`
- Test: `Test/ComponentTypeRegistryCacheTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTypeRegistryCacheTestUnit
    {
        private struct CachedDense : IComponent<CachedDense> { public int X; }
        private struct CachedSparse : ISparseComponent<CachedSparse> { public int X; }
        private struct CachedTag : ITagComponent<CachedTag> { }

        [Test]
        public void GetOrRegister_GenericAndTypeOverloads_Agree()
        {
            var generic = ComponentTypeRegistry.GetOrRegister<CachedDense>();
            var byType = ComponentTypeRegistry.GetOrRegister(typeof(CachedDense));

            Assert.AreEqual(generic.TypeId, byType.TypeId);
            Assert.AreEqual(ComponentKind.Dense, generic.Kind);
            Assert.AreEqual(ComponentKind.Sparse, ComponentTypeRegistry.GetOrRegister<CachedSparse>().Kind);
            Assert.AreEqual(ComponentKind.Tag, ComponentTypeRegistry.GetOrRegister<CachedTag>().Kind);
        }

        [Test]
        public void GetOrRegister_RepeatedCalls_ReturnSameInfoWithoutNewRegistration()
        {
            var before = ComponentTypeRegistry.RegisteredTypeCount;
            var first = ComponentTypeRegistry.GetOrRegister<CachedDense>();
            var second = ComponentTypeRegistry.GetOrRegister<CachedDense>();

            Assert.AreEqual(first.TypeId, second.TypeId);
            Assert.AreEqual(before, ComponentTypeRegistry.RegisteredTypeCount);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentTypeRegistryCacheTestUnit" --verbosity minimal`
Expected: FAIL（`RegisteredTypeCount` 不存在或第二次注册计数变化）。

- [ ] **Step 3: 实现缓存 + 测试钩子**

`Kernel/Structures/ComponentTypeRegistry.cs`：

```csharp
/// <summary>Number of registered type entries (test hook).</summary>
internal static int RegisteredTypeCount => s_byType.Count;

private static class Cache<T> where T : struct, IComponent<T>
{
    public static readonly ComponentTypeInfo Info = GetOrRegister(typeof(T));
}

public static ComponentTypeInfo GetOrRegister<T>() where T : struct, IComponent<T> => Cache<T>.Info;
```

- [ ] **Step 4: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentTypeRegistryCacheTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures/ComponentTypeRegistry.cs Test/ComponentTypeRegistryCacheTestUnit.cs
git commit -m "refactor(core): cache component type info in generic statics"
```

---

## Task 4: `Structure` slot 访问器 + `ComponentRefCore` 快路径

**Files:**
- Modify: `Kernel/Structures/Structure.cs`
- Modify: `Kernel/Structures/ComponentRefCore.cs`
- Modify: `Kernel/Defines/ComponentRef.cs`
- Test: `Test/ComponentRefFastPathTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentRefFastPathTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }
        private struct Velocity : IComponent<Velocity> { public int X; }
        private struct Health : IComponent<Health> { public int X; }

        [Test]
        public void Rw_AfterMigration_RecomputesSlotAndWritesLiveData()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 1;

            entity.CreateComponent<Velocity>();   // 迁移，Position slot 可能变化
            position.RW.X = 9;
            entity.CreateComponent<Health>();     // 再次迁移

            Assert.AreEqual(9, entity.GetComponent<Position>().RW.X);
        }

        [Test]
        public void Core_CachesDenseSlotAfterFirstAccess()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            _ = position.RW.X;

            var structure = _world.GetManager<CoreECS.Managers.EntityManager>().Table
                .TryGetLocation(entity.EntityId, out var location) ? location.Structure : null;
            Assert.IsNotNull(structure);
            Assert.AreEqual(structure.IndexOfDense(ComponentTypeRegistry.GetOrRegister<Position>().TypeId),
                position.Core.CachedSlot);
        }

        [Test]
        public void Ro_And_Revision_UseFastPathWithoutBreakingSemantics()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 3;
            var revision = position.Revision;

            Assert.AreEqual(3, position.RO.X);
            Assert.GreaterOrEqual(position.Revision, revision);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefFastPathTestUnit" --verbosity minimal`
Expected: FAIL（`CachedSlot` 恒为 -1）。

- [ ] **Step 3: `Structure` 新增 slot 访问器**

`Kernel/Structures/Structure.cs`（顶部 `using System.Runtime.CompilerServices;`）：

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal ref T GetDenseRefAt<T>(int slot, int row) where T : struct, IComponent<T>
    => ref ((T[])m_denseData[slot])[row];

[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal uint GetDenseVersionAt(int slot, int row) => m_denseVersions[slot][row];

[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal uint GetDenseRevisionAt(int slot, int row) => m_denseRevisions[slot][row];

[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal uint BumpDenseRevisionAt(int slot, int row)
{
    var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
    m_denseRevisions[slot][row] = revision;
    return revision;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal void NotifyChanged(int row, uint typeId) => Observer?.OnComponentChanged(this, row, typeId);
```

- [ ] **Step 4: `ComponentRefCore` 快路径**

`Kernel/Structures/ComponentRefCore.cs` 增加缓存方法并把 `NotNull` / `Revision` / `ChangeRevision` 换成快路径（`TryGetDenseSlot` 为 internal，供 `ComponentRef<T>` 复用）：

```csharp
internal bool TryGetDenseSlot(Structure structure, out int slot)
{
    if (ReferenceEquals(CachedStructure, structure) && CachedSlot >= 0)
    {
        slot = CachedSlot;
        return true;
    }

    slot = structure.IndexOfDense(TypeId);
    if (slot >= 0)
    {
        CachedStructure = structure;
        CachedSlot = slot;
        CachedSparseStore = null;
    }
    return slot >= 0;
}

internal SparseStore GetSparseStore(Structure structure)
{
    if (ReferenceEquals(CachedStructure, structure) && CachedSparseStore != null) return CachedSparseStore;

    var store = structure.SparseOrNull?.GetStore(TypeId);
    if (store != null)
    {
        CachedStructure = structure;
        CachedSlot = -1;
        CachedSparseStore = store;
    }
    return store;
}
```

`NotNull` dense 分支：

```csharp
case ComponentKind.Dense:
    return row >= 0 && row < structure.Count &&
           TryGetDenseSlot(structure, out var slot) &&
           structure.GetDenseVersionAt(slot, row) == Version;
```

`NotNull` sparse 分支用 `GetSparseStore(structure)`；`Revision` dense 用 `TryGetDenseSlot` + `GetDenseRevisionAt`；`ChangeRevision` dense：

```csharp
case ComponentKind.Dense:
{
    var structure = Location.Structure;
    var row = Location.Row;
    TryGetDenseSlot(structure, out var slot);
    var revision = structure.BumpDenseRevisionAt(slot, row);
    structure.NotifyChanged(row, TypeId);
    return revision;
}
```

- [ ] **Step 5: `ComponentRef<T>.RO/RW` 使用 slot 访问器**

`Kernel/Defines/ComponentRef.cs`：

```csharp
public ref readonly T RO
{
    get
    {
        var structure = RequireStructure();
        var row = Core.Location.Row;
        switch (Core.Kind)
        {
            case ComponentKind.Dense:
                Core.TryGetDenseSlot(structure, out var slot);
                return ref structure.GetDenseRefAt<T>(slot, row);
            case ComponentKind.Sparse:
                return ref structure.GetSparseRef<T>(row);
            default:
                throw new InvalidOperationException("Tag components carry no data.");
        }
    }
}
```

`RW` 同样在 `Core.ChangeRevision()` 之后用 `Core.TryGetDenseSlot(structure, out var slot)` + `GetDenseRefAt<T>`。

- [ ] **Step 6: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentRefFastPathTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过

- [ ] **Step 7: Commit**

```bash
git add Kernel/Structures/Structure.cs Kernel/Structures/ComponentRefCore.cs Kernel/Defines/ComponentRef.cs Test/ComponentRefFastPathTestUnit.cs
git commit -m "refactor(core): add slot-based dense accessors and cached ref fast path"
```

---

## Task 5: 信号链兴趣短路 + 扁平 relay（保持同步语义）

**Files:**
- Create: `Kernel/Defines/IComponentChangeSink.cs`
- Modify: `Kernel/Managers/ComponentManager.cs`
- Modify: `Kernel/Managers/EntityManager.cs`
- Modify: `Kernel/Managers/EntityMatchManager.cs`
- Modify: `Kernel/World.cs`
- Test: `Test/ComponentChangeSinkTestUnit.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentChangeSinkTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }

        [Test]
        public void ComponentManager_ChangeSignal_HasNoInternalSubscribers()
        {
            var component = _world.GetManager<ComponentManager>();
            Assert.IsFalse(component.OnComponentChanged.HasReceivers,
                "the internal EntityManager bridge must not subscribe to the public signal");
        }

        [Test]
        public void RevisionChange_UserSubscribed_StillReceivesEvent()
        {
            var component = _world.GetManager<ComponentManager>();
            var received = 0;
            component.OnComponentChanged.Add((_, _) => received += 1);

            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();
            position.RW.X = 1;

            Assert.AreEqual(1, received);
        }

        [Test]
        public void RevisionChange_CollectorStillTracksChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 5;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentChangeSinkTestUnit" --verbosity minimal`
Expected: `ComponentManager_ChangeSignal_HasNoInternalSubscribers` FAIL（EntityManager 仍订阅）。

- [ ] **Step 3: 新增 sink 接口**

`Kernel/Defines/IComponentChangeSink.cs`:

```csharp
namespace CoreECS.Defines
{
    /// <summary>
    /// Internal fast path for component revision changes. Implemented by the entity
    /// manager so the component manager can skip the public signal chain.
    /// </summary>
    internal interface IComponentChangeSink
    {
        void OnRevisionChanged(ulong entityId, uint typeId);
    }
}
```

- [ ] **Step 4: `ComponentManager` 转发**

`Kernel/Managers/ComponentManager.cs`：

```csharp
internal IComponentChangeSink ChangeSink { get; set; }

public void OnComponentChanged(Structure structure, int row, uint typeId)
{
    var entityId = structure.Entities[row];
    if (m_manager.OnComponentChanged.HasReceivers)
    {
        m_manager.OnComponentChanged.Emit(
            entityId, ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
    }

    m_manager.ChangeSink?.OnRevisionChanged(entityId, typeId);
}
```

- [ ] **Step 5: `EntityManager` 实现 sink 并连接 MatchManager**

`Kernel/Managers/EntityManager.cs`：

1. 类声明加 `: IComponentChangeSink`（`using CoreECS.Defines;` 已有）。
2. 新增字段与方法：

```csharp
private EntityMatchManager m_matchManager;

internal void ConnectMatchManager(EntityMatchManager matchManager) => m_matchManager = matchManager;

public void OnRevisionChanged(ulong entityId, uint typeId)
{
    if (OnEntityChangeComp.HasReceivers)
    {
        OnEntityChangeComp.Emit(entityId, ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
    }

    m_matchManager?.OnRevisionChanged(entityId, typeId);
}
```

3. `OnManagerCreated` 删除 `m_compManager.OnComponentChanged.Add(_onComponentChanged);`；删除 `_onComponentChanged` 方法；`OnManagerDestroyed` 删除对应 `Remove`。

- [ ] **Step 6: `EntityMatchManager` 提供直连入口（本任务仍同步循环）**

`Kernel/Managers/EntityMatchManager.cs`：

1. 删除 `_ensureEntitySignalSubscriptions` 中的 `m_entityManager.OnEntityChangeComp.Add(_onComponentChanged);` 与 `_release...`、`OnManagerDestroyed` 中的对应 `Remove`；删除 `_onComponentChanged` 方法。
2. 新增：

```csharp
internal void OnRevisionChanged(ulong entityId, uint typeId)
{
    if (m_revisionTrackingCollectorCount == 0) return;

    var componentType = ComponentTypeRegistry.GetById(typeId).Type;
    foreach (var collector in m_collectors)
    {
        _changeCollector(collector, entityId, null, false, componentType);
    }
}
```

- [ ] **Step 7: `World` 装配**

`Kernel/World.cs` 在 156-159 行的 manager 引用之后加：

```csharp
Entity.ConnectMatchManager(EntityMatch);
Component.ChangeSink = Entity;
```

- [ ] **Step 8: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~ComponentChangeSinkTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过（行为与改造前一致）

- [ ] **Step 9: Commit**

```bash
git add Kernel/Defines/IComponentChangeSink.cs Kernel/Managers/ComponentManager.cs Kernel/Managers/EntityManager.cs Kernel/Managers/EntityMatchManager.cs Kernel/World.cs Test/ComponentChangeSinkTestUnit.cs
git commit -m "refactor(core): flatten revision change relay behind interest gate"
```

---

## Task 6: 变更日志 + 游标/水位线 + Flush 结算（Plan B 核心）

> 承接 Task 5 的顺序变化：扁平 relay 后，用户 `OnEntityChangeComp` 处理器先于 collector 记账执行。本任务的延迟结算在 `Flush()` 时统一处理，该顺序差异随之被取代；如需在 commit body 记录此背景，可引用本注。


**Files:**
- Modify: `Kernel/Structures/EntityLocation.cs`
- Modify: `Kernel/Managers/EntityMatchManager.cs`
- Test: `Test/CollectorDeferredSettlementTestUnit.cs`

- [ ] **Step 1: 写失败测试（新语义）**

```csharp
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class CollectorDeferredSettlementTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }
        private struct Velocity : IComponent<Velocity> { public int X; }
        private struct Mana : ISparseComponent<Mana> { public int Value; }

        [Test]
        public void Settlement_SameEntitySameType_CoalescesToOneChangedEntry()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            var position = entity.GetComponent<Position>();
            for (var i = 0; i < 100; i++) position.RW.X = i;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_CollectorCreatedAfterWrite_DoesNotMark()
        {
            var entity = _world.CreateEntity();
            var position = entity.CreateComponent<Position>();

            // An existing collector activates journaling before the write.
            var first = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            first.Flush();
            first.Flush();

            position.RW.X = 1;

            var late = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            late.Flush();
            first.Flush();   // publish first's Changed before asserting

            Assert.AreEqual(0, late.Changed.Count, "a collector must not settle writes that predate its creation");
            Assert.AreEqual(1, first.Changed.Count, "the existing collector still sees the write");
        }

        [Test]
        public void Settlement_DestroyBeforeFlush_DropsRevisionChanged()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            _world.DestroyEntity(entity);
            collector.Flush();

            Assert.AreEqual(0, collector.Changed.Count);
            Assert.AreEqual(1, collector.Clashing.Count);
        }

        [Test]
        public void Settlement_RelevantRevisionMarks_IrrelevantDoesNot()
        {
            const EntityCollectorFlag flags =
                EntityCollectorFlag.RevisionAsChange | EntityCollectorFlag.RelatedComponentOnly;

            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            entity.CreateComponent<Velocity>();
            var collector = _world.CreateCollector(EntityMatcher.With.OfAll<Position>(), flags);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Velocity>().RW.X = 1;
            collector.Flush();
            Assert.AreEqual(0, collector.Changed.Count);

            entity.GetComponent<Position>().RW.X = 2;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_MigratedBeforeFlush_UsesLiveStructure()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            entity.CreateComponent<Velocity>();   // 迁移，但仍匹配
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~CollectorDeferredSettlementTestUnit" --verbosity minimal`
Expected: `Settlement_DestroyBeforeFlush_DropsRevisionChanged` FAIL（当前同步判定会保留 Changed），其余 PASS；`Settlement_CollectorCreatedAfterWrite_DoesNotMark` 在改造后若无水位线会 FAIL（改造前因 collector 未订阅而恰好 PASS），是水位线的判别测试。

- [ ] **Step 3: `EntityLocation` 增加 pending 索引**

`Kernel/Structures/EntityLocation.cs`：

```csharp
/// <summary>Logical journal index of the pending revision entry for this location; -1 when none.</summary>
public int PendingRevisionIndex = -1;
// Reset() 内：
PendingRevisionIndex = -1;
```

- [ ] **Step 4: `EntityMatchManager` 日志与结算**

`Kernel/Managers/EntityMatchManager.cs`：

1. 内部结构：

```csharp
private readonly struct RevisionEntry
{
    public readonly ulong EntityId;
    public readonly uint TypeId;
    public readonly Type Type;

    public RevisionEntry(ulong entityId, uint typeId, Type type)
    {
        EntityId = entityId;
        TypeId = typeId;
        Type = type;
    }
}

private readonly List<RevisionEntry> m_journal = new();
private int m_journalBase;
private readonly List<Collector> m_revisionCollectors = new();
private int JournalLogicalEnd => m_journalBase + m_journal.Count;
```

2. `OnRevisionChanged` 改为写日志：

```csharp
internal void OnRevisionChanged(ulong entityId, uint typeId)
{
    if (m_revisionTrackingCollectorCount == 0) return;
    if (!m_entityManager.Table.TryGetLocation(entityId, out var location)) return;

    var pending = location.PendingRevisionIndex;
    if (pending >= m_journalBase && pending < JournalLogicalEnd)
    {
        var existing = m_journal[pending - m_journalBase];
        if (existing.EntityId == entityId && existing.TypeId == typeId) return;
    }

    m_journal.Add(new RevisionEntry(entityId, typeId, ComponentTypeRegistry.GetById(typeId).Type));
    location.PendingRevisionIndex = JournalLogicalEnd - 1;
}
```

3. `Collector` 增加游标与结算方法：

```csharp
public int JournalCursor;

public void SettleRevision(ulong entityId, uint typeId, Type componentType)
{
    if (!TrackRevisionChanged) return;
    if (!m_manager.m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) return;
    if ((Matcher.EntityMask & location.Structure.Mask) == 0) return;
    if (HasChangeComponent && !Matcher.IsRelevantComponent(componentType)) return;

    var alreadyCollected =
        (ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
         ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId)) &&
        !ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
    if (!alreadyCollected) return;
    if (!Matches(location.Structure, location.Row)) return;

    MarkChanged(entityId);
}
```

4. `Flush()` 最前面（swap 之前）插入：

```csharp
m_manager.SettleRevisions(this);
```

5. `MakeCollector` 在 `m_collectors.Add(c)` 后：

```csharp
if (c.TrackRevisionChanged)
{
    c.JournalCursor = JournalLogicalEnd;
    m_revisionCollectors.Add(c);
}
```

6. `_onDisposeCollector`：

```csharp
if (collector.TrackRevisionChanged) m_revisionCollectors.Remove(collector);
```

7. 结算与压缩：

```csharp
private void SettleRevisions(Collector collector)
{
    if (!collector.TrackRevisionChanged) return;

    for (var i = collector.JournalCursor; i < JournalLogicalEnd; i++)
    {
        var entry = m_journal[i - m_journalBase];
        collector.SettleRevision(entry.EntityId, entry.TypeId, entry.Type);

        // Clear the coalescing marker once the entry is consumed, otherwise a later
        // write to the same (entity, type) merges into an already-settled entry and is
        // invisible to the next Flush.
        if (m_entityManager.Table.TryGetLocation(entry.EntityId, out var location) &&
            location.PendingRevisionIndex == i)
        {
            location.PendingRevisionIndex = -1;
        }
    }

    collector.JournalCursor = JournalLogicalEnd;
    CompactJournalIfNeeded();
}

private void CompactJournalIfNeeded()
{
    if (m_revisionCollectors.Count == 0)
    {
        m_journal.Clear();
        m_journalBase = 0;
        return;
    }

    var min = int.MaxValue;
    for (var i = 0; i < m_revisionCollectors.Count; i++)
    {
        var cursor = m_revisionCollectors[i].JournalCursor;
        if (cursor < min) min = cursor;
    }

    var removable = min - m_journalBase;
    if (removable < 1024) return;

    m_journal.RemoveRange(0, removable);
    m_journalBase += removable;
}
```

> 执行时发现的两个必要修正：① `SettleRevisions` 先判 `TrackRevisionChanged` 再取游标，否则非追踪 collector 的默认游标 0 在压缩后会产生负下标；② settle 后清 `PendingRevisionIndex`，否则同一 (entity,type) 的后续写入会合并进已消费条目而丢失（会被 `EntityCollector_AlternatingModifyAndFlush_EachFlushExposesOneChange` 抓到）。另：最后一个 revision collector dispose 时清空 journal。

8. `OnManagerDestroyed`：`m_journal.Clear(); m_journalBase = 0; m_revisionCollectors.Clear();`

- [ ] **Step 5: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~CollectorDeferredSettlementTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全量通过，且 collector 用例零改动（`git diff --stat Test/EntityCollectorTestUnit.cs Test/CollectorAccelerationTestUnit.cs` 为空）

- [ ] **Step 6: Commit**

```bash
git add Kernel/Structures/EntityLocation.cs Kernel/Managers/EntityMatchManager.cs Test/CollectorDeferredSettlementTestUnit.cs
git commit -m "feat(core): settle collector revision changes at flush via change journal"
```

---

## Task 7: 结算去重 stamp 优化

**Files:**
- Modify: `Kernel/Managers/EntityMatchManager.cs`
- Test: `Test/CollectorSettlementDedupTestUnit.cs`

- [ ] **Step 1: 写失败测试（行为不变，仅验证去重正确性）**

```csharp
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class CollectorSettlementDedupTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }
        private struct Velocity : IComponent<Velocity> { public int X; }

        [Test]
        public void Settlement_MultipleTypesSameEntity_ChangedContainsEntityOnce()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            entity.CreateComponent<Velocity>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>().OfAll<Velocity>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            entity.GetComponent<Velocity>().RW.X = 2;
            collector.Flush();

            Assert.AreEqual(1, collector.Changed.Count);
            Assert.AreEqual(entity.EntityId, collector.Changed[0]);
        }

        [Test]
        public void Settlement_NextPhase_MarksAgainAfterFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<Position>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            entity.GetComponent<Position>().RW.X = 1;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);

            entity.GetComponent<Position>().RW.X = 2;
            collector.Flush();
            Assert.AreEqual(1, collector.Changed.Count);
        }
    }
}
```

- [ ] **Step 2: 运行确认现状通过（stamp 是等价替换，先保留红/绿记录）**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~CollectorSettlementDedupTestUnit" --verbosity minimal`
Expected: PASS（当前 HashSet 去重已正确；本任务只做性能替换，测试防回归）

- [ ] **Step 3: `Collector.MarkChanged` 换 stamp**

`Kernel/Managers/EntityMatchManager.cs` 的 `Collector`：

```csharp
private ulong[] m_changedStamp = new ulong[16];
private ulong m_changedEpoch = 1;

public void MarkChanged(ulong entityId)
{
    if (entityId <= int.MaxValue)
    {
        var index = (int)entityId;
        if (index >= m_changedStamp.Length)
        {
            var grown = Math.Max(index + 1, m_changedStamp.Length * 2);
            Array.Resize(ref m_changedStamp, grown);
        }

        if (m_changedStamp[index] == m_changedEpoch) return;
        m_changedStamp[index] = m_changedEpoch;
        Buffers[CHANGE_CHANGED_BUFFER_INDEX].Add(entityId);
        return;
    }

    AddUniqueToBuffer(CHANGE_CHANGED_BUFFER_INDEX, entityId);
}
```

`Flush()` 在 swap 之后、清理之后加 `m_changedEpoch += 1;`；`Dispose()` 里 `Array.Clear(m_changedStamp, 0, m_changedStamp.Length);`。

> `BufferSets[CHANGE_CHANGED_BUFFER_INDEX]` 不再由 `MarkChanged` 维护；确认无 `ContainsInBuffer(CHANGE_CHANGED_BUFFER_INDEX, ...)` 调用（当前只有 COLLECTED / MATCHING / CLASHING 会被查询）。

- [ ] **Step 4: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~CollectorSettlementDedupTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过

- [ ] **Step 5: Commit**

```bash
git add Kernel/Managers/EntityMatchManager.cs Test/CollectorSettlementDedupTestUnit.cs
git commit -m "refactor(core): dedup settled changed entries with an epoch stamp"
```

---

## Task 8: span 批量路径接入

**Files:**
- Modify: `Kernel/Structures/Structure.cs`
- Test: `Test/StructureBatchSettlementTestUnit.cs`

- [ ] **Step 1: 写失败测试（collector 视角的 span 结算）**

```csharp
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureBatchSettlementTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown() => _world?.Shutdown();

        private struct Position : IComponent<Position> { public int X; }

        [Test]
        public void RwSpan_MarksEveryCollectedRowOnceAfterFlush()
        {
            var entities = new List<Entity>();
            for (var i = 0; i < 3; i++)
            {
                var entity = _world.CreateEntity();
                entity.CreateComponent<Position>();
                entities.Add(entity);
            }

            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<Position>(),
                EntityCollectorFlag.RevisionAsChange);
            collector.Flush();
            collector.Flush();

            var structure = _world.GetManager<CoreECS.Managers.EntityManager>().Table
                .TryGetLocation(entities[0].EntityId, out var location) ? location.Structure : null;
            Assert.IsNotNull(structure);

            var span = structure.GetReadWriteDenseColumn<Position>();
            for (var i = 0; i < span.Length; i++) span[i].X = i;

            collector.Flush();

            Assert.AreEqual(3, collector.Changed.Count);
            foreach (var entity in entities)
                CollectionAssert.Contains(collector.Changed, entity.EntityId);
        }
    }
}
```

- [ ] **Step 2: 运行确认现状通过（span 逐行通知 → 日志逐行追加 → 结算）**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~StructureBatchSettlementTestUnit" --verbosity minimal`
Expected: PASS（Task 6 已使 span 走日志；本任务确认契约并补测试）

- [ ] **Step 3: `Structure` span 路径复用泛型缓存与 `NotifyChanged`**

`Kernel/Structures/Structure.cs`：

```csharp
public ReadOnlySpan<T> GetReadOnlyDenseColumn<T>() where T : struct, IComponent<T>
{
    return ((T[])m_denseData[SlotOf<T>()]).AsSpan(0, m_count);
}

public Span<T> GetReadWriteDenseColumn<T>() where T : struct, IComponent<T>
{
    var slot = SlotOf<T>();
    var revisions = m_denseRevisions[slot];
    var typeId = m_denseTypeIds[slot];
    for (var row = 0; row < m_count; row++)
    {
        revisions[row] = (revisions[row] % uint.MaxValue) + 1;
        Observer?.OnComponentChanged(this, row, typeId);
    }

    return ((T[])m_denseData[slot]).AsSpan(0, m_count);
}
```

`SlotOf<T>` 已走 `ComponentTypeRegistry.GetOrRegister<T>()`（Task 3 后为静态缓存）。行为与逐行通知契约保持不变。

- [ ] **Step 4: 运行新测试与全量测试**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~StructureBatchSettlementTestUnit" --verbosity minimal`
Expected: PASS

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过（`StructureBatchAccessTestUnit` 的逐行 observer 断言不变）

- [ ] **Step 5: Commit**

```bash
git add Kernel/Structures/Structure.cs Test/StructureBatchSettlementTestUnit.cs
git commit -m "refactor(core): route batch rw spans through the cached type and change journal"
```

---

## Task 9: RO/RW 比率基准与断言

**Files:**
- Modify: `Test/PerformanceBaselineTestUnit.cs`
- Modify: `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`（补改造后数据）

- [ ] **Step 1: 在基准中加断言**

在 `Baseline_NonCachedRoVsRw_ByCollectorCount` 内按 collector 数断言：

```csharp
var ratio = rw / ro;
var limit = collectors switch { 0 => 1.2, 100 => 1.5, _ => 2.0 };
Assert.LessOrEqual(ratio, limit,
    $"collectors={collectors}: RW/RO ratio {ratio:F3}x exceeds {limit:F1}x");
```

- [ ] **Step 2: 运行并记录**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~Baseline_NonCachedRoVsRw" --verbosity normal`
Expected: PASS；若某一档未达标，先检查是否走了快路径（`CachedSlot` 已设置）、是否仍存在空转信号链，再优化后重跑。把结果追加到 baseline 文档。

> 可行性提醒（来自 Task 0 审查）：0 collector 档基线 ratio 2.438x，RW 专属开销约 86ns/次；< 1.2x 要求该开销 < 约 17ns。若失败，优先检查 Task 4 快路径与 Task 5 短路是否真正生效。

- [ ] **Step 3: Commit**

```bash
git add Test/PerformanceBaselineTestUnit.cs docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md
git commit -m "test(test): assert non-cached rw/ro ratio targets by collector count"
```

---

## Task 10: Flush 基准断言与全量回归

**Files:**
- Modify: `Test/PerformanceBaselineTestUnit.cs`
- Modify: `docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md`

- [ ] **Step 1: F1/F2/F3 断言**

用 Task 0 回填的 `PerformanceBaselineValues.F3Ms`（以及 F1/F2 记录值）做断言：

```csharp
[Test]
public void PostOptimization_FlushAndPipeline_DoNotRegress()
{
    var f1 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1, writesPerEntity: AccessIterations);
    var f2 = MeasureFlush(collectors: CollectorFanout, changedEntities: 1000, writesPerEntity: 1);
    var f3 = MeasurePipeline(collectors: CollectorFanout, writes: AccessIterations);
    Console.WriteLine($"[post] F1={f1:F3}ms F2={f2:F3}ms F3={f3:F3}ms");

    Assert.LessOrEqual(f3, PerformanceBaselineValues.F3Ms * 1.1,
        "write + flush pipeline must not regress against the pre-optimization baseline");
    Assert.LessOrEqual(f3, 2_000d,
        "post-optimization pipeline must be dramatically faster than the 12.5s baseline");
    Assert.Less(f2, 10_000d, "1000 collectors x 1000 entities settlement must stay bounded");
    Assert.Greater(f1, 0d);
}
```

> `f3 <= F3Ms * 1.1` 单独看几乎是空断言（12.5s × 1.1）；绝对上限 `2s` 才是真正的回归线。若 CI 机器较慢导致 2s 抖动，可上调到 5s，但仍应远低于基线。

- [ ] **Step 2: 运行基准**

Run: `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~Baseline_" --verbosity normal`
Expected: PASS；输出含改造后数值。把数值追加到 baseline 文档。

- [ ] **Step 3: 全量回归 + collector 用例零改动校验**

Run: `~/.dotnet/dotnet test --verbosity minimal`
Expected: 全部通过

Run: `git diff --stat HEAD~10 -- Test/EntityCollectorTestUnit.cs Test/CollectorAccelerationTestUnit.cs`
Expected: 无输出（零改动）

Run: `~/.dotnet/dotnet build --configuration Release --verbosity minimal 2>&1 | grep -E "error|warning CS" | grep -v "CS0649" | head`
Expected: 无新增错误/警告

- [ ] **Step 4: Commit**

```bash
git add Test/PerformanceBaselineTestUnit.cs docs/superpowers/plans/2026-09-18-rw-ro-performance-baseline.md
git commit -m "test(test): assert flush settlement targets and record post-optimization data"
```

---

## 自检记录

- **Spec 覆盖**：池化 core（Task 1/2）、热路径（Task 3/4）、信号链（Task 5）、journal + 结算（Task 6/7）、span（Task 8）、验收（Task 0/9/10）、collector 测试零改动门禁（Task 6 Step 5 / Task 10 Step 3）。
- **占位符**：无代码占位符；`PerformanceBaselineValues.F3Ms` 是 Task 0 实测回填的数据常量（Task 0 Step 2 明确回填步骤），Task 10 引用它。
- **类型一致性**：`ComponentRefCore.Bind/Reset/BindGeneration/CachedSlot/TryGetDenseSlot/GetSparseStore`、`Structure.GetDenseCore/SetDenseCore/ReleaseDenseCoresAt/GetDenseRefAt/BumpDenseRevisionAt/NotifyChanged`、`SparseStore.GetCore/SetCore/ReleaseCore`、`SparseComponentContainer.ReleaseCoresAt`、`IComponentChangeSink.OnRevisionChanged`、`EntityMatchManager.OnRevisionChanged/SettleRevisions/JournalCursor/JournalLogicalEnd`、`Collector.SettleRevision/MarkChanged`、`PerformanceBaselineValues.F3Ms` 在各任务间保持一致。
