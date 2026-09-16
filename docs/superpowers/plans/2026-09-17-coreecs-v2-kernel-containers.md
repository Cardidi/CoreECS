# CoreECS v2 内核容器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地 v2 archetype 内核的第一层：三组件接口、组件类型注册表、`EntityLocation`、`TagContainer`、SpareSet 容器、`Structure`（SoA）与 `StructureRegistry`，全部带单元测试，不改变现有 World 行为。

**Architecture:** 新增 `CoreECS.Structures` 命名空间承载纯数据结构；现有 v1 存储（`ComponentStore` / `EntityGraph`）与 `World` 集成保持不动，等 Plan 1b 再切换。每个类型独立 TDD、独立提交，全程 `dotnet build` / `dotnet test` 全绿。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Defines/IDiscreteComponent.cs` | SpareSet 组件标记接口 |
| `ECS/Defines/ITagComponent.cs` | Tag 组件标记接口 |
| `ECS/Defines/ComponentKind.cs` | 组件存储类别枚举 |
| `ECS/Structures/ComponentTypeRegistry.cs` | `ComponentTypeInfo` + 全局类型注册表（Type → TypeId/Kind） |
| `ECS/Structures/EntityLocation.cs` | 池化的实体位置锚点（Structure/Row/Generation） |
| `ECS/Structures/ComponentVersion.cs` | 全局组件实例版本号计数器 |
| `ECS/Structures/TagContainer.cs` | Structure 内每 row 的 tag 位图容器 |
| `ECS/Structures/DiscreteStore.cs` | 单 discrete 类型的 `row → 数据` 存储 |
| `ECS/Structures/SpareSetComponentContainer.cs` | Structure 的 discrete 容器集合 |
| `ECS/Structures/StructureKey.cs` | `(排序 Dense TypeId, Mask)` 组合键 |
| `ECS/Structures/Structure.cs` | archetype：Dense SoA + row 生命周期 + tag/discrete 操作 + 迁移辅助 |
| `ECS/Structures/StructureRegistry.cs` | 按 key 去重的 Structure 注册表 |

测试文件统一放 `Test/`，命名 `<TypeName>TestUnit.cs`，风格与现有测试一致（classic asserts）。

---

## Task 1: 组件接口扩展（IDiscreteComponent / ITagComponent）

**Files:**
- Create: `ECS/Defines/IDiscreteComponent.cs`
- Create: `ECS/Defines/ITagComponent.cs`
- Test: `Test/ComponentInterfaceTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/ComponentInterfaceTestUnit.cs`：

```csharp
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentInterfaceTestUnit
    {
        private struct DenseComponent : IComponent<DenseComponent>
        {
            public int Value;
        }

        private struct DiscreteComponent : IDiscreteComponent<DiscreteComponent>
        {
            public int Value;
        }

        private struct TagComponent : ITagComponent<TagComponent>
        {
        }

        private static T RequireComponent<T>(T value) where T : struct, IComponent<T>
        {
            return value;
        }

        [Test]
        public void DenseComponent_SatisfiesIComponentConstraint()
        {
            var component = RequireComponent(new DenseComponent { Value = 3 });
            Assert.AreEqual(3, component.Value);
        }

        [Test]
        public void DiscreteComponent_SatisfiesIComponentConstraint()
        {
            var component = RequireComponent(new DiscreteComponent { Value = 5 });
            Assert.AreEqual(5, component.Value);
        }

        [Test]
        public void TagComponent_SatisfiesIComponentConstraint()
        {
            RequireComponent(default(TagComponent));
            Assert.Pass();
        }

        [Test]
        public void DefaultLifecycleHooks_CanBeCalledOnTag()
        {
            IComponent<TagComponent> tag = default(TagComponent);
            tag.OnCreate(1UL);
            tag.OnDestroy(1UL);
            Assert.Pass();
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentInterfaceTestUnit`
Expected: 编译失败，`IDiscreteComponent` / `ITagComponent` 不存在

- [ ] **Step 3: 实现接口**

创建 `ECS/Defines/IDiscreteComponent.cs`：

```csharp
namespace CoreECS.Defines
{
    /// <summary>
    /// Marks a component as discrete: stored in a per-structure spare set.
    /// Discrete components do not participate in structure (archetype) membership.
    /// </summary>
    public interface IDiscreteComponent<T> : IComponent<T>
        where T : struct, IDiscreteComponent<T>
    {
    }
}
```

创建 `ECS/Defines/ITagComponent.cs`：

```csharp
namespace CoreECS.Defines
{
    /// <summary>
    /// Marks a component as a tag: stored as a per-entity bit, carries no data.
    /// Tag components do not participate in structure (archetype) membership.
    /// </summary>
    public interface ITagComponent<T> : IComponent<T>
        where T : struct, ITagComponent<T>
    {
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentInterfaceTestUnit`
Expected: PASS（4 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Defines/IDiscreteComponent.cs ECS/Defines/ITagComponent.cs Test/ComponentInterfaceTestUnit.cs
git commit -m "feat(core): add IDiscreteComponent and ITagComponent interfaces"
```

---

## Task 2: 组件类型注册表（ComponentTypeRegistry）

**Files:**
- Create: `ECS/Defines/ComponentKind.cs`
- Create: `ECS/Structures/ComponentTypeRegistry.cs`
- Test: `Test/ComponentTypeRegistryTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/ComponentTypeRegistryTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentTypeRegistryTestUnit
    {
        private struct RegistryDense : IComponent<RegistryDense>
        {
        }

        private struct RegistryDiscrete : IDiscreteComponent<RegistryDiscrete>
        {
        }

        private struct RegistryTag : ITagComponent<RegistryTag>
        {
        }

        [Test]
        public void ResolveKind_DetectsDense()
        {
            Assert.AreEqual(ComponentKind.Dense, ComponentTypeRegistry.ResolveKind(typeof(RegistryDense)));
        }

        [Test]
        public void ResolveKind_DetectsDiscrete()
        {
            Assert.AreEqual(ComponentKind.Discrete, ComponentTypeRegistry.ResolveKind(typeof(RegistryDiscrete)));
        }

        [Test]
        public void ResolveKind_DetectsTag()
        {
            Assert.AreEqual(ComponentKind.Tag, ComponentTypeRegistry.ResolveKind(typeof(RegistryTag)));
        }

        [Test]
        public void ResolveKind_RejectsNonComponentType()
        {
            Assert.Throws<ArgumentException>(() => ComponentTypeRegistry.ResolveKind(typeof(int)));
        }

        [Test]
        public void GetOrRegister_IsIdempotent()
        {
            var first = ComponentTypeRegistry.GetOrRegister<RegistryDense>();
            var second = ComponentTypeRegistry.GetOrRegister<RegistryDense>();

            Assert.AreEqual(first.TypeId, second.TypeId);
            Assert.AreEqual(ComponentKind.Dense, first.Kind);
            Assert.AreEqual(typeof(RegistryDense), first.Type);
        }

        [Test]
        public void GetOrRegister_AssignsUniqueIds()
        {
            var a = ComponentTypeRegistry.GetOrRegister<RegistryDense>();
            var b = ComponentTypeRegistry.GetOrRegister<RegistryDiscrete>();
            var c = ComponentTypeRegistry.GetOrRegister<RegistryTag>();

            Assert.AreNotEqual(a.TypeId, b.TypeId);
            Assert.AreNotEqual(b.TypeId, c.TypeId);
            Assert.AreNotEqual(a.TypeId, c.TypeId);
        }

        [Test]
        public void GetById_ReturnsRegisteredInfo()
        {
            var registered = ComponentTypeRegistry.GetOrRegister<RegistryTag>();
            var fetched = ComponentTypeRegistry.GetById(registered.TypeId);

            Assert.AreEqual(typeof(RegistryTag), fetched.Type);
            Assert.AreEqual(ComponentKind.Tag, fetched.Kind);
        }

        [Test]
        public void GetById_ThrowsForUnknownId()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentTypeRegistry.GetById(uint.MaxValue));
        }

        private struct RegistryConcurrent : IComponent<RegistryConcurrent>
        {
        }

        [Test]
        public void GetOrRegister_ConcurrentFirstRegistration_KeepsIdsConsistent()
        {
            const int threadCount = 32;
            using var barrier = new Barrier(threadCount);
            var results = new ComponentTypeInfo[threadCount];
            var threads = new Thread[threadCount];

            for (var i = 0; i < threadCount; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    barrier.SignalAndWait();
                    results[index] = ComponentTypeRegistry.GetOrRegister<RegistryConcurrent>();
                });
                threads[i].Start();
            }

            foreach (var thread in threads) thread.Join();

            var canonical = results[0];
            for (var i = 0; i < threadCount; i++)
            {
                Assert.AreEqual(canonical.TypeId, results[i].TypeId);
                Assert.AreEqual(canonical.TypeId, ComponentTypeRegistry.GetById(results[i].TypeId).TypeId);
            }

            Assert.AreEqual(ComponentTypeRegistry.RegisteredTypeCount, ComponentTypeRegistry.RegisteredIdCount);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentTypeRegistryTestUnit`
Expected: 编译失败，`ComponentKind` / `ComponentTypeRegistry` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Defines/ComponentKind.cs`：

```csharp
namespace CoreECS.Defines
{
    /// <summary>
    /// Storage category of a component type.
    /// </summary>
    public enum ComponentKind : byte
    {
        /// <summary>Stored in structure SoA arrays; participates in structure membership.</summary>
        Dense = 0,

        /// <summary>Stored in a per-structure spare set; does not affect structure membership.</summary>
        Discrete = 1,

        /// <summary>Stored as a per-entity bit; carries no data; does not affect structure membership.</summary>
        Tag = 2,
    }
}
```

创建 `ECS/Structures/ComponentTypeRegistry.cs`：

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Immutable metadata describing a registered component type.
    /// </summary>
    public readonly struct ComponentTypeInfo
    {
        /// <summary>The component struct type.</summary>
        public readonly Type Type;

        /// <summary>Stable id assigned on first registration; starts at 1.</summary>
        public readonly uint TypeId;

        /// <summary>Storage category of the component type.</summary>
        public readonly ComponentKind Kind;

        internal ComponentTypeInfo(Type type, uint typeId, ComponentKind kind)
        {
            Type = type;
            TypeId = typeId;
            Kind = kind;
        }
    }

    /// <summary>
    /// Global registry mapping component types to stable ids and storage kinds.
    /// Registration is append-only: ids are never reused or reassigned.
    /// </summary>
    public static class ComponentTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, ComponentTypeInfo> s_byType = new();
        private static readonly ConcurrentDictionary<uint, ComponentTypeInfo> s_byId = new();
        private static readonly object s_lock = new();
        private static int s_nextId = 0;

        /// <summary>Number of registered type entries (test hook).</summary>
        internal static int RegisteredTypeCount => s_byType.Count;

        /// <summary>Number of registered id entries (test hook); always equals <see cref="RegisteredTypeCount"/>.</summary>
        internal static int RegisteredIdCount => s_byId.Count;

        /// <summary>
        /// Gets metadata for a component type, registering it on first use.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component type.</exception>
        public static ComponentTypeInfo GetOrRegister<T>() where T : struct, IComponent<T>
        {
            return GetOrRegister(typeof(T));
        }

        /// <summary>
        /// Gets metadata for a component type, registering it on first use.
        /// Registration is serialized under a lock so a losing concurrent registration
        /// can never publish an orphan id into the id map.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component type.</exception>
        public static ComponentTypeInfo GetOrRegister(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (s_byType.TryGetValue(type, out var registered)) return registered;

            lock (s_lock)
            {
                if (s_byType.TryGetValue(type, out registered)) return registered;

                var kind = ResolveKind(type);
                var id = (uint)Interlocked.Increment(ref s_nextId);
                var info = new ComponentTypeInfo(type, id, kind);
                s_byId[id] = info;
                s_byType[type] = info;
                return info;
            }
        }

        /// <summary>
        /// Tries to get metadata without registering the type.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        public static bool TryGet(Type type, out ComponentTypeInfo info)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return s_byType.TryGetValue(type, out info);
        }

        /// <summary>
        /// Gets metadata for a registered type id.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the id is not registered.</exception>
        public static ComponentTypeInfo GetById(uint typeId)
        {
            if (s_byId.TryGetValue(typeId, out var info)) return info;

            throw new ArgumentOutOfRangeException(
                nameof(typeId), $"Component type id {typeId} is not registered.");
        }

        /// <summary>
        /// Resolves the storage kind of a component type by its most derived interface
        /// (Tag &gt; Discrete &gt; Dense).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="type"/> is not a component struct type.</exception>
        public static ComponentKind ResolveKind(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.IsValueType)
            {
                throw new ArgumentException(
                    $"{type.FullName} is not a CoreECS component type; components must be structs.",
                    nameof(type));
            }

            if (ImplementsOpenGeneric(type, typeof(ITagComponent<>))) return ComponentKind.Tag;
            if (ImplementsOpenGeneric(type, typeof(IDiscreteComponent<>))) return ComponentKind.Discrete;
            if (ImplementsOpenGeneric(type, typeof(IComponent<>))) return ComponentKind.Dense;

            throw new ArgumentException(
                $"{type.FullName} is not a CoreECS component type.", nameof(type));
        }

        private static bool ImplementsOpenGeneric(Type type, Type openGeneric)
        {
            foreach (var implemented in type.GetInterfaces())
            {
                if (implemented.IsGenericType &&
                    implemented.GetGenericTypeDefinition() == openGeneric)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentTypeRegistryTestUnit`
Expected: PASS（9 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Defines/ComponentKind.cs ECS/Structures/ComponentTypeRegistry.cs Test/ComponentTypeRegistryTestUnit.cs
git commit -m "feat(core): add component type registry with kind detection"
```

---

## Task 3: ComponentVersion（组件实例版本计数器）

> 注：`EntityLocation` 依赖 `Structure`，而 `Structure` 又依赖 `EntityLocation`，二者必须同任务落地；因此 `EntityLocation` 移到 Task 7。

**Files:**
- Create: `ECS/Structures/ComponentVersion.cs`
- Test: `Test/ComponentVersionTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/ComponentVersionTestUnit.cs`：

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentVersionTestUnit
    {
        [Test]
        public void Next_ReturnsUniqueIncreasingValues()
        {
            var first = ComponentVersion.Next();
            var second = ComponentVersion.Next();

            Assert.AreNotEqual(first, second);
            Assert.Greater(second, first);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentVersionTestUnit`
Expected: 编译失败，`ComponentVersion` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/ComponentVersion.cs`：

```csharp
using System.Threading;

namespace CoreECS.Structures
{
    /// <summary>
    /// Supplies process-wide component instance versions.
    /// Re-adding a component gets a fresh version, so refs created before the removal
    /// can never match the new instance.
    /// </summary>
    public static class ComponentVersion
    {
        private static int s_next = 0;

        /// <summary>
        /// Gets the next version value. Wraps after uint.MaxValue increments;
        /// version 0 is allowed (not used as a sentinel).
        /// </summary>
        public static uint Next()
        {
            return (uint)Interlocked.Increment(ref s_next);
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~ComponentVersionTestUnit`
Expected: PASS（1 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/ComponentVersion.cs Test/ComponentVersionTestUnit.cs
git commit -m "feat(core): add component version counter"
```

---

## Task 4: TagContainer（每 row tag 位图）

**Files:**
- Create: `ECS/Structures/TagContainer.cs`
- Test: `Test/TagContainerTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/TagContainerTestUnit.cs`：

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class TagContainerTestUnit
    {
        [Test]
        public void Add_Has_Remove_WorkOnSameRow()
        {
            var tags = new TagContainer();
            tags.AddRow();

            Assert.IsTrue(tags.Add(0, 1));
            Assert.IsTrue(tags.Has(0, 1));
            Assert.IsFalse(tags.Add(0, 1));
            Assert.IsTrue(tags.Remove(0, 1));
            Assert.IsFalse(tags.Has(0, 1));
        }

        [Test]
        public void Has_ReturnsFalseForUnknownRowOrTag()
        {
            var tags = new TagContainer();

            Assert.IsFalse(tags.Has(0, 1));

            tags.AddRow();
            Assert.IsFalse(tags.Has(0, 1));
        }

        [Test]
        public void Add_SupportsTagIdsBeyond64Bits()
        {
            var tags = new TagContainer();
            tags.AddRow();

            Assert.IsTrue(tags.Add(0, 130));
            Assert.IsTrue(tags.Has(0, 130));
            Assert.IsFalse(tags.Has(0, 129));
        }

        [Test]
        public void WidthGrowth_PreservesExistingRows()
        {
            var tags = new TagContainer();
            tags.AddRow();
            tags.AddRow();
            tags.Add(0, 1);

            tags.Add(1, 65);

            Assert.IsTrue(tags.Has(0, 1));
            Assert.IsTrue(tags.Has(1, 65));
        }

        [Test]
        public void RemoveRowSwap_MovesLastRowIntoRemovedSlot()
        {
            var tags = new TagContainer();
            tags.AddRow();
            tags.AddRow();
            tags.Add(1, 5);

            tags.RemoveRowSwap(0);

            Assert.AreEqual(1, tags.Count);
            Assert.IsTrue(tags.Has(0, 5));
        }

        [Test]
        public void CopyRowTo_TransfersWordsAcrossDifferentWidths()
        {
            var source = new TagContainer();
            source.AddRow();
            source.Add(0, 130);

            var target = new TagContainer();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            Assert.IsTrue(target.Has(0, 130));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~TagContainerTestUnit`
Expected: 编译失败，`TagContainer` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/TagContainer.cs`：

```csharp
using System;

namespace CoreECS.Structures
{
    /// <summary>
    /// Per-row tag bitmaps owned by a Structure.
    /// Rows are addressed by structure row index; width grows as tag types register.
    /// Words are stored row-major: row * WordCount + word.
    /// </summary>
    public sealed class TagContainer
    {
        private const int InitialRowCapacity = 8;

        private ulong[] m_words = Array.Empty<ulong>();
        private int m_wordCount;
        private int m_rowCapacity;
        private int m_count;

        /// <summary>Number of live rows.</summary>
        public int Count => m_count;

        /// <summary>Appends an empty row.</summary>
        public void AddRow()
        {
            EnsureRowCapacity(m_count + 1);
            ClearRow(m_count);
            m_count += 1;
        }

        /// <summary>
        /// Removes a row by moving the last row into its slot.
        /// </summary>
        public void RemoveRowSwap(int row)
        {
            var last = m_count - 1;
            if (row != last) CopyRow(last, row);
            ClearRow(last);
            m_count -= 1;
        }

        /// <summary>Checks whether the row carries the tag.</summary>
        public bool Has(int row, uint tagId)
        {
            var word = (int)(tagId >> 6);
            if (word >= m_wordCount) return false;

            return (m_words[row * m_wordCount + word] & (1UL << (int)(tagId & 63))) != 0;
        }

        /// <summary>Adds the tag to the row; returns false when already present.</summary>
        public bool Add(int row, uint tagId)
        {
            EnsureWidth(tagId);

            var index = row * m_wordCount + (int)(tagId >> 6);
            var bit = 1UL << (int)(tagId & 63);
            if ((m_words[index] & bit) != 0) return false;

            m_words[index] |= bit;
            return true;
        }

        /// <summary>Removes the tag from the row; returns false when absent.</summary>
        public bool Remove(int row, uint tagId)
        {
            var word = (int)(tagId >> 6);
            if (word >= m_wordCount) return false;

            var index = row * m_wordCount + word;
            var bit = 1UL << (int)(tagId & 63);
            if ((m_words[index] & bit) == 0) return false;

            m_words[index] &= ~bit;
            return true;
        }

        /// <summary>
        /// Copies one row into another container, widening the target when needed.
        /// </summary>
        public void CopyRowTo(int sourceRow, TagContainer target, int targetRow)
        {
            target.EnsureWordCount(m_wordCount);
            target.EnsureRowCapacity(targetRow + 1);

            for (var word = 0; word < m_wordCount; word++)
            {
                target.m_words[targetRow * target.m_wordCount + word] =
                    m_words[sourceRow * m_wordCount + word];
            }
        }

        private void EnsureRowCapacity(int rows)
        {
            if (rows <= m_rowCapacity) return;

            var newCapacity = Math.Max(rows, Math.Max(InitialRowCapacity, m_rowCapacity * 2));
            var newWords = new ulong[newCapacity * m_wordCount];
            if (m_words.Length > 0) Array.Copy(m_words, newWords, m_words.Length);

            m_words = newWords;
            m_rowCapacity = newCapacity;
        }

        private void EnsureWordCount(int words)
        {
            if (words <= m_wordCount) return;

            var newWords = new ulong[m_rowCapacity * words];
            for (var row = 0; row < m_count; row++)
            {
                for (var word = 0; word < m_wordCount; word++)
                {
                    newWords[row * words + word] = m_words[row * m_wordCount + word];
                }
            }

            m_words = newWords;
            m_wordCount = words;
        }

        private void EnsureWidth(uint tagId)
        {
            EnsureWordCount((int)(tagId >> 6) + 1);
        }

        private void CopyRow(int fromRow, int toRow)
        {
            for (var word = 0; word < m_wordCount; word++)
            {
                m_words[toRow * m_wordCount + word] = m_words[fromRow * m_wordCount + word];
            }
        }

        private void ClearRow(int row)
        {
            for (var word = 0; word < m_wordCount; word++)
            {
                m_words[row * m_wordCount + word] = 0;
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~TagContainerTestUnit`
Expected: PASS（6 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/TagContainer.cs Test/TagContainerTestUnit.cs
git commit -m "feat(core): add per-row tag container"
```

---

## Task 5: DiscreteStore 与 SpareSetComponentContainer

**Files:**
- Create: `ECS/Structures/DiscreteStore.cs`
- Create: `ECS/Structures/SpareSetComponentContainer.cs`
- Test: `Test/SpareSetComponentContainerTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/SpareSetComponentContainerTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class SpareSetComponentContainerTestUnit
    {
        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct RageComponent : IDiscreteComponent<RageComponent>
        {
            public int Value;
        }

        [Test]
        public void Store_Set_Get_Has_Remove()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            Assert.IsFalse(store.Has(0));

            store.Set(0, new ManaComponent { Value = 5 }, 11);

            Assert.IsTrue(store.Has(0));
            Assert.AreEqual(5, store.Get(0).Value);
            Assert.AreEqual(11u, store.GetVersion(0));

            store.Remove(0);

            Assert.IsFalse(store.Has(0));
            Assert.AreEqual(0u, store.GetVersion(0));
        }

        [Test]
        public void ChangeRevision_Increments()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            store.Set(0, default, 1);

            Assert.AreEqual(1u, store.ChangeRevision(0));
            Assert.AreEqual(2u, store.ChangeRevision(0));
        }

        [Test]
        public void RemoveRowSwap_MovesPresenceAndData()
        {
            var store = new DiscreteStore<ManaComponent>();
            store.AddRow();
            store.AddRow();
            store.Set(1, new ManaComponent { Value = 9 }, 3);

            store.RemoveRowSwap(0);

            Assert.AreEqual(1, store.Count);
            Assert.IsTrue(store.Has(0));
            Assert.AreEqual(9, store.Get(0).Value);
            Assert.AreEqual(3u, store.GetVersion(0));
        }

        [Test]
        public void CopyRowTo_TransfersDataVersionAndRevision()
        {
            var source = new DiscreteStore<ManaComponent>();
            source.AddRow();
            source.Set(0, new ManaComponent { Value = 7 }, 42);
            source.ChangeRevision(0);

            var target = new DiscreteStore<ManaComponent>();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            Assert.IsTrue(target.Has(0));
            Assert.AreEqual(7, target.Get(0).Value);
            Assert.AreEqual(42u, target.GetVersion(0));
            Assert.AreEqual(1u, target.GetRevision(0));
        }

        [Test]
        public void Container_ManagesIndependentStores()
        {
            var container = new SpareSetComponentContainer();
            container.AddRow();

            var mana = container.GetOrCreateStore<ManaComponent>();
            var rage = container.GetOrCreateStore<RageComponent>();
            mana.Set(0, new ManaComponent { Value = 1 }, 1);
            rage.Set(0, new RageComponent { Value = 2 }, 1);

            Assert.IsTrue(container.Has(mana.TypeId, 0));
            Assert.IsTrue(container.Has(rage.TypeId, 0));
            Assert.AreSame(mana, container.GetOrCreateStore<ManaComponent>());
        }

        [Test]
        public void Container_CopyRowTo_CreatesTargetStores()
        {
            var source = new SpareSetComponentContainer();
            source.AddRow();
            var mana = source.GetOrCreateStore<ManaComponent>();
            mana.Set(0, new ManaComponent { Value = 3 }, 8);

            var target = new SpareSetComponentContainer();
            target.AddRow();

            source.CopyRowTo(0, target, 0);

            var copied = target.GetOrCreateStore<ManaComponent>();
            Assert.IsTrue(copied.Has(0));
            Assert.AreEqual(3, copied.Get(0).Value);
            Assert.AreEqual(8u, copied.GetVersion(0));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~SpareSetComponentContainerTestUnit`
Expected: 编译失败，`DiscreteStore` / `SpareSetComponentContainer` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/DiscreteStore.cs`：

```csharp
using System;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Non-generic base for a per-structure store of one discrete component type.
    /// </summary>
    public abstract class DiscreteStore
    {
        /// <summary>Registered type id of the stored component.</summary>
        public abstract uint TypeId { get; }

        /// <summary>Number of rows (mirrors the owning structure row count).</summary>
        public abstract int Count { get; }

        /// <summary>Checks whether the row has the component.</summary>
        public abstract bool Has(int row);

        /// <summary>Removes the component from the row.</summary>
        public abstract void Remove(int row);

        /// <summary>Appends an empty row.</summary>
        public abstract void AddRow();

        /// <summary>Removes a row by moving the last row into its slot.</summary>
        public abstract void RemoveRowSwap(int row);

        /// <summary>Creates an empty store of the same concrete type.</summary>
        public abstract DiscreteStore CreateEmpty();

        /// <summary>Copies one row into another store, including version and revision.</summary>
        public abstract void CopyRowTo(int sourceRow, DiscreteStore target, int targetRow);

        /// <summary>Gets the component instance version at the row.</summary>
        public abstract uint GetVersion(int row);

        /// <summary>Gets the modification revision at the row.</summary>
        public abstract uint GetRevision(int row);

        /// <summary>Bumps and returns the modification revision at the row.</summary>
        public abstract uint ChangeRevision(int row);
    }

    /// <summary>
    /// Spare-set storage for a single discrete component type inside one structure.
    /// Data arrays are row-aligned; presence is tracked with a bitmap.
    /// </summary>
    public sealed class DiscreteStore<T> : DiscreteStore
        where T : struct, IDiscreteComponent<T>
    {
        private const int InitialCapacity = 8;

        private T[] m_data = new T[InitialCapacity];
        private ulong[] m_present = new ulong[1];
        private uint[] m_versions = new uint[InitialCapacity];
        private uint[] m_revisions = new uint[InitialCapacity];
        private int m_capacity = InitialCapacity;
        private int m_count;

        /// <inheritdoc />
        public override uint TypeId => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        /// <inheritdoc />
        public override int Count => m_count;

        /// <inheritdoc />
        public override bool Has(int row)
        {
            if (row < 0 || row >= m_count) return false;

            return (m_present[row >> 6] & (1UL << (row & 63))) != 0;
        }

        /// <summary>
        /// Writes the component at the row, stamping it with the given version
        /// and resetting its revision.
        /// </summary>
        public void Set(int row, in T value, uint version)
        {
            EnsureCapacity(row + 1);
            m_data[row] = value;
            m_versions[row] = version;
            m_revisions[row] = 0;
            SetPresence(row, true);
        }

        /// <summary>Gets a writable reference to the component data at the row.</summary>
        public ref T Get(int row)
        {
            return ref m_data[row];
        }

        /// <inheritdoc />
        public override uint GetVersion(int row) => m_versions[row];

        /// <inheritdoc />
        public override uint GetRevision(int row) => m_revisions[row];

        /// <inheritdoc />
        public override uint ChangeRevision(int row)
        {
            var revision = (m_revisions[row] % uint.MaxValue) + 1;
            m_revisions[row] = revision;
            return revision;
        }

        /// <inheritdoc />
        public override void Remove(int row)
        {
            if (!Has(row)) return;

            SetPresence(row, false);
            m_data[row] = default;
            m_versions[row] = 0;
            m_revisions[row] = 0;
        }

        /// <inheritdoc />
        public override void AddRow()
        {
            EnsureCapacity(m_count + 1);
            m_count += 1;
        }

        /// <inheritdoc />
        public override void RemoveRowSwap(int row)
        {
            var last = m_count - 1;
            if (row != last)
            {
                m_data[row] = m_data[last];
                m_versions[row] = m_versions[last];
                m_revisions[row] = m_revisions[last];
                SetPresence(row, Has(last));
            }

            ClearSlot(last);
            m_count -= 1;
        }

        /// <inheritdoc />
        public override DiscreteStore CreateEmpty() => new DiscreteStore<T>();

        /// <inheritdoc />
        public override void CopyRowTo(int sourceRow, DiscreteStore target, int targetRow)
        {
            var typed = (DiscreteStore<T>)target;
            typed.EnsureCapacity(targetRow + 1);

            if (!Has(sourceRow))
            {
                typed.Remove(targetRow);
                return;
            }

            typed.m_data[targetRow] = m_data[sourceRow];
            typed.m_versions[targetRow] = m_versions[sourceRow];
            typed.m_revisions[targetRow] = m_revisions[sourceRow];
            typed.SetPresence(targetRow, true);
        }

        private void EnsureCapacity(int rows)
        {
            if (rows <= m_capacity) return;

            var newCapacity = Math.Max(rows, Math.Max(InitialCapacity, m_capacity * 2));
            Array.Resize(ref m_data, newCapacity);
            Array.Resize(ref m_versions, newCapacity);
            Array.Resize(ref m_revisions, newCapacity);
            Array.Resize(ref m_present, (newCapacity + 63) >> 6);
            m_capacity = newCapacity;
        }

        private void SetPresence(int row, bool present)
        {
            var word = row >> 6;
            var bit = 1UL << (row & 63);
            if (present) m_present[word] |= bit;
            else m_present[word] &= ~bit;
        }

        private void ClearSlot(int row)
        {
            m_data[row] = default;
            m_versions[row] = 0;
            m_revisions[row] = 0;
            SetPresence(row, false);
        }
    }
}
```

创建 `ECS/Structures/SpareSetComponentContainer.cs`：

```csharp
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Collection of discrete component stores attached to one structure.
    /// Stores are created lazily per discrete component type.
    /// </summary>
    public sealed class SpareSetComponentContainer
    {
        private readonly Dictionary<uint, DiscreteStore> m_stores = new();

        /// <summary>Number of discrete component types present in this container.</summary>
        public int StoreCount => m_stores.Count;

        /// <summary>Gets the store for a type id, or null when absent.</summary>
        public DiscreteStore GetStore(uint typeId)
        {
            return m_stores.TryGetValue(typeId, out var store) ? store : null;
        }

        /// <summary>Gets or creates the store for a discrete component type.</summary>
        public DiscreteStore<T> GetOrCreateStore<T>() where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            if (m_stores.TryGetValue(typeId, out var existing))
            {
                return (DiscreteStore<T>)existing;
            }

            var created = new DiscreteStore<T>();
            m_stores.Add(typeId, created);
            return created;
        }

        /// <summary>Checks whether the row has the discrete component.</summary>
        public bool Has(uint typeId, int row)
        {
            var store = GetStore(typeId);
            return store != null && store.Has(row);
        }

        /// <summary>Appends an empty row to every store.</summary>
        public void AddRow()
        {
            foreach (var store in m_stores.Values)
            {
                store.AddRow();
            }
        }

        /// <summary>Removes a row (swap-remove) from every store.</summary>
        public void RemoveRowSwap(int row)
        {
            foreach (var store in m_stores.Values)
            {
                store.RemoveRowSwap(row);
            }
        }

        /// <summary>Copies one row into another container, creating target stores as needed.</summary>
        public void CopyRowTo(int sourceRow, SpareSetComponentContainer target, int targetRow)
        {
            foreach (var pair in m_stores)
            {
                if (!pair.Value.Has(sourceRow)) continue;

                if (!target.m_stores.TryGetValue(pair.Key, out var targetStore))
                {
                    targetStore = pair.Value.CreateEmpty();
                    target.m_stores.Add(pair.Key, targetStore);
                }

                pair.Value.CopyRowTo(sourceRow, targetStore, targetRow);
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~SpareSetComponentContainerTestUnit`
Expected: PASS（6 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/DiscreteStore.cs ECS/Structures/SpareSetComponentContainer.cs Test/SpareSetComponentContainerTestUnit.cs
git commit -m "feat(core): add discrete component spare set container"
```

---

## Task 6: StructureKey（archetype 组合键）

**Files:**
- Create: `ECS/Structures/StructureKey.cs`
- Test: `Test/StructureKeyTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/StructureKeyTestUnit.cs`：

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureKeyTestUnit
    {
        [Test]
        public void Equality_ComparesMaskAndSortedIds()
        {
            var a = new StructureKey(new uint[] { 1, 2, 3 }, 0b101);
            var b = new StructureKey(new uint[] { 1, 2, 3 }, 0b101);
            var differentIds = new StructureKey(new uint[] { 1, 2, 4 }, 0b101);
            var differentMask = new StructureKey(new uint[] { 1, 2, 3 }, 0b110);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, differentIds);
            Assert.AreNotEqual(a, differentMask);
        }

        [Test]
        public void Equality_HandlesEmptyComposition()
        {
            var a = new StructureKey(null, 7);
            var b = new StructureKey(System.Array.Empty<uint>(), 7);

            Assert.AreEqual(a, b);
            Assert.AreEqual(0, a.DenseCount);
        }

        [Test]
        public void AddType_KeepsIdsSortedAndDeduplicates()
        {
            var ids = StructureKey.AddType(new uint[] { 2, 5 }, 3);
            CollectionAssert.AreEqual(new uint[] { 2, 3, 5 }, ids);

            var same = StructureKey.AddType(ids, 3);
            Assert.AreSame(ids, same);
        }

        [Test]
        public void AddType_HandlesEmptySource()
        {
            var ids = StructureKey.AddType(null, 9);
            CollectionAssert.AreEqual(new uint[] { 9 }, ids);
        }

        [Test]
        public void RemoveType_KeepsIdsSorted()
        {
            var ids = StructureKey.RemoveType(new uint[] { 2, 3, 5 }, 3);
            CollectionAssert.AreEqual(new uint[] { 2, 5 }, ids);
        }

        [Test]
        public void RemoveType_IgnoresMissingId()
        {
            var ids = StructureKey.RemoveType(new uint[] { 2, 5 }, 4);
            CollectionAssert.AreEqual(new uint[] { 2, 5 }, ids);
        }

        [Test]
        public void ToArray_ReturnsDefensiveCopy()
        {
            var key = new StructureKey(new uint[] { 1, 2 }, 0);
            var copy = key.ToArray();
            copy[0] = 99;

            Assert.AreEqual(1u, key.DenseTypeIds[0]);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureKeyTestUnit`
Expected: 编译失败，`StructureKey` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/StructureKey.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Identity of an archetype: the sorted dense component type ids plus the entity mask.
    /// Callers must pass an already sorted, never-mutated id array.
    /// </summary>
    public readonly struct StructureKey : IEquatable<StructureKey>
    {
        private readonly uint[] m_denseTypeIds;

        /// <summary>Entity mask; part of archetype identity.</summary>
        public readonly ulong Mask;

        /// <summary>Sorted dense component type ids.</summary>
        public IReadOnlyList<uint> DenseTypeIds => m_denseTypeIds ?? Array.Empty<uint>();

        /// <summary>Number of dense component types.</summary>
        public int DenseCount => m_denseTypeIds?.Length ?? 0;

        /// <summary>
        /// Creates a key from sorted dense type ids and a mask.
        /// </summary>
        public StructureKey(uint[] sortedDenseTypeIds, ulong mask)
        {
            m_denseTypeIds = sortedDenseTypeIds ?? Array.Empty<uint>();
            Mask = mask;
        }

        /// <summary>Returns a defensive copy of the dense type ids.</summary>
        public uint[] ToArray()
        {
            var length = m_denseTypeIds?.Length ?? 0;
            var copy = new uint[length];
            if (length > 0) Array.Copy(m_denseTypeIds, copy, length);
            return copy;
        }

        /// <summary>
        /// Returns a new sorted id array with the type added.
        /// Returns the same array instance when the type is already present.
        /// </summary>
        public static uint[] AddType(uint[] sortedIds, uint typeId)
        {
            var source = sortedIds ?? Array.Empty<uint>();
            var index = Array.BinarySearch(source, typeId);
            if (index >= 0) return source;

            index = ~index;
            var result = new uint[source.Length + 1];
            Array.Copy(source, 0, result, 0, index);
            result[index] = typeId;
            Array.Copy(source, index, result, index + 1, source.Length - index);
            return result;
        }

        /// <summary>
        /// Returns a new sorted id array with the type removed.
        /// Returns the same array instance when the type is absent.
        /// </summary>
        public static uint[] RemoveType(uint[] sortedIds, uint typeId)
        {
            var source = sortedIds ?? Array.Empty<uint>();
            var index = Array.BinarySearch(source, typeId);
            if (index < 0) return source;

            var result = new uint[source.Length - 1];
            Array.Copy(source, 0, result, 0, index);
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);
            return result;
        }

        /// <inheritdoc />
        public bool Equals(StructureKey other)
        {
            if (Mask != other.Mask) return false;

            var mine = m_denseTypeIds;
            var theirs = other.m_denseTypeIds;
            var myLength = mine?.Length ?? 0;
            var theirLength = theirs?.Length ?? 0;
            if (myLength != theirLength) return false;

            for (var i = 0; i < myLength; i++)
            {
                if (mine[i] != theirs[i]) return false;
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StructureKey other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)(Mask ^ (Mask >> 32)) * 397;
                if (m_denseTypeIds != null)
                {
                    for (var i = 0; i < m_denseTypeIds.Length; i++)
                    {
                        hash = hash * 31 + (int)m_denseTypeIds[i];
                    }
                }

                return hash;
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureKeyTestUnit`
Expected: PASS（7 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/StructureKey.cs Test/StructureKeyTestUnit.cs
git commit -m "feat(core): add structure key for archetype identity"
```

---

## Task 7: Structure（Dense SoA + row 生命周期）

> 注：`EntityLocation` 与 `Structure` 相互引用，必须同任务落地（由原 Task 3 移入）。

**Files:**
- Create: `ECS/Structures/EntityLocation.cs`
- Create: `ECS/Structures/Structure.cs`
- Test: `Test/EntityLocationTestUnit.cs`
- Test: `Test/StructureTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/EntityLocationTestUnit.cs`：

```csharp
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityLocationTestUnit
    {
        [Test]
        public void Release_ResetsStructureAndRow()
        {
            var location = EntityLocation.Pool.Get();
            location.Row = 7;
            EntityLocation.Pool.Release(location);

            var reused = EntityLocation.Pool.Get();
            Assert.IsNull(reused.Structure);
            Assert.AreEqual(-1, reused.Row);
        }

        [Test]
        public void Release_AdvancesGenerationOfReleasedInstance()
        {
            var location = EntityLocation.Pool.Get();
            var generation = location.Generation;

            EntityLocation.Pool.Release(location);

            Assert.AreEqual(generation + 1, location.Generation);
        }
    }
}
```

创建 `Test/StructureTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId) => Added.Add((typeId, row));
            public void OnComponentRemoved(Structure structure, int row, uint typeId) => Removed.Add((typeId, row));
            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakePositionStructure()
        {
            return new Structure(new StructureKey(new[] { IdOf<Position>() }, 0));
        }

        [Test]
        public void Append_TracksEntityAndLocation()
        {
            var structure = MakePositionStructure();
            var location = EntityLocation.Pool.Get();

            var row = structure.Append(42, location);

            Assert.AreEqual(0, row);
            Assert.AreEqual(1, structure.Count);
            Assert.AreSame(structure, location.Structure);
            Assert.AreEqual(0, location.Row);
            Assert.AreEqual(42UL, structure.Entities[0]);
        }

        [Test]
        public void SwapRemove_MovesLastEntityAndFixesLocation()
        {
            var structure = MakePositionStructure();
            var a = EntityLocation.Pool.Get();
            var b = EntityLocation.Pool.Get();
            var c = EntityLocation.Pool.Get();
            structure.Append(1, a);
            structure.Append(2, b);
            structure.Append(3, c);

            structure.SwapRemove(0);

            Assert.AreEqual(2, structure.Count);
            Assert.AreEqual(3UL, structure.Entities[0]);
            Assert.AreEqual(0, c.Row);
            Assert.AreEqual(2UL, structure.Entities[1]);
            Assert.AreEqual(1, b.Row);
        }

        [Test]
        public void HasDense_ReflectsComposition()
        {
            var structure = MakePositionStructure();

            Assert.IsTrue(structure.HasDense(IdOf<Position>()));
            Assert.IsFalse(structure.HasDense(IdOf<Velocity>()));
        }

        [Test]
        public void RO_ReturnsRowAlignedSpan()
        {
            var structure = MakePositionStructure();
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = 9 }, 1);

            var span = structure.RO<Position>();

            Assert.AreEqual(1, span.Length);
            Assert.AreEqual(9, span[0].X);
        }

        [Test]
        public void RW_BumpsRevisionAndNotifiesObserver()
        {
            var structure = MakePositionStructure();
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, default(Position), 1);

            var span = structure.RW<Position>();
            span[0].X = 5;

            Assert.AreEqual(5, structure.RO<Position>()[0].X);
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
            Assert.AreEqual(1, observer.Changed.Count);
            Assert.AreEqual(IdOf<Position>(), observer.Changed[0].TypeId);
            Assert.AreEqual(0, observer.Changed[0].Row);
        }

        [Test]
        public void GetDenseRef_And_ChangeDenseRevision()
        {
            var structure = MakePositionStructure();
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDenseValue(row, new Position { X = 2 }, 3);

            ref var position = ref structure.GetDenseRef<Position>(row);
            position.X = 4;
            structure.ChangeDenseRevision<Position>(row);

            Assert.AreEqual(4, structure.RO<Position>()[0].X);
            Assert.AreEqual(3u, structure.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, structure.GetDenseRevision<Position>(0));
        }

        [Test]
        public void Growth_PreservesData()
        {
            var structure = MakePositionStructure();
            for (var i = 0; i < 100; i++)
            {
                var row = structure.Append((ulong)i, EntityLocation.Pool.Get());
                structure.SetDenseValue(row, new Position { X = i }, 1);
            }

            Assert.AreEqual(100, structure.Count);
            Assert.AreEqual(99, structure.RO<Position>()[99].X);
        }

        [Test]
        public void SlotOf_ThrowsForMissingDenseType()
        {
            var structure = MakePositionStructure();

            Assert.Throws<InvalidOperationException>(() => structure.RO<Velocity>());
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureTestUnit`
Expected: 编译失败，`EntityLocation` / `Structure` / `IStructureObserver` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/EntityLocation.cs`：

```csharp
using CoreECS.Utils;

namespace CoreECS.Structures
{
    /// <summary>
    /// Pooled anchor shared by Entity handles and ComponentRefs.
    /// Moving an entity only mutates this object, so existing references follow automatically.
    /// Note: do not cache instances in production; they are pooled and reused.
    /// </summary>
    public sealed class EntityLocation
    {
        /// <summary>
        /// Object pool for EntityLocation instances.
        /// </summary>
        public static readonly Pool<EntityLocation> Pool = new(
            createFunc: () => new EntityLocation(),
            returnAction: x => x.Reset());

        /// <summary>The structure currently owning the entity.</summary>
        public Structure Structure;

        /// <summary>The entity row inside <see cref="Structure"/>.</summary>
        public int Row;

        /// <summary>Generation used to detect stale handles after the instance is recycled.</summary>
        public uint Generation;

        private EntityLocation()
        {
            Row = -1;
        }

        private void Reset()
        {
            Structure = null;
            Row = -1;
            Generation = (Generation % uint.MaxValue) + 1;
        }
    }
}
```

创建 `ECS/Structures/Structure.cs`：

```csharp
using System;
using System.Collections.Generic;
using CoreECS.Defines;

namespace CoreECS.Structures
{
    /// <summary>
    /// Observer notified by structures when component state changes.
    /// </summary>
    public interface IStructureObserver
    {
        /// <summary>A component (dense, discrete or tag) was added.</summary>
        void OnComponentAdded(Structure structure, int row, uint typeId);

        /// <summary>A component (dense, discrete or tag) was removed.</summary>
        void OnComponentRemoved(Structure structure, int row, uint typeId);

        /// <summary>A component revision changed.</summary>
        void OnComponentChanged(Structure structure, int row, uint typeId);
    }

    /// <summary>
    /// One archetype: every entity sharing the same dense composition and mask.
    /// Dense component data is stored in row-aligned SoA arrays; tags and discrete
    /// components live in auxiliary containers attached to the structure.
    /// </summary>
    public sealed class Structure
    {
        private const int InitialCapacity = 8;

        private readonly StructureKey m_key;
        private readonly uint[] m_denseTypeIds;
        private readonly Type[] m_denseTypes;
        private readonly Array[] m_denseData;
        private readonly uint[][] m_denseVersions;
        private readonly uint[][] m_denseRevisions;
        private readonly TagContainer m_tags = new();

        private ulong[] m_entityIds = new ulong[InitialCapacity];
        private EntityLocation[] m_locations = new EntityLocation[InitialCapacity];
        private SpareSetComponentContainer m_spareSet;
        private int m_capacity = InitialCapacity;
        private int m_count;

        /// <summary>Optional observer for component add/remove/change notifications.</summary>
        public IStructureObserver Observer { get; set; }

        /// <summary>The archetype key of this structure.</summary>
        public StructureKey Key => m_key;

        /// <summary>The entity mask shared by all rows.</summary>
        public ulong Mask => m_key.Mask;

        /// <summary>Number of live rows (entities).</summary>
        public int Count => m_count;

        /// <summary>Sorted dense component type ids.</summary>
        public IReadOnlyList<uint> DenseTypeIds => m_denseTypeIds;

        /// <summary>Entity ids aligned with row indexes.</summary>
        public ReadOnlySpan<ulong> Entities => m_entityIds.AsSpan(0, m_count);

        internal SpareSetComponentContainer SpareSet => m_spareSet ??= new SpareSetComponentContainer();

        /// <summary>
        /// Creates a structure for the given key.
        /// </summary>
        public Structure(in StructureKey key)
        {
            m_key = key;
            m_denseTypeIds = key.ToArray();
            var denseCount = m_denseTypeIds.Length;
            m_denseTypes = new Type[denseCount];
            m_denseData = new Array[denseCount];
            m_denseVersions = new uint[denseCount][];
            m_denseRevisions = new uint[denseCount][];

            for (var i = 0; i < denseCount; i++)
            {
                var info = ComponentTypeRegistry.GetById(m_denseTypeIds[i]);
                m_denseTypes[i] = info.Type;
                m_denseData[i] = Array.CreateInstance(info.Type, InitialCapacity);
                m_denseVersions[i] = new uint[InitialCapacity];
                m_denseRevisions[i] = new uint[InitialCapacity];
            }
        }

        /// <summary>Checks whether the structure carries the dense component type.</summary>
        public bool HasDense(uint typeId) => IndexOfDense(typeId) >= 0;

        /// <summary>Returns the dense slot for a type id, or -1 when absent.</summary>
        public int IndexOfDense(uint typeId)
        {
            var low = 0;
            var high = m_denseTypeIds.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) >> 1;
                var value = m_denseTypeIds[mid];
                if (value == typeId) return mid;
                if (value < typeId) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        /// <summary>
        /// Appends a row for the entity and binds its location to this structure.
        /// </summary>
        public int Append(ulong entityId, EntityLocation location)
        {
            if (m_count == m_capacity) Grow();

            var row = m_count;
            m_entityIds[row] = entityId;
            m_locations[row] = location;
            location.Structure = this;
            location.Row = row;
            m_tags.AddRow();
            m_spareSet?.AddRow();
            m_count += 1;
            return row;
        }

        /// <summary>
        /// Removes a row by moving the last row into its slot.
        /// The removed entity's location is left untouched for the caller to reassign or release.
        /// </summary>
        public void SwapRemove(int row)
        {
            var last = m_count - 1;
            if (row != last)
            {
                for (var i = 0; i < m_denseData.Length; i++)
                {
                    Array.Copy(m_denseData[i], last, m_denseData[i], row, 1);
                    m_denseVersions[i][row] = m_denseVersions[i][last];
                    m_denseRevisions[i][row] = m_denseRevisions[i][last];
                }

                m_entityIds[row] = m_entityIds[last];
                m_locations[row] = m_locations[last];
                m_locations[row].Row = row;
            }

            m_tags.RemoveRowSwap(row);
            m_spareSet?.RemoveRowSwap(row);
            m_entityIds[last] = 0;
            m_locations[last] = null;
            m_count -= 1;
        }

        /// <summary>Gets a read-only span over a dense component column.</summary>
        public ReadOnlySpan<T> RO<T>() where T : struct, IComponent<T>
        {
            return ((T[])m_denseData[SlotOf<T>()]).AsSpan(0, m_count);
        }

        /// <summary>
        /// Gets a writable span over a dense component column.
        /// Acquiring the span marks every row as changed (revision bump + observer notification).
        /// </summary>
        public Span<T> RW<T>() where T : struct, IComponent<T>
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

        /// <summary>Gets a writable reference to a dense component without marking it changed.</summary>
        public ref T GetDenseRef<T>(int row) where T : struct, IComponent<T>
        {
            return ref ((T[])m_denseData[SlotOf<T>()])[row];
        }

        /// <summary>Gets the dense component instance version at the row.</summary>
        public uint GetDenseVersion<T>(int row) where T : struct, IComponent<T>
        {
            return m_denseVersions[SlotOf<T>()][row];
        }

        /// <summary>Gets the dense component revision at the row.</summary>
        public uint GetDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            return m_denseRevisions[SlotOf<T>()][row];
        }

        /// <summary>Bumps the dense component revision and notifies the observer.</summary>
        public uint ChangeDenseRevision<T>(int row) where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            var revision = (m_denseRevisions[slot][row] % uint.MaxValue) + 1;
            m_denseRevisions[slot][row] = revision;
            Observer?.OnComponentChanged(this, row, m_denseTypeIds[slot]);
            return revision;
        }

        /// <summary>
        /// Writes a dense component value (used when a component is added or migrated in).
        /// </summary>
        public void SetDenseValue<T>(int row, in T value, uint version) where T : struct, IComponent<T>
        {
            var slot = SlotOf<T>();
            ((T[])m_denseData[slot])[row] = value;
            m_denseVersions[slot][row] = version;
            m_denseRevisions[slot][row] = 0;
        }

        private int SlotOf<T>() where T : struct, IComponent<T>
        {
            var slot = IndexOfDense(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            if (slot < 0)
            {
                throw new InvalidOperationException(
                    $"Component {typeof(T).Name} is not part of structure with mask {Mask}.");
            }

            return slot;
        }

        private void Grow()
        {
            var newCapacity = Math.Max(InitialCapacity, m_capacity * 2);
            Array.Resize(ref m_entityIds, newCapacity);
            Array.Resize(ref m_locations, newCapacity);

            for (var i = 0; i < m_denseData.Length; i++)
            {
                var grown = Array.CreateInstance(m_denseTypes[i], newCapacity);
                Array.Copy(m_denseData[i], grown, m_denseData[i].Length);
                m_denseData[i] = grown;
                Array.Resize(ref m_denseVersions[i], newCapacity);
                Array.Resize(ref m_denseRevisions[i], newCapacity);
            }

            m_capacity = newCapacity;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter "FullyQualifiedName~StructureTestUnit|FullyQualifiedName~EntityLocationTestUnit"`
Expected: PASS（8 + 2 = 10 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/EntityLocation.cs ECS/Structures/Structure.cs Test/EntityLocationTestUnit.cs Test/StructureTestUnit.cs
git commit -m "feat(core): add pooled entity location and archetype structure with dense SoA storage"
```

---

## Task 8: Structure 附属容器操作与迁移辅助

**Files:**
- Modify: `ECS/Structures/Structure.cs`
- Test: `Test/StructureMigrationTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/StructureMigrationTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureMigrationTestUnit
    {
        private struct Position : IComponent<Position>
        {
            public int X;
        }

        private struct Velocity : IComponent<Velocity>
        {
            public int Y;
        }

        private struct Mana : IDiscreteComponent<Mana>
        {
            public int Value;
        }

        private struct Player : ITagComponent<Player>
        {
        }

        private sealed class RecordingObserver : IStructureObserver
        {
            public readonly List<(uint TypeId, int Row)> Added = new();
            public readonly List<(uint TypeId, int Row)> Removed = new();
            public readonly List<(uint TypeId, int Row)> Changed = new();

            public void OnComponentAdded(Structure structure, int row, uint typeId) => Added.Add((typeId, row));
            public void OnComponentRemoved(Structure structure, int row, uint typeId) => Removed.Add((typeId, row));
            public void OnComponentChanged(Structure structure, int row, uint typeId) => Changed.Add((typeId, row));
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        private static Structure MakeStructure(params uint[] denseTypeIds)
        {
            var sorted = (uint[])denseTypeIds.Clone();
            Array.Sort(sorted);
            return new Structure(new StructureKey(sorted, 0));
        }

        [Test]
        public void Tag_AddAndRemove_NotifyObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            Assert.IsTrue(structure.AddTag(IdOf<Player>(), row));
            Assert.IsTrue(structure.HasTag(IdOf<Player>(), row));
            Assert.IsFalse(structure.AddTag(IdOf<Player>(), row));
            Assert.IsTrue(structure.RemoveTag(IdOf<Player>(), row));
            Assert.IsFalse(structure.HasTag(IdOf<Player>(), row));

            Assert.AreEqual(1, observer.Added.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        [Test]
        public void Discrete_SetOverwriteRemove_NotifyObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());

            structure.SetDiscrete(row, new Mana { Value = 1 }, 5);
            structure.SetDiscrete(row, new Mana { Value = 2 }, 5);
            structure.RemoveDiscrete(IdOf<Mana>(), row);

            Assert.IsFalse(structure.HasDiscrete(IdOf<Mana>(), row));
            Assert.AreEqual(1, observer.Added.Count);
            Assert.AreEqual(1, observer.Changed.Count);
            Assert.AreEqual(1, observer.Removed.Count);
        }

        [Test]
        public void Discrete_ChangeRevision_NotifiesObserver()
        {
            var structure = MakeStructure(IdOf<Position>());
            var observer = new RecordingObserver();
            structure.Observer = observer;
            var row = structure.Append(1, EntityLocation.Pool.Get());
            structure.SetDiscrete(row, new Mana { Value = 1 }, 5);

            var revision = structure.ChangeDiscreteRevision<Mana>(row);

            Assert.AreEqual(1u, revision);
            Assert.AreEqual(1, observer.Changed.Count);
        }

        [Test]
        public void CopyDenseTo_CopiesSharedTypesWithVersionAndRevision()
        {
            var source = MakeStructure(IdOf<Position>(), IdOf<Velocity>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.SetDenseValue(row, new Position { X = 11 }, 7);
            source.SetDenseValue(row, new Velocity { Y = 22 }, 8);
            source.ChangeDenseRevision<Position>(row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.CopyDenseTo(target, row, targetRow);

            Assert.AreEqual(11, target.RO<Position>()[0].X);
            Assert.AreEqual(7u, target.GetDenseVersion<Position>(0));
            Assert.AreEqual(1u, target.GetDenseRevision<Position>(0));
            Assert.IsFalse(target.HasDense(IdOf<Velocity>()));
        }

        [Test]
        public void CopyTagsTo_TransfersRowTags()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.AddTag(IdOf<Player>(), row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.CopyTagsTo(target, row, targetRow);

            Assert.IsTrue(target.HasTag(IdOf<Player>(), 0));
        }

        [Test]
        public void MoveDiscreteTo_TransfersDataVersionAndRevision()
        {
            var source = MakeStructure(IdOf<Position>());
            var target = MakeStructure(IdOf<Position>());

            var row = source.Append(1, EntityLocation.Pool.Get());
            source.SetDiscrete(row, new Mana { Value = 4 }, 9);
            source.ChangeDiscreteRevision<Mana>(row);

            var targetRow = target.Append(1, EntityLocation.Pool.Get());
            source.MoveDiscreteTo(target, row, targetRow);

            Assert.IsTrue(target.HasDiscrete(IdOf<Mana>(), 0));
            Assert.AreEqual(4, target.GetDiscreteRef<Mana>(0).Value);
            Assert.AreEqual(9u, target.GetDiscreteVersion<Mana>(0));
            Assert.AreEqual(1u, target.GetDiscreteRevision<Mana>(0));
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureMigrationTestUnit`
Expected: 编译失败，`AddTag` / `SetDiscrete` / `CopyDenseTo` 等不存在

- [ ] **Step 3: 在 `ECS/Structures/Structure.cs` 的 `SetDenseValue` 方法之后、`private int SlotOf<T>()` 之前插入以下成员**

```csharp
        /// <summary>Checks whether the row carries the tag.</summary>
        public bool HasTag(uint tagId, int row) => m_tags.Has(row, tagId);

        /// <summary>Adds the tag to the row; notifies the observer when newly added.</summary>
        public bool AddTag(uint tagId, int row)
        {
            if (!m_tags.Add(row, tagId)) return false;

            Observer?.OnComponentAdded(this, row, tagId);
            return true;
        }

        /// <summary>Removes the tag from the row; notifies the observer when present.</summary>
        public bool RemoveTag(uint tagId, int row)
        {
            if (!m_tags.Remove(row, tagId)) return false;

            Observer?.OnComponentRemoved(this, row, tagId);
            return true;
        }

        /// <summary>Checks whether the row has the discrete component.</summary>
        public bool HasDiscrete(uint typeId, int row) => m_spareSet != null && m_spareSet.Has(typeId, row);

        /// <summary>
        /// Writes a discrete component at the row. Adding a new instance notifies
        /// <see cref="IStructureObserver.OnComponentAdded"/>; overwriting an existing one
        /// notifies <see cref="IStructureObserver.OnComponentChanged"/>.
        /// </summary>
        public void SetDiscrete<T>(int row, in T value, uint version)
            where T : struct, IDiscreteComponent<T>
        {
            var store = SpareSet.GetOrCreateStore<T>();
            var existed = store.Has(row);
            store.Set(row, value, version);

            if (existed) Observer?.OnComponentChanged(this, row, store.TypeId);
            else Observer?.OnComponentAdded(this, row, store.TypeId);
        }

        /// <summary>Removes the discrete component from the row when present.</summary>
        public void RemoveDiscrete(uint typeId, int row)
        {
            var store = m_spareSet?.GetStore(typeId);
            if (store == null || !store.Has(row)) return;

            store.Remove(row);
            Observer?.OnComponentRemoved(this, row, typeId);
        }

        /// <summary>Gets a writable reference to a discrete component; throws when absent.</summary>
        public ref T GetDiscreteRef<T>(int row) where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            var store = m_spareSet?.GetStore(typeId);
            if (store == null || !store.Has(row))
            {
                throw new InvalidOperationException(
                    $"Discrete component {typeof(T).Name} is not present at row {row}.");
            }

            return ref ((DiscreteStore<T>)store).Get(row);
        }

        /// <summary>Gets the discrete component instance version at the row.</summary>
        public uint GetDiscreteVersion<T>(int row) where T : struct, IDiscreteComponent<T>
        {
            var store = m_spareSet?.GetStore(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            return store == null ? 0u : store.GetVersion(row);
        }

        /// <summary>Gets the discrete component revision at the row.</summary>
        public uint GetDiscreteRevision<T>(int row) where T : struct, IDiscreteComponent<T>
        {
            var store = m_spareSet?.GetStore(ComponentTypeRegistry.GetOrRegister<T>().TypeId);
            return store == null ? 0u : store.GetRevision(row);
        }

        /// <summary>Bumps the discrete component revision and notifies the observer.</summary>
        public uint ChangeDiscreteRevision<T>(int row) where T : struct, IDiscreteComponent<T>
        {
            var typeId = ComponentTypeRegistry.GetOrRegister<T>().TypeId;
            var store = m_spareSet?.GetStore(typeId);
            if (store == null) return 0u;

            var revision = store.ChangeRevision(row);
            Observer?.OnComponentChanged(this, row, typeId);
            return revision;
        }

        /// <summary>
        /// Copies dense component data shared with the target structure for one row,
        /// preserving versions and revisions. Types absent from the target are skipped.
        /// </summary>
        internal void CopyDenseTo(Structure target, int sourceRow, int targetRow)
        {
            for (var i = 0; i < m_denseTypeIds.Length; i++)
            {
                var targetSlot = target.IndexOfDense(m_denseTypeIds[i]);
                if (targetSlot < 0) continue;

                Array.Copy(m_denseData[i], sourceRow, target.m_denseData[targetSlot], targetRow, 1);
                target.m_denseVersions[targetSlot][targetRow] = m_denseVersions[i][sourceRow];
                target.m_denseRevisions[targetSlot][targetRow] = m_denseRevisions[i][sourceRow];
            }
        }

        /// <summary>Copies one row of tag bits into the target structure.</summary>
        internal void CopyTagsTo(Structure target, int sourceRow, int targetRow)
        {
            m_tags.CopyRowTo(sourceRow, target.m_tags, targetRow);
        }

        /// <summary>Moves one row of discrete components into the target structure.</summary>
        internal void MoveDiscreteTo(Structure target, int sourceRow, int targetRow)
        {
            m_spareSet?.CopyRowTo(sourceRow, target.SpareSet, targetRow);
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureMigrationTestUnit`
Expected: PASS（6 个测试）

- [ ] **Step 5: 提交**

```bash
git add ECS/Structures/Structure.cs Test/StructureMigrationTestUnit.cs
git commit -m "feat(core): add structure auxiliary containers and migration helpers"
```

---

## Task 9: StructureRegistry（archetype 去重注册表）

**Files:**
- Create: `ECS/Structures/StructureRegistry.cs`
- Test: `Test/StructureRegistryTestUnit.cs`

- [ ] **Step 1: 写失败测试**

创建 `Test/StructureRegistryTestUnit.cs`：

```csharp
using CoreECS.Defines;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class StructureRegistryTestUnit
    {
        private struct Position : IComponent<Position>
        {
        }

        private struct Velocity : IComponent<Velocity>
        {
        }

        private static uint IdOf<T>() where T : struct, IComponent<T>
            => ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        [Test]
        public void GetOrCreate_DeduplicatesByCompositionAndMask()
        {
            var registry = new StructureRegistry();
            var positionId = IdOf<Position>();
            var velocityId = IdOf<Velocity>();

            var first = registry.GetOrCreate(new[] { positionId }, 7);
            var second = registry.GetOrCreate(new[] { positionId }, 7);
            var differentMask = registry.GetOrCreate(new[] { positionId }, 8);
            var differentComposition = registry.GetOrCreate(new[] { positionId, velocityId }, 7);

            Assert.AreSame(first, second);
            Assert.AreNotSame(first, differentMask);
            Assert.AreNotSame(first, differentComposition);
            Assert.AreEqual(3, registry.Count);
        }

        [Test]
        public void GetOrCreate_KeyMatchesEquivalentKeyInstance()
        {
            var registry = new StructureRegistry();
            var positionId = IdOf<Position>();

            var created = registry.GetOrCreate(new[] { positionId }, 0);
            var fetched = registry.GetOrCreate(new StructureKey(new[] { positionId }, 0));

            Assert.AreSame(created, fetched);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureRegistryTestUnit`
Expected: 编译失败，`StructureRegistry` 不存在

- [ ] **Step 3: 实现**

创建 `ECS/Structures/StructureRegistry.cs`：

```csharp
using System.Collections.Generic;

namespace CoreECS.Structures
{
    /// <summary>
    /// Deduplicates structures by their (dense composition, mask) key.
    /// </summary>
    public sealed class StructureRegistry
    {
        private readonly Dictionary<StructureKey, Structure> m_structures = new();

        /// <summary>Number of registered structures.</summary>
        public int Count => m_structures.Count;

        /// <summary>All registered structures.</summary>
        public IEnumerable<Structure> Structures => m_structures.Values;

        /// <summary>Gets or creates the structure for a composition and mask.</summary>
        public Structure GetOrCreate(uint[] sortedDenseTypeIds, ulong mask)
        {
            return GetOrCreate(new StructureKey(sortedDenseTypeIds, mask));
        }

        /// <summary>Gets or creates the structure for a key.</summary>
        public Structure GetOrCreate(in StructureKey key)
        {
            if (m_structures.TryGetValue(key, out var existing)) return existing;

            var created = new Structure(key);
            m_structures.Add(key, created);
            return created;
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test Test/Test.csproj --filter FullyQualifiedName~StructureRegistryTestUnit`
Expected: PASS（2 个测试）

- [ ] **Step 5: 全量验证**

Run: `dotnet build` 然后 `dotnet test --verbosity normal`
Expected: 两个目标框架构建成功；现有测试 + 本次新增测试全部 PASS

- [ ] **Step 6: 提交**

```bash
git add ECS/Structures/StructureRegistry.cs Test/StructureRegistryTestUnit.cs
git commit -m "feat(core): add structure registry for archetype deduplication"
```

---

## 本计划范围边界（不在 Plan 1a 内）

以下内容属于 **Plan 1b（World 集成）**，本计划不实现：

- `World` / `Entity` / `ComponentRef` 切换到新内核（`ComponentManager` / `EntityManager` 重写）
- 迁移编排：计算目标 `StructureKey`、`StructureRegistry` 调用、`EntityLocation` 分配与回收、向 collector 发事件
- `IEntityMatcher` 对 `Structure` + tag/discrete 的求值
- `EntityMatchManager` / collector 适配
- 删除 v1 存储（`ComponentStore` / `EntityGraph`）与内部测试迁移
- `ComponentTypeRegistry` 的 `IStructureObserver` 桥接实现

## Self-Review 记录

- **Spec 覆盖**：设计文档 §2（三接口、类型注册表）、§3.1/§3.2/§3.4（StructureKey、布局、version/revision）、§4.1（EntityLocation）均由本计划覆盖；§3.3 迁移编排与 §5/§6/§7/§8 留给 1b/后续阶段。
- **占位符扫描**：无 TBD/TODO；每个代码步骤均为完整实现。
- **类型一致性**：`ComponentTypeRegistry.GetOrRegister<T>()` / `GetById`、`StructureKey.AddType/RemoveType/ToArray`、`EntityLocation.Pool`、`TagContainer.AddRow/RemoveRowSwap/CopyRowTo`、`DiscreteStore.Set/Get/Remove/ChangeRevision`、`Structure.Append/SwapRemove/SetDenseValue/RO/RW/GetDenseRef/CopyDenseTo/CopyTagsTo/MoveDiscreteTo` 在定义与使用处签名一致。
- **已知边界**：`Structure.Append` 只更新 `EntityLocation`，不生成实体 id；id 由 1b 的 `EntityManager` 提供。
