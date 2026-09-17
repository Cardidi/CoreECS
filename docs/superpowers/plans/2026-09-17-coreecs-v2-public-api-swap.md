# CoreECS v2 公开 API 切换（Phase 1c）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把公开 `Entity` / `ComponentRef` / `EntityExtension` 切换到 v2 archetype 内核（`EntityLocation` + `Structure` + `ComponentOrchestrator`），保持 v1 公开签名与行为语义，为 Task 2 的管理器/World 接线与 Task 3 的测试迁移铺路。

**Architecture:** `Entity` 持有 `(world, entityId, EntityLocation, generation)`，按 `ComponentTypeRegistry` 解析出的 kind 经 `ComponentOrchestrator` 分发读写；`ComponentRef` / `ComponentRef<T>` 内部改为持有 `CoreECS.Structures.ComponentRefCore`，`RO` / `RW` 直接读写 `Structure` 存储并复用 v1 异常语义。统一写 API 要求从 `IComponent<T>` 宽约束上下文调用 discrete/tag 的泛型内核方法，而 C# 泛型约束不能在运行时收窄（CS0314），因此本任务先把内核相关泛型约束从 `IDiscreteComponent<T>` / `ITagComponent<T>` 放宽为 `IComponent<T>`（kind 仍由运行时注册表唯一决定），并新增非泛型 `RemoveComponent`。

**Tech Stack:** C# 9（`LangVersion 9`）、`net8.0` + `netstandard2.1`、NUnit 3.14、`dotnet test --filter`

**Spec:** `docs/superpowers/specs/2026-09-17-coreecs-v2-design.md`

**Handoff:** `docs/superpowers/plans/2026-09-17-coreecs-v2-handoff.md`

**前置计划:** `docs/superpowers/plans/2026-09-17-coreecs-v2-kernel-containers.md`（1a，已完成）、`docs/superpowers/plans/2026-09-17-coreecs-v2-world-integration-internals.md`（1b，必须先实现且全绿）

---

## File Structure

| 文件 | 职责 |
|---|---|
| `ECS/Defines/ComponentRef.cs` | （Task 1 重写）公开 `ComponentRef` / `ComponentRef<T>` 内部持有 `CoreECS.Structures.ComponentRefCore`；文件开头保留 v1 `IComponentRefLocator` / `IComponentRefCore` 兼容接口（Task 2 删除） |
| `ECS/Entity.cs` | （Task 1 重写）`Entity` 改为 `(world, entityId, EntityLocation, generation)`；写 API 按 kind 经 `ComponentOrchestrator` 分发；读 API 保持 v1 失效语义 |
| `ECS/EntityExtension.cs` | （Task 1 重写）`TryGetComponent` / `GetOrCreateComponent` 适配新 API，公开签名不变 |
| `ECS/Structures/ComponentOrchestrator.cs` | （Task 1 前向适配）放宽 discrete/tag 泛型约束；新增非泛型 `RemoveComponent`（复用 Task 4 的 `RemoveDenseComponentCore` / `RemoveDiscreteComponentCore` 守卫核心） |
| `ECS/Structures/ComponentHookDispatcher.cs` | （Task 1 前向适配）`RegisterDiscrete<T>` 约束放宽 |
| `ECS/Structures/Structure.cs` | （Task 1 前向适配）`SetDiscrete<T>` / `GetDiscreteRef<T>` 约束放宽 |
| `ECS/Structures/SpareSetComponentContainer.cs` | （Task 1 前向适配）`GetOrCreateStore<T>` 约束放宽 |
| `ECS/Structures/DiscreteStore.cs` | （Task 1 前向适配）`DiscreteStore<T>` 类约束放宽 |
| `ECS/Managers/ComponentManager.cs` | （Task 1 前向声明 `Orchestrator`；Task 2 重写）持有 `StructureRegistry` + observer 桥 + `ComponentOrchestrator`，删除 v1 存储 |
| `ECS/EntityGraph.cs`、`ComponentStore<T>`、v1 `ComponentRefCore`、`IComponentRefLocator`、`IComponentRefCore`、v1 `EntityMatcher.ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` | **Task 2 删除**（随 v1 存储一起） |
| `ECS/Structures/EntityTable.cs` | （Task 2 补充）`EntityIds` 枚举，供 `World.Query` / `EntityMatchManager` 遍历 |
| `ECS/Defines/IEntityMatcher.cs` / `ECS/EntityMatcher.cs` | （Task 2 切换）求值入口改为 `ComponentFilter(Structure, row)` 并提升到接口；v1 引用集合重载与 `m_changing` 删除 |
| `ECS/World.cs` | **Task 2 接线**（`CreateEntity(mask)` / `GetEntity` / `DestroyEntity` 经 EntityManager；`Query` 遍历 EntityTable）；`MinimalWorld.cs` / `EntityExtension.cs` 无需改动 |
| `ECS/Managers/EntityManager.cs` / `EntityMatchManager.cs` | **Task 2 重写**（实体注册表 / Structure 求值接线） |
| `Test/*` + `ECS/Structures/EntityTable.cs` / `ECS/Managers/EntityManager.cs`（shutdown 清理补丁） | **Task 3 迁移**（内部测试重写 + 行为测试适配 + 全量验证 + world shutdown 释放实体位置） |

Task 2、Task 3 均已追加在本文件末尾（Task 2：管理器与 World 接线 + v1 存储删除；Task 3：内部测试重写 + 行为测试机械适配 + Task 2 遗漏的 world shutdown 释放实体位置补丁 + 全量验证）。

## 本计划范围边界

- **Task 1（本文件）**：公开 `Entity` / `ComponentRef` / `EntityExtension` 切换到 v2 内核；含为使统一写 API 可编译的内核前向适配（泛型约束放宽 + 非泛型 `RemoveComponent`）与 `ComponentManager.Orchestrator` 访问器声明。本任务结束时 `ECS/ECS.csproj` 预期编译失败（仅 `EntityGraph.cs` 与 `World.cs`，Task 2 修复），Test 项目同样无法编译，因此本任务不跑测试。
- **Task 2（已追加，见下文）**：三个管理器 + `World` 接线（`MinimalWorld` / `EntityExtension` 无需改动）、`CreateEntity(mask)` 选择初始结构、`EntityMatchManager` 改用 Plan 1b Task 5 的 `ComponentFilter(Structure, row)`、`EntityTable.EntityIds` 遍历补充、删除 v1 存储（`EntityGraph`、`ComponentStore<T>`、v1 `ComponentRefCore`、`IComponentRefCore`、`IComponentRefLocator`、v1 `EntityMatcher.ComponentFilter` 等）与 v1 文件，恢复 `ECS/ECS.csproj` 编译；Test 项目保持预期红。
- **Task 3（已追加，见下文）**：内部测试重写（`ComponentManagerTestUnit` / `EntityManagerTestUnit`）、`EntityGraphTestUnit` 删除、五个行为文件机械适配（`ComponentTestUnit` / `EntityTestUnit` / `EntityMatcherTestUnit` / `WorldTestUnit` / `IntegrationTestUnit`）、Task 2 遗漏的 world shutdown 释放实体位置补丁（`EntityTable.Clear` + `EntityManager.OnManagerDestroyed`）、全量 `dotnet test` 与 v1 引用 grep 验证。**Task 3 完成后 Plan 1c 完成**（公开 API 已切 v2 内核、v1 存储已删除、测试全绿）。
- **Plan 1c 之后的阶段**（spec 后置，不在本计划）：`IEntityQuery` / `s.RO/RW<T>()` 批量访问（Phase 3）、系统分组与排序（Phase 4）、World 合并与生命周期收敛（Phase 5）、CommandBuffer（Phase 6）。

---

## Task 1: 公开 Entity 与 ComponentRef 切换到新内核

**Files:**
- Modify: `ECS/Defines/ComponentRef.cs`（重写公开结构体；文件开头的 v1 兼容接口原样保留）
- Modify: `ECS/Entity.cs`（整体重写）
- Modify: `ECS/EntityExtension.cs`（重写实现，公开签名不变）
- Modify（Plan 1b 内核前向适配）: `ECS/Structures/DiscreteStore.cs`、`ECS/Structures/SpareSetComponentContainer.cs`、`ECS/Structures/Structure.cs`、`ECS/Structures/ComponentHookDispatcher.cs`、`ECS/Structures/ComponentOrchestrator.cs`
- Modify（前向声明）: `ECS/Managers/ComponentManager.cs`
- Test: 无新增测试（验证方式见 Step 5 / Step 6）

**前置:** Plan 1b 已实现且全绿（`EntityTable` / `ComponentRefCore` / `ComponentHookDispatcher` / `ComponentOrchestrator` / `Structure` 非泛型访问器 / `EntityMatcher.ComponentFilter(Structure, int)` 均存在）。

**设计决策（执行时不要改动，评审时按此核对）：**

1. **内核泛型约束放宽（CS0314 规避）**：统一写 API 要求 `Entity.CreateComponent<T>() where T : struct, IComponent<T>` 在运行时按 kind 分发，而宽约束上下文不能调用 `AddDiscreteComponent<T>() where T : struct, IDiscreteComponent<T>`（CS0314，泛型约束无法在运行时收窄）。因此把 discrete/tag 调用链上的泛型方法约束放宽为 `IComponent<T>`；kind 仍由 `ComponentTypeRegistry.ResolveKind`（Tag > Discrete > Dense）在运行时唯一决定，公开调用点不可能把 dense 类型路由进 discrete/tag 分支。放宽只改签名，不改任何现有行为，Step 1 结束时全量测试应保持 Plan 1b 基线全绿。
2. **`ComponentRef.Core` 改为 internal**：`Core` 的类型是 internal 的 `CoreECS.Structures.ComponentRefCore`，public 字段会触发 CS0052。测试经 `InternalsVisibleTo("Test")` 仍可访问；公开成员 `NotNull` / `RuntimeType` / `EntityId` / `Revision` / `Inspect` / `Typed` / `Untyped` 全部保留。
3. **相等性改为结构相等**：v1 的 core 是每组件共享的池化对象，引用相等即可；v2 每次 `GetComponentRef` 新建 core，因此 `ComponentRef` / `ComponentRef<T>` 的相等与哈希按 `(Location 引用, Generation, TypeId, Kind, Version)` 计算，保证 `CreateComponent` 与 `GetComponent` 拿到的同一组件引用相等（对应 `EntityTestUnit` 的两个相等测试）。
4. **Tag 无 ref**：`CreateComponent<Tag>` / `GetComponent<Tag>` 返回 `default`（`NotNull == false`，不抛）；`HasComponent<Tag>` / `DestroyComponent<Tag>` 正常。`ComponentRef<T>.RO/RW` 对 tag core（公开 API 已不可获得，仅内核测试可构造）抛 `InvalidOperationException("Tag components carry no data.")`。
5. **失效实体语义保持 v1**：`IsValid` 返回 false；`World` 抛 `InvalidOperationException`；`Mask` / `GetComponent` / `GetComponents` / `HasComponent` / `CreateComponent` / `DestroyComponent` 在失效实体上抛 `InvalidOperationException`（`RequireLocation`），保持 `EntityTestUnit.Entity_InvalidAccessThrowsException` 等行为。
6. **change 事件路径**：`RW` → `ComponentRefCore.ChangeRevision()` → `Structure.ChangeDenseRevision/ChangeDiscreteRevision` → `Structure.Observer.OnComponentChanged`。本任务不接线 observer（Task 2 在管理器/World 侧注入），因此不新增事件测试。
7. **前向依赖**：`ComponentManager` 的 `internal ComponentOrchestrator Orchestrator { get; set; }` 在 Task 1 只声明（`Entity` 取用），Task 2 在管理器接线时注入实例。`Entity` 构造函数签名变化使 `EntityGraph.cs` / `World.cs` 在本任务结束时编译失败——这是预期的中间状态，Task 2 修复。
8. **保留 v1 兼容接口**：`ComponentRef.cs` 顶部的 `IComponentRefLocator` / `IComponentRefCore` 原样保留（`IComponentRefCore.RefLocator` 依赖前者，且 `EntityGraph` / `ComponentManager` / `EntityMatcher` 的 v1 路径仍引用它们；Task 2 随 v1 存储一起删除）。新公开结构体不再实现它们。注意 v1 `ComponentRefCore` 类在 `ECS/Managers/ComponentManager.cs`，不在本任务三个文件内，同样由 Task 2 删除。
9. **`GetComponents()` 不返回 Tag**（tag 无数据无 ref），顺序 = Dense（TypeId 升序）→ Discrete（store 枚举序），调用方不应依赖顺序。
10. **命名冲突**：v1 `CoreECS.Managers.ComponentRefCore` 与新 `CoreECS.Structures.ComponentRefCore` 同名，`Entity.cs` 同时 using 两个命名空间，必须使用 using 别名。

**测试说明：** 本任务不写新测试。公开 swap 的正确性由现有行为套件在 Task 2 恢复编译后验证；Task 1 结束时仓库处于预期的中间红状态（见 Step 5 / Step 6），不是回归。

---

- [ ] **Step 1: 内核前向适配**

1a. `ECS/Structures/DiscreteStore.cs`：

```diff
-    internal sealed class DiscreteStore<T> : DiscreteStore
-        where T : struct, IDiscreteComponent<T>
+    internal sealed class DiscreteStore<T> : DiscreteStore
+        where T : struct, IComponent<T>
```

1b. `ECS/Structures/SpareSetComponentContainer.cs`（`GetOrCreateStore`）：

```diff
-        public DiscreteStore<T> GetOrCreateStore<T>() where T : struct, IDiscreteComponent<T>
+        public DiscreteStore<T> GetOrCreateStore<T>() where T : struct, IComponent<T>
```

1c. `ECS/Structures/Structure.cs`（仅放宽 Task 1 代码实际调用的两个方法；其余 discrete 泛型访问器保持窄约束，`ComponentRefCore` 走非泛型重载）：

```diff
-        public void SetDiscrete<T>(int row, in T value, uint version)
-            where T : struct, IDiscreteComponent<T>
+        public void SetDiscrete<T>(int row, in T value, uint version)
+            where T : struct, IComponent<T>
```

```diff
-        public ref T GetDiscreteRef<T>(int row) where T : struct, IDiscreteComponent<T>
+        public ref T GetDiscreteRef<T>(int row) where T : struct, IComponent<T>
```

1d. `ECS/Structures/ComponentHookDispatcher.cs`（Task 4 复审新增的 `DiscreteHooks<T>` 静态持有类同样需要放宽，否则 `RegisterDiscrete<T>` 的宽约束无法引用它）：

```diff
-        private static class DiscreteHooks<T> where T : struct, IDiscreteComponent<T>
+        private static class DiscreteHooks<T> where T : struct, IComponent<T>
```

```diff
-        public static void RegisterDiscrete<T>() where T : struct, IDiscreteComponent<T>
+        public static void RegisterDiscrete<T>() where T : struct, IComponent<T>
```

1e. `ECS/Structures/ComponentOrchestrator.cs` 放宽 4 个方法约束：

```diff
-        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
-            where T : struct, IDiscreteComponent<T>
+        public ComponentRefCore AddDiscreteComponent<T>(ulong entityId, in T value)
+            where T : struct, IComponent<T>
```

```diff
-        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
+        public ComponentRefCore AddTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

```diff
-        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IDiscreteComponent<T>
+        public void RemoveDiscreteComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

```diff
-        public void RemoveTagComponent<T>(ulong entityId) where T : struct, ITagComponent<T>
+        public void RemoveTagComponent<T>(ulong entityId) where T : struct, IComponent<T>
```

1f. `ECS/Structures/ComponentOrchestrator.cs` 新增非泛型 `RemoveComponent`（放在 `RemoveDenseComponent<T>` 之后、`RequireLocation` 之前），复用 Task 4 的守卫核心（dense/discrete 的 hook + 守卫 + 行重读逻辑已在核心内）：

```csharp
/// <summary>
/// Removes a component by type id and kind. Dense and discrete removals run their
/// lifecycle hooks under the entity mutation guard and re-read the row afterwards
/// (see the core helpers); tag removal clears the bit and ignores absent tags.
/// </summary>
/// <exception cref="InvalidOperationException">
/// Thrown when a dense component is absent, matching <see cref="RemoveDenseComponent{T}"/>.
/// </exception>
public void RemoveComponent(ulong entityId, uint typeId, ComponentKind kind)
{
    switch (kind)
    {
        case ComponentKind.Dense:
            RemoveDenseComponentCore(entityId, typeId);
            return;

        case ComponentKind.Discrete:
            RemoveDiscreteComponentCore(entityId, typeId);
            return;

        case ComponentKind.Tag:
            var location = RequireLocation(entityId);
            location.Structure.RemoveTag(typeId, location.Row);
            return;
    }
}
```

`RemoveDenseComponent<T>` / `RemoveDiscreteComponent<T>` 保持不变（已在 Task 4 委托各自核心），本任务不再改动它们。

1g. `ECS/Managers/ComponentManager.cs`：文件顶部 `using CoreECS.Utils;` 之后加 `using CoreECS.Structures;`；类体内加前向访问器（放在 `OnManagerCreated()` 之前）：

```csharp
/// <summary>
/// v2 component kernel orchestrator. Declared here by Plan 1c Task 1 so the public
/// Entity swap compiles; Plan 1c Task 2 creates and injects the instance when the
/// manager is rewired to the kernel.
/// </summary>
internal ComponentOrchestrator Orchestrator { get; set; }
```

- [ ] **Step 2: 重写 `ECS/Defines/ComponentRef.cs`**

文件顶部 `using` 增加 `System.Runtime.CompilerServices` 与 `CoreECS.Structures`；`IComponentRefLocator`（原第 10-68 行）与 `IComponentRefCore`（原第 74-90 行）两个 v1 接口定义原样保留；从 `ComponentRef` 结构体开始整体替换为以下内容（含新的 `ComponentRefCoreComparer` 辅助类）：

```csharp
    /// <summary>
    /// Value equality for v2 ref cores. v1 compared shared pooled cores by reference;
    /// v2 creates a core per access, so identity is the component instance coordinates.
    /// </summary>
    internal static class ComponentRefCoreComparer
    {
        public static bool Equals(ComponentRefCore left, ComponentRefCore right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;

            return ReferenceEquals(left.Location, right.Location)
                   && left.Generation == right.Generation
                   && left.TypeId == right.TypeId
                   && left.Kind == right.Kind
                   && left.Version == right.Version;
        }

        public static int GetHashCode(ComponentRefCore core)
        {
            if (core == null) return 0;

            unchecked
            {
                var hash = core.Location == null ? 0 : RuntimeHelpers.GetHashCode(core.Location);
                hash = (hash * 397) ^ (int)core.Generation;
                hash = (hash * 397) ^ (int)core.TypeId;
                hash = (hash * 397) ^ (int)core.Kind;
                hash = (hash * 397) ^ (int)core.Version;
                return hash;
            }
        }
    }

    /// <summary>
    /// Typeless component reference over the v2 kernel core. <see cref="Core"/> is internal
    /// because the kernel core type is internal; public members keep v1 semantics.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Runtime type of the referenced component, or null when invalid.</summary>
        public Type RuntimeType => NotNull ? ComponentTypeRegistry.GetById(Core.TypeId).Type : null;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Checks whether the ref points at a component of type <typeparamref name="T"/>.</summary>
        public bool Inspect<T>() where T : struct, IComponent<T>
            => NotNull && Core.TypeId == ComponentTypeRegistry.GetOrRegister<T>().TypeId;

        /// <summary>Checks whether the ref points at a component of the given type.</summary>
        public bool Inspect(Type type)
            => NotNull
               && type != null
               && ComponentTypeRegistry.TryGet(type, out var info)
               && info.TypeId == Core.TypeId;

        /// <summary>Converts to a typed ref, validating presence and type unless skipped.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        /// <exception cref="InvalidCastException">Thrown when the component type differs.</exception>
        public ComponentRef<T> Typed<T>(bool noSafeCheck = false) where T : struct, IComponent<T>
        {
            if (!noSafeCheck)
            {
                if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
                if (Core.TypeId != ComponentTypeRegistry.GetOrRegister<T>().TypeId)
                    throw new InvalidCastException("Given type is unmatched with actual component type.");
            }

            return new ComponentRef<T>(Core);
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef left, ComponentRef right) => left.Equals(right);

        public static bool operator !=(ComponentRef left, ComponentRef right) => !left.Equals(right);
    }

    /// <summary>
    /// Typed component reference over the v2 kernel core. RO/RW read the owning structure
    /// directly; RW bumps the revision (emitting the change event through the structure
    /// observer) before handing out the writable ref.
    /// </summary>
    public readonly struct ComponentRef<T> : IEquatable<ComponentRef<T>> where T : struct, IComponent<T>
    {
        /// <summary>Kernel reference core; null for default/invalid refs.</summary>
        internal readonly ComponentRefCore Core;

        /// <summary>Creates a ref around a kernel core (ECS integration only).</summary>
        internal ComponentRef(ComponentRefCore core)
        {
            Core = core;
        }

        /// <summary>True when the referenced component instance still exists.</summary>
        public bool NotNull => Core != null && Core.NotNull;

        /// <summary>Entity owning the component, or 0 when invalid.</summary>
        public ulong EntityId => Core?.EntityId ?? 0UL;

        /// <summary>Current revision, or 0 when invalid/tag.</summary>
        public ulong Revision => Core?.Revision ?? 0UL;

        /// <summary>Readonly ref to the component data.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref readonly T RO
        {
            get
            {
                var structure = RequireStructure();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Writable ref to the component data; bumps the revision on access.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ref T RW
        {
            get
            {
                var structure = RequireStructure();
                Core.ChangeRevision();
                var row = Core.Location.Row;
                switch (Core.Kind)
                {
                    case ComponentKind.Dense:
                        return ref structure.GetDenseRef<T>(row);
                    case ComponentKind.Discrete:
                        return ref structure.GetDiscreteRef<T>(row);
                    default:
                        throw new InvalidOperationException("Tag components carry no data.");
                }
            }
        }

        /// <summary>Converts to the typeless ref.</summary>
        /// <exception cref="NullReferenceException">Thrown when the ref is invalid.</exception>
        public ComponentRef Untyped()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return new ComponentRef(Core);
        }

        private Structure RequireStructure()
        {
            if (Core == null || !Core.NotNull) throw new NullReferenceException("Component Reference is cut.");
            return Core.Location.Structure;
        }

        /// <inheritdoc />
        public bool Equals(ComponentRef<T> other) => ComponentRefCoreComparer.Equals(Core, other.Core);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null) return Core is null;
            return obj is ComponentRef<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() => ComponentRefCoreComparer.GetHashCode(Core);

        public static bool operator ==(ComponentRef<T> left, ComponentRef<T> right) => left.Equals(right);

        public static bool operator !=(ComponentRef<T> left, ComponentRef<T> right) => !left.Equals(right);

        public static implicit operator ComponentRef(ComponentRef<T> obj) => obj.Untyped();

        public static explicit operator ComponentRef<T>(ComponentRef obj) => obj.Typed<T>();
    }
```

- [ ] **Step 3: 重写 `ECS/Entity.cs`**

整体替换为：

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;
using CoreECS.Utils;
using ComponentRefCore = CoreECS.Structures.ComponentRefCore;

namespace CoreECS
{
    /// <summary>
    /// Handle to an entity inside a world. Holds the shared pooled
    /// <see cref="EntityLocation"/>, so references follow migrations and
    /// swap-removes automatically.
    /// </summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        private readonly IWorld m_world;
        private readonly ulong m_entityId;
        private readonly EntityLocation m_location;
        private readonly uint m_generation;
        private readonly ComponentManager m_componentManager;

        /// <summary>Internal constructor used by the world integration layer.</summary>
        internal Entity(IWorld world, ulong entityId, EntityLocation location, uint generation)
        {
            m_world = world;
            m_entityId = entityId;
            m_location = location;
            m_generation = generation;
            m_componentManager = world?.GetManager<ComponentManager>();
        }

        /// <summary>World this entity belongs to.</summary>
        /// <exception cref="InvalidOperationException">Thrown for a default entity.</exception>
        public IWorld World => m_world ?? throw new InvalidOperationException("Entity is not associated with any world.");

        /// <summary>Unique id inside its world; safe to copy without the location being alive.</summary>
        public ulong EntityId => m_entityId;

        /// <summary>True while the location is alive and its generation matches this handle.</summary>
        public bool IsValid => m_location != null
                               && m_location.Structure != null
                               && m_location.Generation == m_generation;

        /// <summary>Component mask of the structure currently owning the entity.</summary>
        /// <exception cref="InvalidOperationException">Thrown when the entity is no longer alive.</exception>
        public ulong Mask => RequireLocation().Structure.Mask;

        /// <summary>Creates a default-initialized component of type <typeparamref name="T"/>.</summary>
        public ComponentRef<T> CreateComponent<T>() where T : struct, IComponent<T> => CreateComponent(default(T));

        /// <summary>
        /// Creates a component of type <typeparamref name="T"/> with the given value.
        /// Dense components may migrate the entity to another structure; discrete
        /// components overwrite an existing instance; tags ignore the value and return
        /// <c>default</c> (tags carry no data).
        /// </summary>
        public ComponentRef<T> CreateComponent<T>(T component) where T : struct, IComponent<T>
        {
            RequireLocation();
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    return new ComponentRef<T>(orchestrator.AddDenseComponent(m_entityId, component));
                case ComponentKind.Discrete:
                    return new ComponentRef<T>(orchestrator.AddDiscreteComponent(m_entityId, component));
                case ComponentKind.Tag:
                    orchestrator.AddTagComponent<T>(m_entityId);
                    return default;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>Destroys a component referenced by a typed handle.</summary>
        public void DestroyComponent<T>(ComponentRef<T> comp) where T : struct, IComponent<T>
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys a component referenced by a typeless handle.</summary>
        public void DestroyComponent(ComponentRef comp)
        {
            Assertion.ArgumentNotNull(comp.NotNull ? this : null, "Component is null.");
            Assertion.AreEqual(comp.EntityId, m_entityId, "Component does not belong to this entity.");
            RequireLocation();
            Orchestrator.RemoveComponent(m_entityId, comp.Core.TypeId, comp.Core.Kind);
        }

        /// <summary>Destroys the component of type <typeparamref name="T"/>; throws when absent.</summary>
        public void DestroyComponent<T>() where T : struct, IComponent<T>
        {
            Assertion.IsTrue(HasComponent<T>(), "Entity does not have a component of type T.");
            var orchestrator = Orchestrator;
            var info = ComponentTypeRegistry.GetOrRegister<T>();
            switch (info.Kind)
            {
                case ComponentKind.Dense:
                    orchestrator.RemoveDenseComponent<T>(m_entityId);
                    return;
                case ComponentKind.Discrete:
                    orchestrator.RemoveDiscreteComponent<T>(m_entityId);
                    return;
                case ComponentKind.Tag:
                    orchestrator.RemoveTagComponent<T>(m_entityId);
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported component kind: {info.Kind}.");
            }
        }

        /// <summary>
        /// Gets a reference to the component of type <typeparamref name="TComp"/>.
        /// Returns <c>default</c> when the component is absent or when
        /// <typeparamref name="TComp"/> is a tag (tags carry no refs).
        /// </summary>
        public ComponentRef<TComp> GetComponent<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return default;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null ? default : new ComponentRef<TComp>(core);
        }

        /// <summary>
        /// Gets refs for all dense and discrete components on the entity. Tags are omitted
        /// (no data). Order is dense (type id ascending) then discrete (store enumeration
        /// order) and must not be relied on.
        /// </summary>
        public ComponentRef[] GetComponents()
        {
            var location = RequireLocation();
            var results = new List<ComponentRef>();
            CollectComponents(location, location.Structure, location.Row, results);
            return results.ToArray();
        }

        /// <summary>Adds all dense and discrete refs to <paramref name="results"/> and returns the count.</summary>
        public int GetComponents(ICollection<ComponentRef> results)
        {
            var location = RequireLocation();
            var before = results.Count;
            CollectComponents(location, location.Structure, location.Row, results);
            return results.Count - before;
        }

        /// <summary>Gets the refs of type <typeparamref name="TComp"/>; tags yield an empty array.</summary>
        public ComponentRef<TComp>[] GetComponents<TComp>() where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return Array.Empty<ComponentRef<TComp>>();

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            return core == null
                ? Array.Empty<ComponentRef<TComp>>()
                : new[] { new ComponentRef<TComp>(core) };
        }

        /// <summary>Adds the refs of type <typeparamref name="TComp"/> and returns the count.</summary>
        public int GetComponents<TComp>(ICollection<ComponentRef<TComp>> results)
            where TComp : struct, IComponent<TComp>
        {
            RequireLocation();
            var info = ComponentTypeRegistry.GetOrRegister<TComp>();
            if (info.Kind == ComponentKind.Tag) return 0;

            var core = Orchestrator.GetComponentRef<TComp>(m_entityId);
            if (core == null) return 0;

            results.Add(new ComponentRef<TComp>(core));
            return 1;
        }

        /// <summary>Checks whether the entity carries the component (all three kinds).</summary>
        public bool HasComponent<T>() where T : struct, IComponent<T>
        {
            RequireLocation();
            return Orchestrator.HasComponent<T>(m_entityId);
        }

        private static void CollectComponents(
            EntityLocation location, Structure structure, int row, ICollection<ComponentRef> results)
        {
            var denseTypeIds = structure.DenseTypeIds;
            for (var i = 0; i < denseTypeIds.Count; i++)
            {
                var typeId = denseTypeIds[i];
                results.Add(new ComponentRef(new ComponentRefCore(
                    location, location.Generation, typeId, ComponentKind.Dense,
                    structure.GetDenseVersion(typeId, row))));
            }

            var spareSet = structure.SpareSetOrNull;
            if (spareSet == null) return;

            foreach (var typeId in spareSet.TypeIds)
            {
                if (!structure.HasDiscrete(typeId, row)) continue;
                results.Add(new ComponentRef(new ComponentRefCore(
                    location, location.Generation, typeId, ComponentKind.Discrete,
                    structure.GetDiscreteVersion(typeId, row))));
            }
        }

        private EntityLocation RequireLocation()
        {
            if (!IsValid) throw new InvalidOperationException("Entity has already been destroyed.");
            return m_location;
        }

        private ComponentOrchestrator Orchestrator
        {
            get
            {
                var orchestrator = m_componentManager?.Orchestrator;
                if (orchestrator == null)
                {
                    throw new InvalidOperationException("Entity is not associated with a component kernel.");
                }

                return orchestrator;
            }
        }

        #region Equality

        /// <summary>Same world, entity id and generation.</summary>
        public bool Equals(Entity other)
        {
            return ReferenceEquals(m_world, other.m_world)
                   && m_entityId == other.m_entityId
                   && m_generation == other.m_generation;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Entity other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var worldHash = m_world == null ? 0 : RuntimeHelpers.GetHashCode(m_world);
            return HashCode.Combine(worldHash, m_entityId, m_generation);
        }

        public static bool operator ==(Entity left, Entity right) => left.Equals(right);

        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

        #endregion
    }
}
```

- [ ] **Step 4: 重写 `ECS/EntityExtension.cs`**

整体替换为（公开签名与 v1 完全一致，`GetOrCreateComponent` 复用 `TryGetComponent`）：

```csharp
using CoreECS.Defines;

namespace CoreECS
{
    /// <summary>
    /// Entity component access helpers built on the public Entity API.
    /// Tag components carry no refs: TryGetComponent returns true with a default ref
    /// for a present tag, and GetOrCreateComponent adds the tag then returns a default ref.
    /// </summary>
    public static class EntityExtension
    {
        /// <summary>Tries to get the component of type <typeparamref name="TComp"/>.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference; default when absent.</param>
        /// <returns>True when the component exists.</returns>
        public static bool TryGetComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.HasComponent<TComp>())
            {
                componentRef = entity.GetComponent<TComp>();
                return true;
            }

            componentRef = default;
            return false;
        }

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating a default one when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(this Entity entity, out ComponentRef<TComp> componentRef)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent<TComp>();
            return false;
        }

        /// <summary>Gets the component of type <typeparamref name="TComp"/>, creating one with <paramref name="initialValue"/> when absent.</summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <param name="componentRef">Component reference.</param>
        /// <param name="initialValue">Initial value used when the component is created.</param>
        /// <returns>True when the component already existed.</returns>
        public static bool GetOrCreateComponent<TComp>(
            this Entity entity, out ComponentRef<TComp> componentRef, in TComp initialValue)
            where TComp : struct, IComponent<TComp>
        {
            if (entity.TryGetComponent(out componentRef)) return true;

            componentRef = entity.CreateComponent(initialValue);
            return false;
        }
    }
}
```

- [ ] **Step 5: 编译验证（预期失败，错误面有界）**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`

Expected: **FAIL**，错误只出现在 Task 2 将重写/删除的 v1 集成文件，共 8 处（多目标构建可能每处报两次）：

| 文件 | 位置 | 错误 |
|---|---|---|
| `ECS/EntityGraph.cs` | 62、74、85、103、127 | `ComponentRef` / `ComponentRef<T>` 构造函数只接受 `CoreECS.Structures.ComponentRefCore`，传入 v1 `IComponentRefCore` → CS1503 |
| `ECS/World.cs` | 152、171、256 | `Entity` 构造函数签名变为 `(IWorld, ulong, EntityLocation, uint)`，传入 5 参旧签名 → CS1729 |

若出现上述列表之外的错误（尤其 `ECS/Entity.cs` / `ECS/Defines/ComponentRef.cs`），说明 Step 1 的内核前向适配有遗漏，必须修复后再提交。

- [ ] **Step 6: 全量测试说明**

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`

Expected: **无法运行**。Test 项目仍引用 v1 内部 API（`Core.RefLocator` / `Core.Offset` / `Core.Version`、`new Entity(world, id, generation)`、`EntityGraph`、v1 `ComponentRefCore`、`EntityManager.GetEntity` 等），编译失败属预期。Task 2 恢复 `ECS` 编译后，以下测试文件在 Task 3 迁移前预期为红：

- 需重写（v1 存储/管理器语义）：`ComponentManagerTestUnit`、`EntityGraphTestUnit`、`EntityManagerTestUnit`
- 需机械适配（断言 v1 ref 内部结构或 v1 语义）：`ComponentTestUnit`（`Core.RefLocator/Offset/Version`）、`EntityTestUnit`（`Core.RefLocator`、旧 `Entity` 构造、同一实体重复添加同类型 dense 组件）、`EntityMatcherTestUnit`（把 `GetComponents().Select(x => x.Core)` 传给 v1 `ComponentFilter`）、`WorldTestUnit`（`EntityManager.GetEntity`）
- 预期保持通过（Task 2 接线后，Task 3 编译修复前无法运行）：`EntityCollectorTestUnit`、`StressTestUnit`，以及所有 Plan 1a/1b 内核套件；`IntegrationTestUnit` 另有 `GetComponentStore` 引用（Task 2 Step 7 扫描发现），需 Task 3 机械适配

- [ ] **Step 7: 提交**

```bash
git add ECS/Entity.cs ECS/EntityExtension.cs ECS/Defines/ComponentRef.cs \
        ECS/Structures/ComponentOrchestrator.cs ECS/Structures/ComponentHookDispatcher.cs \
        ECS/Structures/Structure.cs ECS/Structures/SpareSetComponentContainer.cs \
        ECS/Structures/DiscreteStore.cs ECS/Managers/ComponentManager.cs
git commit -m "refactor(core): swap public entity and component ref to v2 kernel"
```

---

## Task 2: 管理器与 World 接线 + v1 存储删除

**Files:**
- Rewrite: `ECS/Managers/ComponentManager.cs`（内核持有者 + observer 桥；删除 v1 存储）
- Rewrite: `ECS/Managers/EntityManager.cs`（EntityTable + 实体级信号）
- Modify: `ECS/Managers/EntityMatchManager.cs`（`_changeCollector` 改用结构求值）
- Modify: `ECS/World.cs`（实体 API 与 `Query` 经新管理器；`OnTickEnd` 移除 `CleanupComponents`）
- Modify: `ECS/Structures/EntityTable.cs`（新增 `EntityIds`）
- Modify: `ECS/EntityMatcher.cs`、`ECS/Defines/IEntityMatcher.cs`（v2 求值入口提升到接口；删除 v1 重载与 `m_changing`）
- Modify: `ECS/Defines/ComponentRef.cs`（删除顶部 v1 接口块）
- Delete: `ECS/EntityGraph.cs`
- Test: 无新增/无迁移；预期状态见 Step 7（Task 3 迁移）

**前置:** Task 1 已提交（公开 `Entity` / `ComponentRef` 已切 v2、内核约束已放宽、`ComponentManager.Orchestrator` 已声明、`ComponentOrchestrator.RemoveComponent` 非泛型已存在）；Plan 1b 全绿。

**设计决策（执行时不要改动，评审时按此核对）：**

1. **内核归属与注入**：`ComponentManager` 持有 `StructureRegistry`、observer 桥（`IStructureObserver` 私有嵌套实现，把结构事件翻译成组件级信号）与 `Orchestrator` 属性；`EntityManager` 持有 `EntityTable`，并在构造函数里创建 `new ComponentOrchestrator(compManager.Structures, m_table, compManager.Observer)` 注入 `ComponentManager.Orchestrator`（DI 保证 EntityManager 构造时 ComponentManager 已构造）。`MinimalWorld` / `EntityExtension` 无需改动。
2. **信号 payload 变化（公开 API 破坏，Phase 1c 允许）**：`ComponentCreated` / `ComponentDestroyed` / `ComponentChanged` 从 `(IComponentRefCore, ulong, Type)` 改为 `(ulong entityId, Type compType)`；`EntityGetComponent` / `EntityLoseComponent` / `EntityChangeComponent` 从 `(EntityGraph, Type)` 改为 `(ulong entityId, Type compType)`——被删除的 `IComponentRefCore` / `EntityGraph` 无法继续作为 payload。`EntityLoseComponent` 的 `componentType == null` 约定为"实体销毁"。
3. **实体销毁信号**：编排层 `DestroyEntity` 只跑 hook + `SwapRemove`（Plan 1b 决策，无观察者事件），因此 `EntityManager.DestroyEntity` 在编排销毁后补发一条 `OnEntityLoseComp(entityId, null)`，否则 collector 会把已销毁实体永久留在 `Collected`。`EntityMatchManager` 以 `destroyed=true` 处理，等价 v1 的 `WishDestroy` 路径（`isMatched=false` → 下次 `Flush` 移出 `Collected` / 进入 `Clashing`）。v1 在实体销毁时逐组件发移除事件的语义不再保留（组件级订阅者改由 `OnDestroy` hook 覆盖）。
4. **matcher 求值入口**：`ComponentFilter(Structure, int)` 从 `EntityMatcher` 的 internal 方法提升为 `IEntityMatcher` 公开成员——`_changeCollector` / `World.Query` 在接口类型上求值，必须有接口成员才能编译（`Structure` 本就是 public，无可见性障碍）；v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 与仅供其使用的 `m_changing` 一并删除。`IsRelevantComponent` / `m_all` / `m_any` / `m_none` 保留。
5. **Query 遍历**：`EntityTable` 新增 `EntityIds`（`m_locations.Keys`）；`World.Query` 逐个 `TryGetLocation` 后用 `ComponentFilter(Structure, row)` 求值（v2 重载内含 mask 交集）。v1 `_isMatched` 的 `WishDestroy` 分支随 v1 存储删除：已销毁实体不在表内，天然被跳过。
6. **删除清单**（Step 6 有 grep 扫描）：`EntityGraph.cs` 整文件；`ComponentManager` 内 `ComponentRefCore` / `ComponentStore` / `ComponentStore<T>` / `GetAllComponentStores` / `GetComponentStore` / `CreateComponent<T>` / `DestroyComponent(IComponentRefCore)` / `CleanupComponents`；`ComponentRef.cs` 顶部 `IComponentRefLocator` / `IComponentRefCore`；`EntityMatcher` v1 重载 + `m_changing`；`IEntityMatcher` v1 成员；`World.OnTickEnd` 的 `CleanupComponents` 调用。
7. **测试状态**：本任务不迁移测试。ECS 双目标编译恢复是硬门槛；Test 项目保持预期红（三个内部文件 + 五个行为文件），Task 3 迁移后全量验证。

---

- [ ] **Step 1: `EntityTable` 补充 `EntityIds`**

在 `ECS/Structures/EntityTable.cs` 的 `TryGetLocation` 方法之后、`NextId` 之前插入：

```csharp
        /// <summary>
        /// Live entity ids. Enumeration order is unspecified; the table must not be
        /// mutated while enumerating.
        /// </summary>
        public IEnumerable<ulong> EntityIds => m_locations.Keys;
```

（`using System.Collections.Generic;` 已存在。）

- [ ] **Step 2: 重写 `ECS/Managers/ComponentManager.cs`**

整体替换为：

```csharp
using System;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for component creation events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owns the component</param>
    /// <param name="compType">The type of the component that was created</param>
    public delegate void ComponentCreated(ulong entityId, Type compType);

    /// <summary>
    /// Delegate for component destruction events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owned the component</param>
    /// <param name="compType">The type of the component that was destroyed</param>
    public delegate void ComponentDestroyed(ulong entityId, Type compType);

    /// <summary>
    /// Delegate for component revision change events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that owns the component</param>
    /// <param name="compType">The type of the component that changed</param>
    public delegate void ComponentChanged(ulong entityId, Type compType);

    /// <summary>
    /// Owns the v2 component kernel for one world: the structure registry, the
    /// orchestrator and the observer bridge that forwards structure events to the
    /// component-level signals.
    /// </summary>
    public sealed class ComponentManager : IWorldManager
    {
        private static readonly Emitter<ComponentCreated, ulong, Type> s_addEmitter =
            static (h, entityId, compType) => h(entityId, compType);

        private static readonly Emitter<ComponentDestroyed, ulong, Type> s_rmEmitter =
            static (h, entityId, compType) => h(entityId, compType);

        private static readonly Emitter<ComponentChanged, ulong, Type> s_changeEmitter =
            static (h, entityId, compType) => h(entityId, compType);

        /// <summary>
        /// Translates structure observer events into component-level signals.
        /// The entity id is read from the emitting structure row, so events carry the
        /// post-migration structure and row.
        /// </summary>
        private sealed class KernelObserver : IStructureObserver
        {
            private readonly ComponentManager m_manager;

            public KernelObserver(ComponentManager manager)
            {
                m_manager = manager;
            }

            public void OnComponentAdded(Structure structure, int row, uint typeId)
            {
                m_manager.OnComponentCreated.Emit(
                    structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type, s_addEmitter);
            }

            public void OnComponentRemoved(Structure structure, int row, uint typeId)
            {
                m_manager.OnComponentRemoved.Emit(
                    structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type, s_rmEmitter);
            }

            public void OnComponentChanged(Structure structure, int row, uint typeId)
            {
                m_manager.OnComponentChanged.Emit(
                    structure.Entities[row], ComponentTypeRegistry.GetById(typeId).Type, s_changeEmitter);
            }
        }

        /// <summary>Archetype registry owned by this manager.</summary>
        internal StructureRegistry Structures { get; } = new();

        /// <summary>Observer sink handed to the orchestrator.</summary>
        internal IStructureObserver Observer { get; }

        /// <summary>
        /// v2 component kernel orchestrator. Created and injected by <see cref="EntityManager"/>
        /// (which owns the entity table the orchestrator needs).
        /// </summary>
        internal ComponentOrchestrator Orchestrator { get; set; }

        /// <summary>
        /// Event triggered when a component is created. Payload is the owning entity id and
        /// the component type; the v1 component-ref-core payload was removed with v1 storage.
        /// </summary>
        public Signal<ComponentCreated> OnComponentCreated { get; } = new();

        /// <summary>
        /// Event triggered when a component is removed.
        /// </summary>
        public Signal<ComponentDestroyed> OnComponentRemoved { get; } = new();

        /// <summary>
        /// Event triggered when a component revision changes.
        /// </summary>
        public Signal<ComponentChanged> OnComponentChanged { get; } = new();

        /// <summary>
        /// Initializes a new instance of the ComponentManager class.
        /// </summary>
        public ComponentManager()
        {
            Observer = new KernelObserver(this);
        }

        /// <summary>Called when the manager is created.</summary>
        public void OnManagerCreated() {}

        /// <summary>Called when the world starts.</summary>
        public void OnWorldStarted() {}

        /// <summary>Called when the world ends.</summary>
        public void OnWorldEnded() {}

        /// <summary>Called when the manager is destroyed.</summary>
        public void OnManagerDestroyed() {}
    }
}
```

- [ ] **Step 3: 重写 `ECS/Managers/EntityManager.cs`**

整体替换为：

```csharp
using System;
using CoreECS.Structures;
using CoreECS.Utils;

namespace CoreECS.Managers
{
    /// <summary>
    /// Delegate for entity component acquisition events.
    /// </summary>
    /// <param name="entityId">The ID of the entity that acquired a component</param>
    /// <param name="componentType">The type of the component that was added</param>
    public delegate void EntityGetComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Delegate for entity component loss events. <paramref name="componentType"/> is null
    /// when the entity itself was destroyed (the kernel raises no per-component events then).
    /// </summary>
    /// <param name="entityId">The ID of the entity that lost a component</param>
    /// <param name="componentType">The type of the component that was removed</param>
    public delegate void EntityLoseComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Delegate for entity component revision change events.
    /// </summary>
    /// <param name="entityId">The ID of the entity whose component changed</param>
    /// <param name="componentType">The type of the component that changed</param>
    public delegate void EntityChangeComponent(ulong entityId, Type componentType);

    /// <summary>
    /// Manages entities in the world over the v2 kernel: owns the entity table, creates and
    /// destroys entities through the orchestrator, and re-emits component-level signals as
    /// entity-level signals. The v1 EntityGraph payload was removed with v1 storage; signals
    /// now carry the entity id instead of the pooled graph.
    /// </summary>
    public sealed class EntityManager : IWorldManager
    {
        private static readonly Emitter<EntityGetComponent, ulong, Type> s_gotEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        private static readonly Emitter<EntityLoseComponent, ulong, Type> s_loseEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        private static readonly Emitter<EntityChangeComponent, ulong, Type> s_changeEmitter =
            static (h, entityId, componentType) => h(entityId, componentType);

        /// <summary>Gets the world this manager belongs to.</summary>
        public IWorld World { get; }

        /// <summary>Event triggered when an entity gets a component.</summary>
        public Signal<EntityGetComponent> OnEntityGotComp { get; } = new();

        /// <summary>Event triggered when an entity loses a component or is destroyed.</summary>
        public Signal<EntityLoseComponent> OnEntityLoseComp { get; } = new();

        /// <summary>Event triggered when one of an entity's components changes revision.</summary>
        public Signal<EntityChangeComponent> OnEntityChangeComp { get; } = new();

        private readonly ComponentManager m_compManager;
        private readonly EntityTable m_table = new();
        private bool m_init;
        private bool m_shutdown;

        /// <summary>Kernel entity registry (internal test/debug access).</summary>
        internal EntityTable Table => m_table;

        private ComponentOrchestrator Orchestrator => m_compManager.Orchestrator;

        /// <summary>
        /// Creates a new entity with the specified mask.
        /// </summary>
        /// <param name="mask">The component mask for the new entity</param>
        /// <returns>The entity handle for the newly created entity</returns>
        public Entity CreateEntity(ulong mask = ulong.MaxValue)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            var (entityId, location) = Orchestrator.CreateEntity(mask);
            return new Entity(World, entityId, location, location.Generation);
        }

        /// <summary>
        /// Gets the entity handle for a live entity id.
        /// </summary>
        /// <param name="entityId">The ID of the entity to retrieve</param>
        /// <returns>The entity handle, or default when the id is not live</returns>
        public Entity GetEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            if (!m_table.TryGetLocation(entityId, out var location) || location.Structure == null) return default;

            return new Entity(World, entityId, location, location.Generation);
        }

        /// <summary>
        /// Destroys the entity with the specified ID. Unknown ids are ignored. Component
        /// lifecycle hooks run inside the orchestrator; a single entity-lost event with a
        /// null component type is emitted afterwards.
        /// </summary>
        /// <param name="entityId">The ID of the entity to destroy</param>
        public void DestroyEntity(ulong entityId)
        {
            Assertion.IsTrue(m_init);
            Assertion.IsFalse(m_shutdown);

            if (!m_table.TryGetLocation(entityId, out _)) return;

            Orchestrator.DestroyEntity(entityId);
            OnEntityLoseComp.Emit(entityId, null, s_loseEmitter);
        }

        /// <summary>Handles component addition events.</summary>
        private void _onComponentAdded(ulong entityId, Type compType)
        {
            OnEntityGotComp.Emit(entityId, compType, s_gotEmitter);
        }

        /// <summary>Handles component removal events.</summary>
        private void _onComponentRemoved(ulong entityId, Type compType)
        {
            OnEntityLoseComp.Emit(entityId, compType, s_loseEmitter);
        }

        /// <summary>Handles component revision change events.</summary>
        private void _onComponentChanged(ulong entityId, Type compType)
        {
            if (!OnEntityChangeComp.HasReceivers) return;

            OnEntityChangeComp.Emit(entityId, compType, s_changeEmitter);
        }

        /// <summary>Called when the manager is created.</summary>
        public void OnManagerCreated()
        {
            m_compManager.OnComponentCreated.Add(_onComponentAdded);
            m_compManager.OnComponentRemoved.Add(_onComponentRemoved);
            m_compManager.OnComponentChanged.Add(_onComponentChanged);

            m_init = true;
        }

        /// <summary>Called when the world starts.</summary>
        public void OnWorldStarted() {}

        /// <summary>Called when the world ends.</summary>
        public void OnWorldEnded() {}

        /// <summary>Called when the manager is destroyed.</summary>
        public void OnManagerDestroyed()
        {
            m_shutdown = true;

            m_compManager.OnComponentCreated.Remove(_onComponentAdded);
            m_compManager.OnComponentRemoved.Remove(_onComponentRemoved);
            m_compManager.OnComponentChanged.Remove(_onComponentChanged);
        }

        /// <summary>
        /// Initializes a new instance of the EntityManager class and injects the orchestrator
        /// (over the shared structure registry and this manager's table) into
        /// <paramref name="compManager"/>.
        /// </summary>
        /// <param name="world">The world this manager belongs to</param>
        /// <param name="compManager">The component manager owning the kernel</param>
        public EntityManager(IWorld world, ComponentManager compManager)
        {
            World = world;
            m_compManager = compManager;
            compManager.Orchestrator = new ComponentOrchestrator(compManager.Structures, m_table, compManager.Observer);
        }
    }
}
```

- [ ] **Step 4: `ECS/Managers/EntityMatchManager.cs` 求值接线**

4a. 文件顶部 `using CoreECS.Utils;` 之后加 `using CoreECS.Structures;`。

4b. 用以下代码整体替换 `_onComponentAdded` / `_onComponentRemoved` / `_onComponentChanged` / `_onEntityChanged` 四个方法（原第 384-430 行）：

```csharp
        /// <summary>
        /// Handles component addition events.
        /// </summary>
        /// <param name="entityId">The entity that gained the component</param>
        /// <param name="componentType">The type of the component that was added</param>
        private void _onComponentAdded(ulong entityId, Type componentType)
        {
            _onEntityChanged(entityId, componentType, true);
        }

        /// <summary>
        /// Handles component removal events. A null component type signals entity destruction:
        /// the entity is no longer in the table, so it is evaluated as unmatched and leaves
        /// the collected buffer on the next flush.
        /// </summary>
        /// <param name="entityId">The entity that lost the component</param>
        /// <param name="componentType">The type of the component that was removed</param>
        private void _onComponentRemoved(ulong entityId, Type componentType)
        {
            if (componentType == null)
            {
                foreach (var collector in m_collectors)
                {
                    _changeCollector(collector, entityId, false, false, null, true);
                }

                return;
            }

            _onEntityChanged(entityId, componentType, false);
        }

        /// <summary>
        /// Handles component revision change events.
        /// </summary>
        /// <param name="entityId">The entity that owns the component</param>
        /// <param name="componentType">The type of the component that changed</param>
        private void _onComponentChanged(ulong entityId, Type componentType)
        {
            if (m_revisionTrackingCollectorCount == 0) return;

            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityId, null, false, componentType);
            }
        }

        /// <summary>
        /// Handles entity changes by updating all collectors.
        /// </summary>
        /// <param name="entityId">The entity that changed</param>
        /// <param name="componentType">The type of the component that changed</param>
        /// <param name="isAdd">True if components were added, false if removed</param>
        private void _onEntityChanged(ulong entityId, Type componentType, bool isAdd)
        {
            foreach (var collector in m_collectors)
            {
                _changeCollector(collector, entityId, isAdd, false, componentType);
            }
        }
```

4c. 用以下代码整体替换 `_changeCollector`（原第 432-489 行）：

```csharp
        /// <summary>
        /// Updates a collector based on entity changes. The entity's structure and row are
        /// resolved from the entity table and evaluated with the v2 structure matcher;
        /// destroyed entities are evaluated as unmatched without a table lookup.
        /// </summary>
        /// <param name="collector">The collector to update</param>
        /// <param name="entityId">The entity that changed</param>
        /// <param name="isAdd">True if components were added, false if removed, null if only revision changed</param>
        /// <param name="init">True if this is during initialization</param>
        /// <param name="componentType">The type of the component that changed</param>
        /// <param name="destroyed">True when the entity was destroyed (no structure lookup)</param>
        private void _changeCollector(Collector collector, ulong entityId, bool? isAdd, bool init, Type componentType, bool destroyed = false)
        {
            var matcher = collector.Matcher;

            Structure structure = null;
            var row = 0;
            if (!destroyed)
            {
                if (!m_entityManager.Table.TryGetLocation(entityId, out var location) || location.Structure == null) return;

                structure = location.Structure;
                row = location.Row;
                // Quick-pass filter
                if ((matcher.EntityMask & structure.Mask) == 0) return;
            }

            // Pending match/clash buffers can make an entity "already collected" before it
            // reaches Collected, or keep it in Collected after it is scheduled to leave.
            var alreadyCollected = !init &&
                (collector.ContainsInBuffer(COLLECTED_BUFFER_INDEX, entityId) ||
                 collector.ContainsInBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId)) &&
                !collector.ContainsInBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);

            var isMatched = !destroyed && matcher.ComponentFilter(structure, row);

            if (!isAdd.HasValue)
            {
                if (collector.TrackRevisionChanged && alreadyCollected && isMatched
                    && RelevanceGate(collector, matcher, componentType))
                    collector.MarkChanged(entityId);
                return;
            }

            // Membership unchanged, but match-relevant composition changed while still collected.
            if (!(isMatched ^ alreadyCollected))
            {
                if (alreadyCollected && isMatched
                    && RelevanceGate(collector, matcher, componentType))
                    collector.MarkChanged(entityId);
                return;
            }

            if (isMatched)
            {
                collector.RemoveFromBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);

                if (collector.TrackMatchChanged)
                    collector.MarkChanged(entityId);
            }
            else
            {
                collector.RemoveFromBuffer(CHANGE_MATCHING_BUFFER_INDEX, entityId);
                collector.AddUniqueToBuffer(CHANGE_CLASHING_BUFFER_INDEX, entityId);

                if (collector.TrackClashChanged)
                    collector.MarkChanged(entityId);
            }
        }
```

4d. 把 `MakeCollector` 中的初始化遍历（原第 547-551 行）替换为：

```csharp
            foreach (var entityId in m_entityManager.Table.EntityIds)
            {
                _changeCollector(c, entityId, false, true, null);
            }
```

- [ ] **Step 5: `ECS/World.cs` 接线**

5a. `GetEntity`（原第 145-155 行）替换为：

```csharp
        public Entity GetEntity(ulong entityId)
        {
            if (Entity == null || Component == null)
                throw new InvalidOperationException("Core ECS managers are not available");

            return Entity.GetEntity(entityId);
        }
```

5b. `CreateEntity` 的 `var entityGraph = Entity.CreateEntity(mask);` 与 `return new Entity(...);` 两行替换为：

```csharp
            return Entity.CreateEntity(mask);
```

5c. `Query(IEntityMatcher, ICollection<ulong>)` 的遍历体（原第 221-230 行）替换为：

```csharp
            var added = 0;
            foreach (var entityId in Entity.Table.EntityIds)
            {
                if (!Entity.Table.TryGetLocation(entityId, out var location) || location.Structure == null) continue;
                if (!matcher.ComponentFilter(location.Structure, location.Row)) continue;

                result.Add(entityId);
                added += 1;
            }

            return added;
```

5d. `Query(IEntityMatcher, ICollection<Entity>)` 的遍历体（原第 251-260 行）替换为：

```csharp
            var added = 0;
            foreach (var entityId in Entity.Table.EntityIds)
            {
                if (!Entity.Table.TryGetLocation(entityId, out var location) || location.Structure == null) continue;
                if (!matcher.ComponentFilter(location.Structure, location.Row)) continue;

                result.Add(new Entity(this, entityId, location, location.Generation));
                added += 1;
            }

            return added;
```

5e. 删除 `_isMatched` 方法（原第 343-351 行），并删除 `OnTickEnd` 中的 `Component.CleanupComponents();` 调用（保留 `System.CleanupSystems();`）。

- [ ] **Step 6: 删除 v1 存储与残留引用**

先跑引用扫描（预期只命中待删/待改文件）：

```bash
grep -rn "EntityGraph\|IComponentRefCore\|IComponentRefLocator\|ComponentStore\|CleanupComponents" ECS/ --include="*.cs"
```

6a. `git rm ECS/EntityGraph.cs`。

6b. `ECS/Defines/ComponentRef.cs`：删除文件顶部 `IComponentRefLocator` 与 `IComponentRefCore` 两个接口声明（Task 1 保留的 v1 兼容块），其余不动。

6c. `ECS/Defines/IEntityMatcher.cs` 整体替换为：

```csharp
using System;
using CoreECS.Structures;

namespace CoreECS.Defines
{
    /// <summary>
    /// Defines a matcher to filter entities based on their components.
    /// </summary>
    public interface IEntityMatcher
    {
        /// <summary>
        /// Determines if a structure row satisfies all requirements of the matcher.
        /// Dense conditions are structure-level; tag and discrete conditions are row-level.
        /// </summary>
        /// <param name="structure">Structure owning the row</param>
        /// <param name="row">Live row inside the structure</param>
        /// <returns>True if the row matches the criteria, false otherwise</returns>
        public bool ComponentFilter(Structure structure, int row);

        /// <summary>
        /// Gets the allowed entities mask for this matcher.
        /// </summary>
        public ulong EntityMask { get; }

        /// <summary>
        /// Determines whether the specified component type is relevant
        /// to this matcher's criteria (all, any, or none sets).
        /// </summary>
        /// <param name="componentType">The component type to check</param>
        /// <returns>True if the component appears in any matcher set</returns>
        public bool IsRelevantComponent(Type componentType);
    }
}
```

6d. `ECS/EntityMatcher.cs`：删除 v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 方法与 `m_changing` 字段；把 Plan 1b Task 5 插入的 `internal bool ComponentFilter(Structure structure, int row)` 改为 `public`（隐式实现 `IEntityMatcher` 新成员）。`m_all` / `m_any` / `m_none` / `IsRelevantComponent` 与 `ResolvedSet` 求值代码保留。

6e. 再次运行 6 开头的扫描，Expected: 无输出。

- [ ] **Step 7: 编译验证与预期红状态**

```bash
PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj
```

Expected: **PASS**，`net8.0` + `netstandard2.1` 均 0 errors（本任务硬门槛）。

```bash
PATH="$HOME/.dotnet:$PATH" dotnet build Test/Test.csproj
```

Expected: **FAIL** —— Test 项目仍引用 v1 内部 API，Task 3 迁移前无法运行任何测试；错误只出现在下表文件：

| 文件 | 预期错误 | Task 3 处理 |
|---|---|---|
| `ComponentManagerTestUnit.cs` | `ComponentStore` / `ComponentStore<T>` / `GetComponentStore` / `GetAllComponentStores` / `CleanupComponents` / `CreateComponent(ulong)` / `DestroyComponent(IComponentRefCore)` / `IComponentRefCore` 已删除（CS0246/CS1061） | 重写 |
| `EntityGraphTestUnit.cs` | `EntityGraph` 已删除（CS0246） | 重写/替换 |
| `EntityManagerTestUnit.cs` | `EntityGraph` / `IComponentRefLocator` / `IComponentRefCore` 已删除；`EntityCaches` 不存在；`GetEntity` 返回 `Entity`；信号 payload 变化（CS0246/CS1061/CS1503） | 重写 |
| `ComponentTestUnit.cs` | `Core.RefLocator` / `Core.Offset` / `Core.RefLocator.ChangeRevision/GetRevision` 不存在；`as ComponentRefCore` 指向已删除的 v1 类（CS1061/CS0246） | 机械适配（v2 core：`Location`/`Generation`/`TypeId`/`Kind`/`Version`） |
| `EntityTestUnit.cs` | `graph.Generation`、`new Entity(world, id, generation)`、`Core.RefLocator`（CS1061/CS1729） | 机械适配 |
| `EntityMatcherTestUnit.cs` | v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 已删除（CS1503/CS1061） | 机械适配（v1 用例由 `EntityMatcherStructureTestUnit` 覆盖） |
| `WorldTestUnit.cs` | 可编译；`World_CanDestroyEntity` 运行时失败（`Assert.IsNull(EntityManager.GetEntity(...))` 对结构体恒不成立） | 一行适配：`Assert.IsFalse(...GetEntity(entityId).IsValid)` |
| `IntegrationTestUnit.cs` | `GetComponentStore` / `store.Allocated` / `CleanupComponents` 已删除（CS0246/CS1061）——Task 1 Step 6 未列出，本次扫描发现 | 机械适配（删除 store 断言，改断 hook 行为） |

三个内部测试文件（`ComponentManagerTestUnit` / `EntityGraphTestUnit` / `EntityManagerTestUnit`）保持红直到 Task 3；Task 1 Step 6 预告的 4 个机械适配文件（`ComponentTestUnit` / `EntityTestUnit` / `EntityMatcherTestUnit` / `WorldTestUnit`）同样保持红，Task 3 一并适配。编译修复后预期保持通过的公开行为文件：`EntityCollectorTestUnit`、`StressTestUnit`；Plan 1a/1b 内核套件（`EntityTableTestUnit`、`ComponentRefCoreTestUnit`、`ComponentOrchestratorTestUnit`、`StructureMigrationTestUnit`、`EntityMatcherStructureTestUnit`、`StructureTestUnit`、`EntityLocationTestUnit` 等）不受本任务影响。

- [ ] **Step 8: 提交**

```bash
git add ECS/Managers/ComponentManager.cs ECS/Managers/EntityManager.cs \
        ECS/Managers/EntityMatchManager.cs ECS/World.cs \
        ECS/Structures/EntityTable.cs ECS/EntityMatcher.cs \
        ECS/Defines/IEntityMatcher.cs ECS/Defines/ComponentRef.cs
git rm ECS/EntityGraph.cs
git commit -m "refactor(core): wire managers and world to v2 kernel"
```

---

## Task 3: 内部测试迁移与全量验证

**Files:**
- Rewrite: `Test/ComponentManagerTestUnit.cs`（19 个 v1 存储测试 → 10 个 v2 信号/生命周期测试）
- Rewrite: `Test/EntityManagerTestUnit.cs`（18 个 v1 图缓存测试 → 13 个 EntityTable/信号测试）
- Delete: `Test/EntityGraphTestUnit.cs`（17 个测试，`EntityGraph` 类型已在 Task 2 删除）
- Create: `Test/EntityExtensionTestUnit.cs`（9 个 EntityExtension / tag 读路径测试，见 Step 10）
- Modify: `Test/EntityCollectorTestUnit.cs`（新增 1 个"实体销毁移出 Collected"回归测试，见 Step 10b）
- Modify: `Test/IntegrationTestUnit.cs`（`ComponentLifecycle_OnCreateAndOnDestroyEvents` 去掉 v1 store 断言，改断 hook 行为）
- Modify: `Test/ComponentTestUnit.cs`（14 处 v1 core 坐标引用替换 + 删除 1 个 v1 池化重定位测试 + 新增 1 个 `Inspect(Type)` 重载测试）
- Modify: `Test/EntityTestUnit.cs`（v1 构造/core 引用替换 + 重复 dense 测试改写为 v2 单实例语义）
- Modify: `Test/EntityMatcherTestUnit.cs`（2 处 v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 调用改为 `World.Query`）
- Modify: `Test/WorldTestUnit.cs`（1 行：`GetEntity` 返回结构体，`IsNull` → `IsValid`）
- Modify（补 Task 2 遗漏）: `ECS/Structures/EntityTable.cs`（新增 `Clear()`）、`ECS/Managers/EntityManager.cs`（shutdown 时调用 `m_table.Clear()`）
- Test: 本任务即测试迁移；除上述 shutdown 补丁外不得改动 `ECS/` 其它代码

**前置:** Task 1、Task 2 已提交且 `ECS/ECS.csproj` 双目标 0 错误；Plan 1b 全绿（计划预期 450 passed）。Test 项目当前预期红（8 个文件，见 Task 2 Step 7 表）。

**设计决策（执行时不要改动，评审时按此核对）：**

1. **信号 payload 迁移**：`OnComponentCreated` / `OnComponentRemoved` / `OnComponentChanged` 的 payload 是 `(ulong entityId, Type compType)`；实体级信号 `OnEntityGotComp` / `OnEntityLoseComp` / `OnEntityChangeComp` 同为 `(ulong entityId, Type compType)`，实体销毁时 `OnEntityLoseComp` 第二参为 `null`（Task 2 决策 3）。旧测试捕获 `IComponentRefCore` / `EntityGraph` 的断言全部删除（payload 类型已不存在）。
2. **v2 core 坐标替换规则**：`Core.RefLocator.IsT(type)` → 公开 `ComponentRef.Inspect<T>()` / `Inspect(Type)`；`Core.RefLocator.GetT()` → typed 用 `Untyped().RuntimeType`、untyped 用 `RuntimeType`；`Core.RefLocator.GetEntityId(Core.Offset)` → `EntityId`；`Core.RefLocator.GetRevision(Core.Offset)` → `Core.Revision`（uint，与 `ComponentRef.Revision`（ulong）比较时需显式转换）；`Core.RefLocator.ChangeRevision(Core.Offset)` → `Core.ChangeRevision()`（uint）。`Core.Offset` / `Core.RefLocator` / `Core.Version` 在 v2 不存在（v2 core 坐标是 `Location` / `Generation` / `TypeId` / `Kind` / `Version`）。
3. **core 对象身份 vs 结构相等**：v2 每次 `GetComponent` / `GetComponents` 新建 core，所以"同一组件"的跨调用比较必须用 `ComponentRef` 的结构相等（`Equals` / `==`），不能用 `AreSame`。只有同一 core 的包装（`Untyped()` / `Typed()` / 隐式/显式转换）才保持同一实例，这些地方用 `Assert.AreSame(x.Core, y.Core)` 精确表达"同一组件、同一版本"。
4. **冗余测试删除**：`ComponentManagerTestUnit` 的 v1 store 容量/重排/扩展/计数测试全部删除——这些行为属于 v1 `ComponentStore<T>`，v2 由 `Structure` / `DiscreteStore` 承担且已有 Plan 1a 内核套件覆盖；`EntityGraphTestUnit` 的 17 个测试验证 v1 池化图内部，等价覆盖为 `EntityTableTestUnit`（id/位置/世代）+ 新 `EntityManagerTestUnit` + `EntityTestUnit`（组件访问）。`ComponentTestUnit.ComponentRef_CanRelocate` 删除：v2 core 不可重定位，迁移由共享 `EntityLocation` 自动完成，已由 `StructureMigrationTestUnit` / `ComponentOrchestratorTestUnit` 覆盖。
5. **重复 dense 语义**：v2 同一实体同类型 dense 只能有一个实例（`AddDenseComponent` 重复添加抛异常）。`Entity_GetComponents_GenericArray_ReturnsCorrectTypes` 原测试对同一实体加两个 `PositionComponent`，改写为单实例断言；重复添加抛异常已由 `ComponentOrchestratorTestUnit.AddDenseComponent_AlreadyPresentAndRemoveDenseComponent_Absent_Throw` 覆盖。
6. **Task 2 遗漏的 shutdown 清理**：v1 `EntityManager.OnManagerDestroyed` 会把所有 `EntityGraph` 归还池（实体句柄随 world shutdown 失效）。Task 2 的 v2 `OnManagerDestroyed` 只退订信号，导致 `Entity.IsValid` 在 shutdown 后仍为 true，`EntityTestUnit.Entity_IsValidAfterWorldShutdown_ReturnsFalse` 会运行失败（Task 2 Step 7 的错误表只列了编译错误，遗漏了这条运行时失败）。本任务补：`EntityTable.Clear()` 归还全部位置（不跑 hook、不发信号，等价 v1 的 `EntityGraph.Pool.Release` 循环），`EntityManager.OnManagerDestroyed` 调用它。
7. **`EntityLocation.Pool` 是进程级共享池**：`EntityManager_CreateEntity_AfterDestroy_ReusesReleasedLocationWithNewerGeneration` 先 `EntityLocation.Pool.Clear()` 再创建，确保释放的位置是唯一复用候选（与 `EntityTableTestUnit` 同法）。
8. **测试数量调和**：Plan 1b 后 450；本任务删除 `EntityGraphTestUnit` 17 + `ComponentManagerTestUnit` 旧 19 + `EntityManagerTestUnit` 旧 18 + `ComponentTestUnit.ComponentRef_CanRelocate` 1 = 55，新增 `ComponentManagerTestUnit` 10 + `EntityManagerTestUnit` 13 + `EntityExtensionTestUnit` 9 + `ComponentTestUnit` Inspect(Type) 1 + EntityCollectorTestUnit 1 = 34 → 预期 **429 passed**。执行时以 `dotnet test` 实际输出为准；若 Plan 1b 实际新增数与计划不同，按实际数调和，并把最终总数记录到本计划 Self-Review。
9. **验证命令**：统一 `PATH="$HOME/.dotnet:$PATH"`；ECS 双目标 0 错误 + Test 全绿 + v1 类型 grep 零命中是本任务硬门槛。

---

- [ ] **Step 1: 补 Task 2 遗漏——world shutdown 释放实体位置**

1a. `ECS/Structures/EntityTable.cs`：在 `Destroy` 方法之后、`TryGetLocation` 之前插入：

```csharp
        /// <summary>
        /// Releases every live entity location back to the pool without invoking component
        /// hooks or emitting signals. Used when the owning world shuts down; entity ids are
        /// not reused within this table.
        /// </summary>
        public void Clear()
        {
            foreach (var location in m_locations.Values)
            {
                EntityLocation.Pool.Release(location);
            }

            m_locations.Clear();
        }
```

1b. `ECS/Managers/EntityManager.cs`：`OnManagerDestroyed` 的 `m_shutdown = true;` 之后插入一行：

```diff
         public void OnManagerDestroyed()
         {
             m_shutdown = true;
+            m_table.Clear();
 
             m_compManager.OnComponentCreated.Remove(_onComponentAdded);
```

1c. `ECS/Managers/EntityManager.cs`：为 `DestroyEntity` 增加管理器级重入守卫（Task 2 质量评审修订——hook 内重入 `DestroyEntity` 会绕过 orchestrator 的 `m_destroying` 检查：orchestrator 的表项在 finally 中移除，内层 `EntityManager.DestroyEntity` 的 `TryGetLocation` 仍成功，从而在 orchestrator no-op 后仍发出一次 `OnEntityLoseComp`，加上外层共两次事件，违反"恰好一条"约定）。文件顶部加 `using System.Collections.Generic;`，新增字段并替换 `DestroyEntity`：

```diff
+        private readonly HashSet<ulong> m_destroying = new();
+
         public void DestroyEntity(ulong entityId)
         {
             Assertion.IsTrue(m_init);
             Assertion.IsFalse(m_shutdown);
 
             if (!m_table.TryGetLocation(entityId, out _)) return;
+            if (!m_destroying.Add(entityId)) return;
 
-            Orchestrator.DestroyEntity(entityId);
+            try
+            {
+                Orchestrator.DestroyEntity(entityId);
+            }
+            finally
+            {
+                m_destroying.Remove(entityId);
+            }
+
             OnEntityLoseComp.Emit(entityId, null, s_loseEmitter);
         }
```

1d. 重新编译确认补丁不破坏 ECS：

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: PASS，net8.0 + netstandard2.1 均 0 errors

- [ ] **Step 2: 重写 `Test/ComponentManagerTestUnit.cs`**

旧 → 新逐测试对照（19 → 10）：

| 旧测试 | 处理 | 理由 |
|---|---|---|
| `ComponentManager_GetComponentStore_CreatesNewStoreIfNotExists` | 删除 | `GetComponentStore` 已删除；store 行为由 Plan 1a 内核套件覆盖 |
| `ComponentManager_GetComponentStore_ReturnsSameInstanceForSameType` | 删除 | 同上 |
| `ComponentManager_GetComponentStore_GenericAndNonGeneric_ReturnSameStore` | 删除 | 同上 |
| `ComponentManager_CreateComponent_AddsComponentSuccessfully` | 删除 | 与 `ComponentTestUnit.Entity_CanCreateComponent` / `EntityTestUnit` 冗余；创建信号由新测试覆盖 |
| `ComponentManager_DestroyComponent_RemovesComponentSuccessfully` | 删除 | 与 `EntityTestUnit.Entity_CanDestroyComponent` 冗余 |
| `ComponentManager_DestroyComponent_ThrowsOnAlreadyDestroyedComponent` | 删除 | 与 `EntityTestUnit.Entity_DestroyComponentTwice_ThrowsException` 冗余 |
| `ComponentManager_GetAllComponentStores_ReturnsCorrectStores` | 删除 | `GetAllComponentStores` 已删除 |
| `ComponentManager_ComponentCreatedEvent_IsTriggered` | 重写 | 拆为 Dense / Discrete / Tag 三个 `(entityId, Type)` payload 测试 |
| `ComponentManager_ComponentRemovedEvent_IsTriggered` | 重写 | 拆为 Dense / Discrete / Tag 三个 payload 测试 |
| `ComponentManager_ComponentStore_CapacityExpansionWorks` | 删除 | v1 存储行为，已由 `StructureTestUnit` / `SpareSetComponentContainerTestUnit` 覆盖 |
| `ComponentManager_ComponentStore_RearrangesBeforeExpandingWhenInvalidSlotsExist` | 删除 | 同上 |
| `ComponentManager_ComponentStore_Rearrange_ReturnsCompactedSlotCount` | 删除 | 同上 |
| `ComponentManager_ComponentStore_ExpandMethod_IncreasesCapacity` | 删除 | 同上 |
| `ComponentManager_MultipleComponentTypes_ManagedSeparately` | 删除 | 与 `ComponentTestUnit.ComponentManager_CanHandleMultipleComponentTypes` 冗余 |
| `ComponentManager_ComponentStore_CorrectlyTracksComponents` | 删除 | v1 存储行为 |
| `ComponentManager_GetComponentStore_WithCreateIfNotExistFalse_ReturnsNullIfNotExists` | 删除 | `GetComponentStore` 已删除 |
| `ComponentManager_GetComponentStore_NonGenericWithCreateIfNotExistFalse_ReturnsNullIfNotExists` | 删除 | 同上 |
| `ComponentManager_ComponentLifecycle_CallbacksAreCalled` | 重写 | `ComponentManager_OnCreateAndOnDestroyHooks_RunThroughEntity` |
| `ComponentManager_ComponentRefCache_AfterDestroyingSameTypeComponents` | 删除 | v1 store 重排语义；迁移后引用有效由 `ComponentOrchestratorTestUnit.ComponentRefCore_CapturedBeforeAddDense_RemainsNotNullAfterMigration` 覆盖 |

整体替换为：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;

namespace CoreECS.Test
{
    [TestFixture]
    public class ComponentManagerTestUnit
    {
        private World _world;
        private ComponentManager _componentManager;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
            _componentManager = _world.GetManager<ComponentManager>();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void ComponentManager_OnComponentCreated_Dense_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent<PositionComponent>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentCreated_Discrete_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent(new ManaComponent { Value = 3 });

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentCreated_Tag_EmitsEntityIdAndType()
        {
            // Arrange
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentCreated.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            var entity = _world.CreateEntity();

            // Act
            entity.CreateComponent<PlayerTag>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PlayerTag), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Dense_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent(componentRef);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Discrete_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent(new ManaComponent { Value = 3 });

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent(componentRef);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_Tag_EmitsEntityIdAndType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            _componentManager.OnComponentRemoved.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent<PlayerTag>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PlayerTag), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentChanged_Dense_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();

            ulong capturedEntityId = 0;
            Type capturedType = null;
            var changeCount = 0;
            _componentManager.OnComponentChanged.Add((entityId, compType) =>
            {
                changeCount += 1;
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.X = 1.0f;

            // Assert
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentChanged_Discrete_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var componentRef = entity.CreateComponent(new ManaComponent { Value = 1 });

            ulong capturedEntityId = 0;
            Type capturedType = null;
            var changeCount = 0;
            _componentManager.OnComponentChanged.Add((entityId, compType) =>
            {
                changeCount += 1;
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.Value = 2;

            // Assert
            Assert.AreEqual(1, changeCount);
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(ManaComponent), capturedType);
        }

        [Test]
        public void ComponentManager_OnComponentRemoved_EntityDestroy_EmitsNoComponentSignal()
        {
            // Arrange
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent(new ManaComponent { Value = 1 });
            entity.CreateComponent<PlayerTag>();

            var removedCount = 0;
            _componentManager.OnComponentRemoved.Add((entityId, compType) => removedCount += 1);

            // Act - the kernel raises no per-component removal events on entity destroy
            _world.DestroyEntity(entity);

            // Assert
            Assert.AreEqual(0, removedCount);
        }

        [Test]
        public void ComponentManager_OnCreateAndOnDestroyHooks_RunThroughEntity()
        {
            // Arrange
            LifecycleComponent.CreateCount = 0;
            LifecycleComponent.DestroyCount = 0;
            var entity = _world.CreateEntity();

            // Act - create
            var componentRef = entity.CreateComponent<LifecycleComponent>();

            // Assert - creation hook ran on the stored instance
            Assert.IsTrue(componentRef.RW.OnCreateCalled);
            Assert.IsFalse(componentRef.RW.OnDestroyCalled);
            Assert.AreEqual(1, LifecycleComponent.CreateCount);

            // Act - destroy the entity
            _world.DestroyEntity(entity);

            // Assert - destruction hook ran and the ref is cut
            Assert.IsFalse(componentRef.NotNull);
            Assert.AreEqual(1, LifecycleComponent.DestroyCount);
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
            public float Y;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }

        private struct LifecycleComponent : IComponent<LifecycleComponent>
        {
            public static int CreateCount;
            public static int DestroyCount;

            public bool OnCreateCalled;
            public bool OnDestroyCalled;

            public void OnCreate(ulong entityId)
            {
                CreateCount += 1;
                OnCreateCalled = true;
            }

            public void OnDestroy(ulong entityId)
            {
                DestroyCount += 1;
                OnDestroyCalled = true;
            }
        }
    }
}
```

- [ ] **Step 3: 重写 `Test/EntityManagerTestUnit.cs`**

旧 → 新逐测试对照（18 → 13）：

| 旧测试 | 处理 | 理由 |
|---|---|---|
| `EntityManager_CreateEntity_AddsEntitySuccessfully` | 重写 | `CreateEntity_AllocatesIncreasingIdsAndRegistersLocations`（`EntityCaches` → `Table`） |
| `EntityManager_GetEntity_ReturnsCorrectEntity` | 重写 | `GetEntity_ReturnsLiveHandleAndDefaultForUnknownId` |
| `EntityManager_GetEntity_ReturnsNullForNonExistentEntity` | 合并 | v2 返回 `default(Entity)`，用 `IsValid == false` 表达（结构体不可能为 null） |
| `EntityManager_DestroyEntity_RemovesEntitySuccessfully` | 重写 | `DestroyEntity_RemovesEntityAndReleasesLocationWithNewGeneration` |
| `EntityManager_DestroyEntity_ReleasesComponentsAndMarksWishDestroyDuringNotification` | 重写 | `OnEntityLoseComp_EmitsNullTypeOnEntityDestroy`（v2 实体销毁只发一条 null 类型事件，不再逐组件发） |
| `EntityManager_CreateMultipleEntities_GeneratesUniqueIDs` | 合并 | 进 `CreateEntity_AllocatesIncreasingIdsAndRegistersLocations` |
| `EntityManager_EntityCachesProperty_IsReadOnly` | 删除 | `EntityCaches` 已删除；表内省由 `EntityTableTestUnit` 覆盖 |
| `EntityManager_ComponentAddedEvent_TriggeredWhenComponentAdded` | 重写 | `OnEntityGotComp_EmitsEntityIdAndComponentType` |
| `EntityManager_ComponentRemovedEvent_TriggeredWhenComponentRemoved` | 重写 | `OnEntityLoseComp_EmitsOnComponentDestroy` |
| `EntityManager_EntitiesMaintainState_AfterComponentOperations` | 删除 | v1 图状态；实体/组件行为由 `EntityTestUnit` / `ComponentTestUnit` 覆盖 |
| `EntityManager_CreateEntity_MaximumIdReached_ThrowsException` | 删除 | 空测试，从未验证行为 |
| `EntityManager_DestroyNonExistentEntity_DoesNotThrow` | 重写 | `DestroyEntity_UnknownId_IsNoOp` |
| `EntityManager_EntityIdSequence_IsContinuous` | 合并 | 进 `CreateEntity_AllocatesIncreasingIdsAndRegistersLocations` |
| `EntityManager_Shutdown_CleansUpProperly` | 重写 | `Shutdown_ReleasesAllLocationsAndRejectsNewEntities`（依赖 Step 1 补丁） |
| `EntityManager_Events_AreNotNull` | 保留 | 补 `OnEntityChangeComp` |
| `EntityManager_WorldProperty_ReturnsCorrectWorld` | 保留 | 无改动 |
| `EntityManager_CreateEntity_WithInitialMask_HasCorrectMask` | 重写 | `CreateEntity_WithInitialMask_SelectsMaskStructure`（mask 选择初始结构） |
| `EntityManager_DestroyEntity_MultipleTimes_DoesNotCauseIssues` | 合并 | 进 `DestroyEntity_UnknownId_IsNoOp` |

整体替换为：

```csharp
using CoreECS.Defines;
using CoreECS.Managers;
using CoreECS.Structures;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityManagerTestUnit
    {
        private World _world;
        private EntityManager _entityManager;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
            _entityManager = _world.GetManager<EntityManager>();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void EntityManager_CreateEntity_AllocatesIncreasingIdsAndRegistersLocations()
        {
            // Act
            var first = _entityManager.CreateEntity();
            var second = _entityManager.CreateEntity();

            // Assert
            Assert.AreEqual(1UL, first.EntityId);
            Assert.AreEqual(2UL, second.EntityId);
            Assert.IsTrue(first.IsValid);
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(2, _entityManager.Table.Count);

            Assert.IsTrue(_entityManager.Table.TryGetLocation(first.EntityId, out var firstLocation));
            Assert.IsTrue(_entityManager.Table.TryGetLocation(second.EntityId, out var secondLocation));
            Assert.IsNotNull(firstLocation);
            Assert.IsNotNull(secondLocation);
            Assert.AreNotSame(firstLocation, secondLocation);
        }

        [Test]
        public void EntityManager_CreateEntity_WithInitialMask_SelectsMaskStructure()
        {
            // Act
            var entity = _entityManager.CreateEntity(0b1010);

            // Assert
            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(0b1010UL, entity.Mask);
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));
            Assert.IsNotNull(location.Structure);
            Assert.AreEqual(0b1010UL, location.Structure.Mask);
        }

        [Test]
        public void EntityManager_GetEntity_ReturnsLiveHandleAndDefaultForUnknownId()
        {
            // Arrange
            var created = _entityManager.CreateEntity();

            // Act
            var retrieved = _entityManager.GetEntity(created.EntityId);
            var unknown = _entityManager.GetEntity(999999);

            // Assert
            Assert.IsTrue(retrieved.IsValid);
            Assert.AreEqual(created.EntityId, retrieved.EntityId);
            Assert.AreSame(_world, retrieved.World);

            Assert.IsFalse(unknown.IsValid);
            Assert.AreEqual(0UL, unknown.EntityId);
        }

        [Test]
        public void EntityManager_DestroyEntity_RemovesEntityAndReleasesLocationWithNewGeneration()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));
            var generation = location.Generation;

            // Act
            _entityManager.DestroyEntity(entity.EntityId);

            // Assert
            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsFalse(_entityManager.Table.TryGetLocation(entity.EntityId, out _));
            Assert.IsFalse(entity.IsValid);
            Assert.IsNull(location.Structure);
            Assert.AreEqual(generation + 1U, location.Generation);
        }

        [Test]
        public void EntityManager_CreateEntity_AfterDestroy_ReusesReleasedLocationWithNewerGeneration()
        {
            // Arrange - drain the shared pool so the released location is the only reuse candidate
            EntityLocation.Pool.Clear();
            var first = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(first.EntityId, out var staleLocation));
            var staleGeneration = staleLocation.Generation;

            // Act
            _entityManager.DestroyEntity(first.EntityId);
            var second = _entityManager.CreateEntity();

            // Assert
            Assert.AreNotEqual(first.EntityId, second.EntityId);
            Assert.IsTrue(_entityManager.Table.TryGetLocation(second.EntityId, out var current));
            Assert.AreSame(staleLocation, current);
            Assert.Greater(current.Generation, staleGeneration);
        }

        [Test]
        public void EntityManager_DestroyEntity_UnknownId_IsNoOp()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();

            // Act & Assert - unknown ids and repeated destroys are ignored
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(999999));
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(entity.EntityId));
            Assert.DoesNotThrow(() => _entityManager.DestroyEntity(entity.EntityId));

            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsFalse(_entityManager.Table.TryGetLocation(entity.EntityId, out _));
        }

        [Test]
        public void EntityManager_OnEntityGotComp_EmitsEntityIdAndComponentType()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityGotComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.CreateComponent<PositionComponent>();

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void EntityManager_OnEntityLoseComp_EmitsOnComponentDestroy()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityLoseComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            entity.DestroyComponent(componentRef);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void EntityManager_OnEntityLoseComp_EmitsNullTypeOnEntityDestroy()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = typeof(object);
            _entityManager.OnEntityLoseComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            _entityManager.DestroyEntity(entity.EntityId);

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.IsNull(capturedType);
        }

        [Test]
        public void EntityManager_OnEntityChangeComp_EmitsOnWritableAccess()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            var componentRef = entity.CreateComponent<PositionComponent>();
            ulong capturedEntityId = 0;
            Type capturedType = null;
            _entityManager.OnEntityChangeComp.Add((entityId, compType) =>
            {
                capturedEntityId = entityId;
                capturedType = compType;
            });

            // Act
            componentRef.RW.X = 1.0f;

            // Assert
            Assert.AreEqual(entity.EntityId, capturedEntityId);
            Assert.AreEqual(typeof(PositionComponent), capturedType);
        }

        [Test]
        public void EntityManager_Events_AreNotNull()
        {
            Assert.IsNotNull(_entityManager.OnEntityGotComp);
            Assert.IsNotNull(_entityManager.OnEntityLoseComp);
            Assert.IsNotNull(_entityManager.OnEntityChangeComp);
        }

        [Test]
        public void EntityManager_WorldProperty_ReturnsCorrectWorld()
        {
            Assert.IsNotNull(_entityManager.World);
            Assert.AreSame(_world, _entityManager.World);
        }

        [Test]
        public void EntityManager_Shutdown_ReleasesAllLocationsAndRejectsNewEntities()
        {
            // Arrange
            var entity = _entityManager.CreateEntity();
            Assert.IsTrue(_entityManager.Table.TryGetLocation(entity.EntityId, out var location));

            // Act
            _world.Shutdown();

            // Assert - shutdown releases every location (v1 parity) and the manager rejects new work
            Assert.AreEqual(0, _entityManager.Table.Count);
            Assert.IsNull(location.Structure);
            Assert.IsFalse(entity.IsValid);
            Assert.Throws<InvalidOperationException>(() => _entityManager.CreateEntity());

            _world = null;
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
            public float Y;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }
    }
}
```

- [ ] **Step 4: 删除 `Test/EntityGraphTestUnit.cs`**

```bash
git rm Test/EntityGraphTestUnit.cs
```

删除说明：`EntityGraph` 类型已在 Task 2 随 v1 存储删除，本文件无法编译且无迁移价值。其 17 个测试的等价覆盖：

| 旧覆盖 | v2 等价 |
|---|---|
| 池化分配/释放、`Reset` | `EntityTableTestUnit.Create_AfterDestroy_ReusesReleasedLocationWithNewerGeneration` |
| `EntityId` / `Mask` 属性 | `EntityManagerTestUnit.CreateEntity_*` / `EntityTestUnit.Entity_CanAccessMask` |
| `WishDestroy` | 由 `EntityManager.DestroyEntity` + `Entity.IsValid` 取代（无对应公开属性） |
| `RwComponents` 集合操作 | `Entity.GetComponents()` / `GetComponents<T>()`（`EntityTestUnit` / `ComponentTestUnit`） |
| `GetComponent` / `GetComponents` / `HasComponent` / `GetComponentCount` | `EntityTestUnit` 同名行为测试（v2 单实例语义） |

- [ ] **Step 5: 机械适配 `Test/IntegrationTestUnit.cs`**

替换 `ComponentLifecycle_OnCreateAndOnDestroyEvents` 中的准备/断言块（v1 store 断言删除，hook 断言保留）：

```diff
             var entity = world.CreateEntity();
             var entityId = entity.EntityId;
-            var componentManager = world.GetManager<ComponentManager>();
             
             // Act
             var componentRef = entity.CreateComponent<LifecycleComponent>();
-            var store = componentManager.GetComponentStore<LifecycleComponent>(false);
             
             // Assert
             Assert.IsTrue(componentRef.RW.OnCreateCalled);
             Assert.IsFalse(componentRef.RW.OnDestroyCalled);
             Assert.AreEqual(1, LifecycleComponent.OnCreateCount);
             Assert.AreEqual(entityId, LifecycleComponent.LastCreatedEntityId);
-            Assert.AreEqual(1, store.Allocated);
             
             // Act
             world.DestroyEntity(entity);
-            componentManager.CleanupComponents();
             
             // Assert
             Assert.IsFalse(componentRef.NotNull);
             Assert.AreEqual(1, LifecycleComponent.OnDestroyCount);
             Assert.AreEqual(entityId, LifecycleComponent.LastDestroyedEntityId);
-            Assert.AreEqual(0, store.Allocated);
```

`using CoreECS.Managers;` 保留（`SystemManager` 仍在用）；其余 4 个测试不改。

- [ ] **Step 6: 机械适配 `Test/ComponentTestUnit.cs`**

按下表逐处替换（行号为当前文件行号，仅作定位参考）：

| # | 测试 | 替换 |
|---|---|---|
| 6.1 | `ComponentRef_CanCheckType`（126-127） | `Core.RefLocator.IsT` → `Untyped().Inspect<T>()` |
| 6.2 | `ComponentRef_CanGetEntityType`（138） | `Core.RefLocator.GetT()` → `Untyped().RuntimeType` |
| 6.3 | `ComponentRef_CanGetEntityId`（152） | `Core.RefLocator.GetEntityId(Core.Offset)` → `EntityId` |
| 6.4 | `ComponentRef_CanGetRefCore`（170） | offset 比较 → 同一 core `AreSame` |
| 6.5 | `ComponentRef_CanRelocate`（173-188） | 删除（v2 core 不可重定位，迁移自动） |
| 6.6 | `ComponentRef_ImplicitConversion_FromTypedToUntyped`（446-448） | 三个坐标比较 → `AreSame(typedRef.Core, untypedRef.Core)` |
| 6.7 | `ComponentRef_ExplicitConversion_FromUntypedToTyped`（464-466） | 同上 |
| 6.8 | `ComponentRef_Typed_Method_SameType_Success`（499-501） | 同上 |
| 6.9 | `ComponentRef_Untyped_Method_Success`（530-532） | 同上 |
| 6.10 | `ComponentRef_Typed_WithNoSafeCheck_False_ValidType_Success`（567-569） | 同上 |
| 6.11 | `ComponentRef_Untyped_AfterImplicitConversion`（586-587） | 两个坐标比较 → `AreSame` |
| 6.12 | `ComponentRef_GetComponents_ReturnsUntypedRefs`（609-619） | `IsT` → `Inspect`；offset → 结构相等 `Equals` |
| 6.13 | `ComponentRef_UntypedThenTyped_ReturnsOriginal`（640-642） | 三个坐标比较 → `AreSame` |
| 6.14 | `ComponentRef_Revision_ChangeRevisionMethodIncrementsRevision`（790、794） | `Core.RefLocator.ChangeRevision(Core.Offset)` → `Core.ChangeRevision()`；`AreEqual` 加 `(ulong)` |
| 6.15 | `ComponentRef_Revision_GetRevisionReturnsCurrentRevision`（803、811、807、815） | `Core.RefLocator.GetRevision(Core.Offset)` → `Core.Revision`；`AreEqual` 加 `(ulong)` |
| 6.16 | 新增 `ComponentRef_InspectType_Overload_MatchesRuntimeType`（追加到文件末尾） | 覆盖 Task 1 新增的 `Untyped().Inspect(Type)` 重载（`TryGet` 路径） |

精确替换：

6.1

```diff
-            // Act & Assert
-            Assert.IsTrue(positionRef.Core.RefLocator.IsT(typeof(PositionComponent)));
-            Assert.IsFalse(positionRef.Core.RefLocator.IsT(typeof(VelocityComponent)));
+            // Act & Assert - v2 exposes type inspection on the untyped ref
+            var untypedRef = positionRef.Untyped();
+            Assert.IsTrue(untypedRef.Inspect<PositionComponent>());
+            Assert.IsFalse(untypedRef.Inspect<VelocityComponent>());
```

6.2

```diff
-            var entityType = positionRef.Core.RefLocator.GetT();
+            var entityType = positionRef.Untyped().RuntimeType;
```

6.3

```diff
-            var entityId = positionRef.Core.RefLocator.GetEntityId(positionRef.Core.Offset);
+            var entityId = positionRef.EntityId;
```

6.4

```diff
-            var refCore = positionRef.Core;
-            
-            // Assert
-            Assert.IsNotNull(refCore);
-            Assert.AreEqual(positionRef.Core.Offset, refCore.Offset);
+            // Act - untyping wraps the same kernel core
+            var refCore = positionRef.Core;
+            var untypedCore = positionRef.Untyped().Core;
+            
+            // Assert
+            Assert.IsNotNull(refCore);
+            Assert.AreSame(refCore, untypedCore);
```

6.5 删除整个方法（含后随空行）：

```csharp
        [Test]
        public void ComponentRef_CanRelocate()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();
            var originalOffset = positionRef.Core.Offset;
            var originalVersion = positionRef.Core.Version;
            
            // Act
            (positionRef.Core as ComponentRefCore).Allocate(positionRef.Core.RefLocator, originalOffset + 1, positionRef.Core.Version + 1);
            
            // Assert
            Assert.AreEqual(originalOffset + 1, positionRef.Core.Offset);
            Assert.AreEqual(originalVersion + 1, positionRef.Core.Version);
        }

```

6.6

```diff
             Assert.IsTrue(untypedRef.NotNull);
-            Assert.AreEqual(typedRef.Core.Offset, untypedRef.Core.Offset);
-            Assert.AreEqual(typedRef.Core.Version, untypedRef.Core.Version);
-            Assert.AreEqual(typedRef.Core.RefLocator, untypedRef.Core.RefLocator);
+            Assert.AreSame(typedRef.Core, untypedRef.Core);
```

6.7

```diff
             Assert.IsTrue(convertedTypedRef.NotNull);
-            Assert.AreEqual(typedRef.Core.Offset, convertedTypedRef.Core.Offset);
-            Assert.AreEqual(typedRef.Core.Version, convertedTypedRef.Core.Version);
-            Assert.AreEqual(typedRef.Core.RefLocator, convertedTypedRef.Core.RefLocator);
+            Assert.AreSame(typedRef.Core, convertedTypedRef.Core);
```

6.8（oldString 需含方法上下文，与 6.10 区分）

```diff
             // Act
             var typedRef = untypedRef.Typed<PositionComponent>();
             
             // Assert
             Assert.IsTrue(typedRef.NotNull);
-            Assert.AreEqual(positionRef.Core.Offset, typedRef.Core.Offset);
-            Assert.AreEqual(positionRef.Core.Version, typedRef.Core.Version);
-            Assert.AreEqual(positionRef.Core.RefLocator, typedRef.Core.RefLocator);
+            Assert.AreSame(positionRef.Core, typedRef.Core);
```

6.9

```diff
             Assert.IsTrue(untypedRef.NotNull);
-            Assert.AreEqual(typedRef.Core.Offset, untypedRef.Core.Offset);
-            Assert.AreEqual(typedRef.Core.Version, untypedRef.Core.Version);
-            Assert.AreEqual(typedRef.Core.RefLocator, untypedRef.Core.RefLocator);
+            Assert.AreSame(typedRef.Core, untypedRef.Core);
```

6.10（oldString 需含 `noSafeCheck` 上下文，与 6.8 区分）

```diff
             // Act - with safe check (default)
             var typedRef = untypedRef.Typed<PositionComponent>(noSafeCheck: false);
             
             // Assert
             Assert.IsTrue(typedRef.NotNull);
-            Assert.AreEqual(positionRef.Core.Offset, typedRef.Core.Offset);
-            Assert.AreEqual(positionRef.Core.Version, typedRef.Core.Version);
-            Assert.AreEqual(positionRef.Core.RefLocator, typedRef.Core.RefLocator);
+            Assert.AreSame(positionRef.Core, typedRef.Core);
```

6.11

```diff
             Assert.IsTrue(untypedRef.NotNull);
             Assert.IsTrue(untypedAgainRef.NotNull);
-            Assert.AreEqual(untypedRef.Core.Offset, untypedAgainRef.Core.Offset);
-            Assert.AreEqual(untypedRef.Core.Version, untypedAgainRef.Core.Version);
+            Assert.AreSame(untypedRef.Core, untypedAgainRef.Core);
```

6.12

```diff
-                if (compRef.Core.RefLocator.IsT(typeof(PositionComponent)))
+                if (compRef.Inspect<PositionComponent>())
                 {
                     foundPosition = true;
                     var typedPosRef = compRef.Typed<PositionComponent>();
-                    Assert.AreEqual(positionRef.Core.Offset, typedPosRef.Core.Offset);
+                    Assert.IsTrue(positionRef.Equals(typedPosRef));
                 }
-                else if (compRef.Core.RefLocator.IsT(typeof(VelocityComponent)))
+                else if (compRef.Inspect<VelocityComponent>())
                 {
                     foundVelocity = true;
                     var typedVelRef = compRef.Typed<VelocityComponent>();
-                    Assert.AreEqual(velocityRef.Core.Offset, typedVelRef.Core.Offset);
+                    Assert.IsTrue(velocityRef.Equals(typedVelRef));
                 }
```

6.13

```diff
             Assert.IsTrue(retypedRef.NotNull);
-            Assert.AreEqual(originalTypedRef.Core.Offset, retypedRef.Core.Offset);
-            Assert.AreEqual(originalTypedRef.Core.Version, retypedRef.Core.Version);
-            Assert.AreEqual(originalTypedRef.Core.RefLocator, retypedRef.Core.RefLocator);
+            Assert.AreSame(originalTypedRef.Core, retypedRef.Core);
```

6.14

```diff
-            var newRevision = componentRef.Core.RefLocator.ChangeRevision(componentRef.Core.Offset);
+            var newRevision = componentRef.Core.ChangeRevision();
             
             // Assert
             Assert.Greater(newRevision, initialRevision, "ChangeRevision should increment the revision");
-            Assert.AreEqual(newRevision, componentRef.Revision, "Revision property should reflect the change");
+            Assert.AreEqual((ulong)newRevision, componentRef.Revision, "Revision property should reflect the change");
```

6.15

```diff
-            var directRevision = componentRef.Core.RefLocator.GetRevision(componentRef.Core.Offset);
+            var directRevision = componentRef.Core.Revision;
             var propertyRevision = componentRef.Revision;
             
             // Assert
-            Assert.AreEqual(directRevision, propertyRevision, "Direct GetRevision call should match property access");
+            Assert.AreEqual((ulong)directRevision, propertyRevision, "Direct core revision should match property access");
             
             // Act - Change revision and check again
             componentRef.RW.X = 10.0f;
-            var newDirectRevision = componentRef.Core.RefLocator.GetRevision(componentRef.Core.Offset);
+            var newDirectRevision = componentRef.Core.Revision;
             var newPropertyRevision = componentRef.Revision;
             
             // Assert
-            Assert.AreEqual(newDirectRevision, newPropertyRevision, "After change, both methods should still match");
+            Assert.AreEqual((ulong)newDirectRevision, newPropertyRevision, "After change, both methods should still match");
```

6.16 在 `ComponentTestUnit` 类结尾的 `}` 之前追加：

```csharp
        [Test]
        public void ComponentRef_InspectType_Overload_MatchesRuntimeType()
        {
            // Arrange
            var entity = _world.CreateEntity();
            var positionRef = entity.CreateComponent<PositionComponent>();

            // Act
            var untypedRef = positionRef.Untyped();

            // Assert - v2 exposes a non-generic Inspect overload for runtime type checks
            Assert.IsTrue(untypedRef.Inspect(typeof(PositionComponent)));
            Assert.IsFalse(untypedRef.Inspect(typeof(VelocityComponent)));
            Assert.IsFalse(untypedRef.Inspect(null));
        }
```

- [ ] **Step 7: 机械适配 `Test/EntityTestUnit.cs`**

7.1 `Entity_Equals_DifferentWorldWithSameIdAndGeneration_ReturnsFalse`（46-47）：v2 `Entity` 构造需要共享 `EntityLocation`（internal，测试经 `InternalsVisibleTo` 访问）：

```diff
                 var entity = _world.CreateEntity();
-                var graph = _world.GetManager<EntityManager>().GetEntity(entity.EntityId);
-                var sameIdAndGenerationInOtherWorld = new Entity(otherWorld, entity.EntityId, graph.Generation);
+                var entityManager = _world.GetManager<EntityManager>();
+                Assert.IsTrue(entityManager.Table.TryGetLocation(entity.EntityId, out var location));
+                var sameIdAndGenerationInOtherWorld = new Entity(otherWorld, entity.EntityId, location, location.Generation);
```

7.2 `Entity_GetComponents_GenericArray_ReturnsCorrectTypes`（369-383）：同一实体重复添加同类型 dense 在 v2 抛异常，改写为单实例语义：

```diff
         [Test]
         public void Entity_GetComponents_GenericArray_ReturnsCorrectTypes()
         {
             // Arrange
             var entity = _world.CreateEntity();
-            var posRef1 = entity.CreateComponent<PositionComponent>();
-            var posRef2 = entity.CreateComponent<PositionComponent>();
+            entity.CreateComponent<PositionComponent>();
+            entity.CreateComponent<VelocityComponent>();
             
             // Act
             var positionComponents = entity.GetComponents<PositionComponent>();
+            var velocityComponents = entity.GetComponents<VelocityComponent>();
+            var healthComponents = entity.GetComponents<HealthComponent>();
             
             // Assert
-            Assert.AreEqual(2, positionComponents.Length);
+            Assert.AreEqual(1, positionComponents.Length);
+            Assert.AreEqual(1, velocityComponents.Length);
+            Assert.AreEqual(0, healthComponents.Length);
             Assert.IsTrue(positionComponents[0].NotNull);
-            Assert.IsTrue(positionComponents[1].NotNull);
+            Assert.IsTrue(velocityComponents[0].NotNull);
         }
```

7.3 `Entity_GetComponents_Collection_FillsCorrectly`（401）：

```diff
-            Assert.AreEqual(typeof(PositionComponent), results[0].Core.RefLocator.GetT());
+            Assert.AreEqual(typeof(PositionComponent), results[0].Untyped().RuntimeType);
```

7.4 `ComponentRef_ExpandMethod_CreatesValidUntypedReference`（597）：

```diff
-            Assert.AreEqual(typeof(PositionComponent), untypedRef.Core.RefLocator.GetT());
+            Assert.AreEqual(typeof(PositionComponent), untypedRef.RuntimeType);
```

- [ ] **Step 8: 机械适配 `Test/EntityMatcherTestUnit.cs`**

8.1 `EntityMatcher_ComplexFiltering`（149-169）：v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 已删除，改用 `World.Query`（v2 对 live structure 求值），计数断言不变：

```diff
-            // Act
-            var positionEntities = new List<Entity>();
-            var positionOrVelocityEntities = new List<Entity>();
-            var positionWithoutHealthEntities = new List<Entity>();
-            
-            foreach (var entity in entities)
-            {
-                if (positionMatcher.ComponentFilter(entity.GetComponents().Select(x => x.Core).ToArray()))
-                    positionEntities.Add(entity);
-                
-                if (positionOrVelocityMatcher.ComponentFilter(entity.GetComponents().Select(x => x.Core).ToArray()))
-                    positionOrVelocityEntities.Add(entity);
-                
-                if (positionWithoutHealthMatcher.ComponentFilter(entity.GetComponents().Select(x => x.Core).ToArray()))
-                    positionWithoutHealthEntities.Add(entity);
-            }
+            // Act - v2 evaluates matchers against live structures through World.Query
+            var positionEntities = new List<ulong>();
+            var positionOrVelocityEntities = new List<ulong>();
+            var positionWithoutHealthEntities = new List<ulong>();
+            _world.Query(positionMatcher, positionEntities);
+            _world.Query(positionOrVelocityMatcher, positionOrVelocityEntities);
+            _world.Query(positionWithoutHealthMatcher, positionWithoutHealthEntities);
             
             // Assert
             Assert.AreEqual(10, positionEntities.Count); // Every second entity (0, 2, 4, ...)
             Assert.AreEqual(13, positionOrVelocityEntities.Count); // Entities with Position or Velocity
             Assert.AreEqual(8, positionWithoutHealthEntities.Count); // Position entities without Health
```

8.2 `EntityMatcher_CanHandleEmptyComponentList`（184-189）：

```diff
-            // Act
-            var result = matcher.ComponentFilter(entity.GetComponents().Select(x => x.Core).ToArray());
-            
-            // Assert
-            Assert.IsFalse(result);
+            // Act - v2 matches against live structures, so query the empty entity
+            var matched = new List<ulong>();
+            _world.Query(matcher, matched);
+            
+            // Assert
+            CollectionAssert.DoesNotContain(matched, entity.EntityId);
```

- [ ] **Step 9: 机械适配 `Test/WorldTestUnit.cs`**

`World_CanDestroyEntity`（89）：v2 `GetEntity` 返回 `Entity` 结构体，默认值不可能为 null：

```diff
-            Assert.IsNull(world.GetManager<EntityManager>().GetEntity(entityId));
+            Assert.IsFalse(world.GetManager<EntityManager>().GetEntity(entityId).IsValid);
```

- [ ] **Step 10: 新增 `Test/EntityExtensionTestUnit.cs`（EntityExtension / tag 读路径）**

创建 `Test/EntityExtensionTestUnit.cs`（9 个测试；覆盖 Plan 1c Task 1 重写的 `EntityExtension` 三个方法与 tag 读路径语义——Task 1 质量审查发现这些行为在原 Task 3 测试计划中没有覆盖）：

```csharp
using CoreECS.Defines;

namespace CoreECS.Test
{
    [TestFixture]
    public class EntityExtensionTestUnit
    {
        private World _world;

        [SetUp]
        public void Setup()
        {
            _world = new World();
            _world.Startup();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Shutdown();
        }

        [Test]
        public void TryGetComponent_Dense_ReturnsRefAndTrueWhenPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();

            Assert.IsTrue(entity.TryGetComponent<PositionComponent>(out var componentRef));
            Assert.IsTrue(componentRef.NotNull);
            Assert.AreEqual(entity.EntityId, componentRef.EntityId);
        }

        [Test]
        public void TryGetComponent_Absent_ReturnsFalseAndDefault()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.TryGetComponent<PositionComponent>(out var componentRef));
            Assert.IsFalse(componentRef.NotNull);
        }

        [Test]
        public void TryGetComponent_Discrete_ReturnsRefAndTrueWhenPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new ManaComponent { Value = 4 });

            Assert.IsTrue(entity.TryGetComponent<ManaComponent>(out var componentRef));
            Assert.IsTrue(componentRef.NotNull);
            Assert.AreEqual(4, componentRef.RO.Value);
        }

        [Test]
        public void TryGetComponent_Tag_ReturnsTrueWithDefaultRef()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            Assert.IsTrue(entity.TryGetComponent<PlayerTag>(out var componentRef));
            Assert.IsFalse(componentRef.NotNull);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void GetOrCreateComponent_Dense_CreatesWhenAbsentAndReturnsExistingWhenPresent()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<PositionComponent>(out var created));
            Assert.IsTrue(created.NotNull);

            Assert.IsTrue(entity.GetOrCreateComponent<PositionComponent>(out var existing));
            Assert.AreEqual(created, existing);
        }

        [Test]
        public void GetOrCreateComponent_Discrete_CreatesWhenAbsent()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<ManaComponent>(out var created));
            Assert.IsTrue(created.NotNull);
            Assert.AreEqual(entity.EntityId, created.EntityId);
        }

        [Test]
        public void GetOrCreateComponent_Tag_ReturnsFalseAndDefaultRef()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.GetOrCreateComponent<PlayerTag>(out var created));
            Assert.IsFalse(created.NotNull);
            Assert.IsTrue(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void GetComponent_Tag_ReturnsDefaultWhileHasComponentIsTrue()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            Assert.IsTrue(entity.HasComponent<PlayerTag>());
            Assert.IsFalse(entity.GetComponent<PlayerTag>().NotNull);
            Assert.AreEqual(0, entity.GetComponents<PlayerTag>().Length);
        }

        [Test]
        public void GetComponents_OmitsTagsAndCollectionOverloadAddsDenseAndDiscrete()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent(new ManaComponent { Value = 1 });
            entity.CreateComponent<PlayerTag>();

            var components = entity.GetComponents();
            Assert.AreEqual(2, components.Length);
            foreach (var componentRef in components)
            {
                Assert.IsTrue(componentRef.NotNull);
                Assert.AreNotEqual(typeof(PlayerTag), componentRef.RuntimeType);
            }

            var results = new List<ComponentRef>();
            var added = entity.GetComponents(results);
            Assert.AreEqual(2, added);
            Assert.AreEqual(2, results.Count);
        }

        // Test components
        private struct PositionComponent : IComponent<PositionComponent>
        {
            public float X;
        }

        private struct ManaComponent : IDiscreteComponent<ManaComponent>
        {
            public int Value;
        }

        private struct PlayerTag : ITagComponent<PlayerTag>
        {
        }
    }
}
```

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityExtensionTestUnit`
Expected: PASS（9 个测试）

10b. 在 `Test/EntityCollectorTestUnit.cs` 类结尾的 `}` 之前追加 1 个回归测试（Task 2 质量评审修订——"实体销毁 → collector 移出 Collected"是 Task 2 决策 3 的核心行为，此前无覆盖）：

```csharp
        [Test]
        public void EntityCollector_DestroyedEntity_LeavesCollectedOnNextFlush()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            var collector = _world.CreateCollector(
                EntityMatcher.With.OfAll<PositionComponent>(),
                EntityCollectorFlag.Default);
            collector.Flush();
            AssertOnly(collector.Collected, entity.EntityId);

            _world.DestroyEntity(entity);
            collector.Flush();

            AssertEmpty(collector.Collected);
            AssertOnly(collector.Clashing, entity.EntityId);
        }
```

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj --filter FullyQualifiedName~EntityCollectorTestUnit`
Expected: PASS（现有测试 + 新增 1 个）

- [ ] **Step 11: 全量验证与数量调和**

11a. ECS 双目标编译：

Run: `PATH="$HOME/.dotnet:$PATH" dotnet build ECS/ECS.csproj`
Expected: PASS，net8.0 + netstandard2.1 均 0 errors

11b. Test 全量：

Run: `PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj`
Expected: **429 passed，0 failed**（Plan 1b 后 450 − 删除 17 + 19 + 18 + 1 = 55 + 新增 10 + 13 + 9 + 1 + 1 = 34）。若 Plan 1b 实际新增数不同，按实际数调和。

11c. 测试数复核（与 11b 输出一致）：

Run: `grep -rh "\[Test\]" Test/ | wc -l`
Expected: 429

11d. v1 引用清零扫描：

Run: `grep -rn "EntityGraph\|ComponentStore\|IComponentRefLocator\|IComponentRefCore" ECS/ Test/ --include="*.cs"`
Expected: 无输出

11e. 执行者必须把 11b 的实际通过总数与 11c 的实际计数记录到本计划 Self-Review（Task 3 第 29 条），两者必须一致。

- [ ] **Step 12: 提交**

```bash
git add Test/ComponentManagerTestUnit.cs Test/EntityManagerTestUnit.cs \
        Test/IntegrationTestUnit.cs Test/ComponentTestUnit.cs Test/EntityTestUnit.cs \
        Test/EntityMatcherTestUnit.cs Test/WorldTestUnit.cs Test/EntityExtensionTestUnit.cs \
        Test/EntityCollectorTestUnit.cs \
        ECS/Structures/EntityTable.cs ECS/Managers/EntityManager.cs
git rm Test/EntityGraphTestUnit.cs
git commit -m "refactor(test): migrate internal tests to v2 kernel" -m "Release entity locations on world shutdown (v1 parity) so entity handles become invalid after shutdown, and rewrite the internal manager suites against the v2 signals and EntityTable."
```

---

## Self-Review 记录

1. **handoff 覆盖**：约束 2（Entity v2）与约束 3（ComponentRef v2）→ Task 1；约束 6（EntityMatchManager 接线）、约束 7/9（World/MinimalWorld + `CreateEntity(mask)`）、约束 8（删 v1 存储）→ Task 2；内部测试迁移 → Task 3（见"本计划范围边界"）。
2. **占位符扫描**：Task 1 三个公开文件均为完整代码；内核适配为精确 diff；无 TBD/TODO/"类似 Task N"。
3. **类型一致性核对**：Entity 调用的内核 API 均来自 Plan 1b 或本任务 Step 1——`ComponentOrchestrator.{CreateEntity, DestroyEntity, AddDenseComponent, RemoveDenseComponent, AddDiscreteComponent, RemoveDiscreteComponent, AddTagComponent, RemoveTagComponent, HasComponent, GetComponentRef, RemoveComponent}`；`Structure.{Mask, DenseTypeIds, SpareSetOrNull, HasDiscrete, GetDenseVersion(uint,int), GetDiscreteVersion(uint,int), GetDenseRef, GetDiscreteRef, SetDiscrete}`；`EntityTable.{TryGetLocation, Create, Destroy}`；`ComponentRefCore.{NotNull, EntityId, Revision, ChangeRevision, Location, Generation, TypeId, Kind, Version}`。命名与 Plan 1b 计划逐字一致。
4. **CS0314 约束问题**：见设计决策 1；discrete 完整调用链已逐环放宽（`Entity` → `AddDiscreteComponent` → `RegisterDiscrete` / `SetDiscrete` → `GetOrCreateStore` → `DiscreteStore<T>`；读路径 `ComponentRef<T>.RO/RW` → `GetDiscreteRef`），tag 路径只放宽编排层两个方法，无遗漏。
5. **Tag 语义**：`GetComponent<Tag>` 返回 `default` 与 spec 4.2（第 124 行）一致；`CreateComponent<Tag>` 返回 `default` 是本计划按"Tag 无 ref"（handoff 约束 3）的补充决策，Task 3 应为 Tag / Discrete 增加行为测试。
6. **失效实体语义**：保持 v1 抛异常，避免 Task 3 无谓改写 `Entity_InvalidAccessThrowsException` / `Entity_DestroyEntityThenAccess_ThrowsException`。
7. **相等性**：结构相等保证 v1 的 `Create` / `Get` 引用相等语义（`EntityTestUnit.ComponentRef_EqualsOperator_DifferentReferencesSameComponent_ReturnsTrue` 与 `ComponentRef_EqualityOperator_SameComponent_ReturnsTrue`）。
8. **预期红状态有界**：Task 1 后 `ECS/ECS.csproj` 仅 `EntityGraph.cs`（5 处）与 `World.cs`（3 处）编译失败；Step 5 给出了精确错误表，超出即为实现缺陷。
9. **前向依赖显式化**：`ComponentManager.Orchestrator`（Task 2 注入）与内核约束放宽（Task 1 落地）已在本任务声明；Task 2 计划不得重复定义或回退约束。
10. **遗留记录**：spec 2.1 要求三种 kind 均调用 `OnCreate` / `OnDestroy`，Plan 1b 内核有意跳过 Tag 的 hook（Tag 默认空实现，行为等价）。若后续要求 Tag 自定义 hook，需在 Plan 1b 内核补 `InvokeTagCreate/InvokeTagDestroy`，本任务不处理。
11. **验证命令统一**：全部带 `PATH="$HOME/.dotnet:$PATH"`；提交信息按用户指定 `refactor(core): swap public entity and component ref to v2 kernel`。

### Task 2

12. **handoff 覆盖**：约束 6（`EntityMatchManager` 求值接线）、约束 7（`World` 保留 `Query` 重载、`MinimalWorld` 不变）、约束 9（`CreateEntity(mask)` 选择初始结构）、约束 8（删除 v1 存储）→ 本任务；约束 2/3/4 的公开面已由 Task 1 完成，管理器接线在本任务收口。
13. **占位符扫描**：`ComponentManager` / `EntityManager` / `IEntityMatcher` 为完整文件；`EntityMatchManager` / `World` / `EntityMatcher` / `ComponentRef` 为逐成员精确替换；删除清单附 grep 命令与预期输出；无 TBD/TODO。
14. **类型一致性核对**：`ComponentOrchestrator` 构造 `(StructureRegistry, EntityTable, IStructureObserver)`、`CreateEntity(ulong)` 返回 `(ulong, EntityLocation)`、`DestroyEntity(ulong)`；`EntityTable.{Create, Destroy, TryGetLocation, EntityIds}`；`Structure.{Entities, Mask, DenseTypeIds, SpareSetOrNull, HasDiscrete}`；`EntityLocation.{Structure, Row, Generation}`；`EntityMatcher.ComponentFilter(Structure, int)` 来自 Plan 1b Task 5（本任务改为 public）；命名与 Plan 1b 逐字一致。
15. **信号 payload 决策**：`(ulong entityId, Type compType)` 同时用于组件级与实体级两组 delegate；被删除的 `IComponentRefCore` / `EntityGraph` 无法作为 payload；`EntityLoseComponent` 的 `componentType == null` 约定为"实体销毁"。公开 API 破坏属 Phase 1c 预期，Task 3 内部测试按新 payload 重写。
16. **matcher 接口决策**：`ComponentFilter(Structure, int)` 必须进入 `IEntityMatcher` 才能让 `_changeCollector` / `World.Query` 在接口类型上求值（Plan 1b 仅把它放在 `EntityMatcher` internal；本任务把实现改为 public 并写入接口）。`Structure` 已是 public，无可见性障碍；v1 重载删除后 `m_changing` 失去用途一并删除。
17. **销毁事件完整性**：编排层 `DestroyEntity` 不产生观察者事件（Plan 1b 决策），因此 `EntityManager` 在销毁后补发一条 `OnEntityLoseComp(entityId, null)`，否则 collector 会把已销毁实体永久留在 `Collected`；`EntityMatchManager` 以 `destroyed=true` 走 v1 `WishDestroy` 等价路径。v1 在实体销毁时逐组件发移除事件的语义不再保留（组件级订阅者改由 `OnDestroy` hook 覆盖）。
18. **预期红状态有界**：Step 7 给出 Test 项目编译错误的逐文件清单（三个内部文件 + 五个行为文件），其中 `IntegrationTestUnit` 的 `GetComponentStore` 引用为 Task 1 Step 6 未列出、本次扫描发现，Task 3 需补适配。ECS 双目标 0 错误是本任务硬门槛。
19. **验证命令与提交**：统一 `PATH="$HOME/.dotnet:$PATH"`；提交信息按用户指定 `refactor(core): wire managers and world to v2 kernel`；`MinimalWorld` / `EntityExtension` 无需改动（已写入文件结构表）。

### Task 3

20. **handoff 约束 1（EntityManager v2）**：`EntityManagerTestUnit` 13 个测试钉死 id 单调递增、`EntityTable` 位置注册、销毁归还位置并递增 generation、位置复用、未知 id no-op、shutdown 释放全部位置并拒绝新实体；实现由 Task 2 Step 3 + 本任务 Step 1 补丁提供。
21. **handoff 约束 2（Entity v2）**：`EntityTestUnit` 机械适配（内部构造 `new Entity(otherWorld, id, location, generation)`、`Untyped().RuntimeType`、重复 dense 改写为单实例）；`Entity_IsValidAfterWorldShutdown_ReturnsFalse` 依赖本任务 Step 1 的 shutdown 补丁保持 v1 语义（实体句柄随 world shutdown 失效）。
22. **handoff 约束 3（ComponentRef v2）**：`ComponentTestUnit` 14 处机械替换（`Inspect` / `RuntimeType` / `EntityId` / `Core.Revision` / `Core.ChangeRevision()` / 同 core `AreSame` / 跨调用结构相等）；删除 `ComponentRef_CanRelocate`（v2 迁移由共享 `EntityLocation` 自动完成）。
23. **handoff 约束 4（ComponentManager v2）**：`ComponentManagerTestUnit` 10 个测试覆盖三种 kind 的 created/removed payload、dense/discrete 的 changed payload、实体销毁不发组件级移除信号、`OnCreate` / `OnDestroy` hook 经 Entity 路径执行。
24. **handoff 约束 5（匹配求值 v2）**：`EntityMatcherTestUnit` 的 v1 `ComponentFilter(IReadOnlyCollection<IComponentRefCore>)` 两处调用改为 `World.Query`，计数断言不变；row 级 tag/discrete 求值由 1b `EntityMatcherStructureTestUnit` 覆盖。
25. **handoff 约束 6（EntityMatchManager v2）**：`EntityMatcherTestUnit.EntityMatcher_Change_Existing_Component`、`IntegrationTestUnit` 动态增删工作流、`EntityCollectorTestUnit` 全量通过即验证 Task 2 Step 4 接线；Task 3 不新增实现。
26. **handoff 约束 7（World v2）**：`WorldTestUnit` 一行适配（`GetEntity` 返回结构体）后 26 个测试全绿；`MinimalWorld` / `CreateCollector` / `Query` 重载未改动。
27. **handoff 约束 8（删 v1 存储）**：`EntityGraphTestUnit` 删除、`ComponentManagerTestUnit` / `EntityManagerTestUnit` 重写、五个行为文件适配；Step 10d 的 grep 确认 `EntityGraph` / `ComponentStore` / `IComponentRefLocator` / `IComponentRefCore` 在 `ECS/` 与 `Test/` 零命中。
28. **handoff 约束 9（mask）**：`EntityManagerTestUnit.CreateEntity_WithInitialMask_SelectsMaskStructure` + `EntityMatcherTestUnit.EntityMask_CanFilterEntitiesByMask` / `WorldTestUnit.World_Query_Ulong_HonorsMaskAndComponentRules` 钉死初始结构选择与查询过滤；`SetMask` 迁移仍留待 CommandBuffer 阶段（Phase 6）。
29. **数量调和**：Plan 1b 后 450；删除 17（EntityGraphTestUnit）+ 19（旧 ComponentManagerTestUnit）+ 18（旧 EntityManagerTestUnit）+ 1（`ComponentRef_CanRelocate`）= 55；新增 10（ComponentManagerTestUnit）+ 13（EntityManagerTestUnit）+ 9（EntityExtensionTestUnit）+ 1（`ComponentRef_InspectType_Overload_MatchesRuntimeType`）+ 1（`EntityCollectorTestUnit`）= 34；预期 **450 − 55 + 34 = 429**。执行者必须把 `dotnet test` 实际通过总数与 `grep -rh "\[Test\]" Test/ | wc -l` 结果记录到本条；若 Plan 1b 实际新增数与计划不同，按实际数调和后更新本条。
    - **执行结果**：`dotnet test Test/Test.csproj` → 通过 429，失败 0，跳过 0（总计 429）；`grep -rh "\[Test\]" Test/ | wc -l` → 429。两者一致，与预期 429 相符。v1 引用扫描（11d）零命中；ECS 双目标 0 errors。
30. **Task 2 遗漏修正**：Task 2 Step 7 的 EntityTestUnit 预期红表只列编译错误；`Entity_IsValidAfterWorldShutdown_ReturnsFalse` 是运行时失败——Task 2 的 `EntityManager.OnManagerDestroyed` 未归还位置（v1 会 `EntityGraph.Pool.Release`）。Task 3 Step 1 以 `EntityTable.Clear()` + `OnManagerDestroyed` 调用补齐，并新增 `EntityManager_Shutdown_ReleasesAllLocationsAndRejectsNewEntities` 钉死。
31. **验证命令与提交**：全部 `PATH="$HOME/.dotnet:$PATH"`；提交信息按用户指定 `refactor(test): migrate internal tests to v2 kernel`（body 说明 shutdown 补丁）；本任务完成后 Plan 1c 收口，后续阶段为 `IEntityQuery`（Phase 3）、系统分组与排序（Phase 4）、World 合并与生命周期收敛（Phase 5）、CommandBuffer（Phase 6）。
32. **（Task 1 质量评审修订 → Task 3 补覆盖）**：Task 1 质量审查确认实现正确、错误面有界（16 errors = 8 站点 × 2 TFM，全部在 `EntityGraph.cs` / `World.cs`），但发现四处 Important 覆盖缺口（均属测试计划而非 Task 1 代码缺陷），已并入本任务：(a) `EntityExtension` 三个方法零覆盖 → Step 10 新增 `EntityExtensionTestUnit`（9 个测试，含 dense/discrete/tag 的存在/缺失与 tag 返回 default 语义）；(b) tag 读路径（`GetComponent<Tag>` 返回 default 而 `HasComponent` 为 true、`GetComponents()` 排除 tag）→ 同一 fixture 覆盖；(c) 非泛型 `GetComponents(ICollection<ComponentRef>)` 因 `EntityGraphTestUnit` 删除而失去唯一覆盖 → 同一 fixture 覆盖（含返回计数与集合填充）；(d) `ComponentRef.Inspect(Type)` 重载无覆盖 → Step 6.16 新增测试。Task 3 预期收口 418 → 429。Task 1 自身的两个 Minor（`RemoveComponent` 无 `default` 分支、XML 文档未覆盖 `RequireLocation` 异常）记录为可选加固，不阻塞。
33. **（Task 2 质量评审修订 → Task 3 补漏）**：Task 2 质量审查确认接线正确（独立 smoke 测试 23 项全过）但发现两处 Important，均并入本任务：(a) `EntityManager.DestroyEntity` 重入守卫——hook 内重入销毁会绕过 orchestrator 的 `m_destroying` no-op（orchestrator 表项在 finally 才移除，内层 `TryGetLocation` 仍成功），导致 `OnEntityLoseComp` 发出两次（v1 只发一次）；Step 1c 以管理器级 `m_destroying` 集合 + try/finally 保证"恰好一条"事件。(b) 销毁→collector 路径无回归测试——Task 2 决策 3 的"已销毁实体在下次 Flush 移出 Collected"是核心新行为，Step 10b 在 `EntityCollectorTestUnit` 新增断言（Flush 后移出 Collected、进入 Clashing）。Task 3 预期收口 428 → 429。
