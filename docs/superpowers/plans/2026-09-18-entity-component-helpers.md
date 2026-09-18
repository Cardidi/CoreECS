# Entity Helper Extensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按 spec `docs/superpowers/specs/2026-09-18-entity-component-helpers-design.md` 为 Entity 增加 ref 直达/安全读写/存在性删除扩展方法、为 IEntityMatcher 增加实体级 `IsMatch`，并同步分析器与文档。

**Architecture:** 全部以扩展方法形式落在 `EntityExtension` / `EntityMatcherExtension`；Entity 新增 internal 访问面（Location/RawLocation/Generation/RawWorld/ComponentManager、Orchestrator 改 internal）供程序集内复用；Roslyn 分析器把 `EntityExtension.GetRO/GetRW` 识别为新的 ref 来源，保证 ECS0001/ECS0002 不漏报。

**Tech Stack:** C#（Kernel `LangVersion 12`，`net8.0` + `netstandard2.1`；Analyzers `netstandard2.0`）、NUnit 3、Microsoft.CodeAnalysis.Testing。

## Global Constraints

- Kernel 的 `ImplicitUsings`/`Nullable` 为 disable；Test 为 enable；不要统一这些设置。
- 本机 PATH 中的 `dotnet` 是 SDK 9.0.306，被 `global.json`（8.0.0 / latestMinor）拒绝；本计划所有验证命令统一用 `$HOME/.dotnet/dotnet`（SDK 8.0.423）。
- 公共 API 只增不改；新增/修改的 public 与 internal 成员都要写 XML 注释（仓库既有风格）。
- 不引入新依赖；不修改 `global.json`。
- 提交信息遵循 Conventional Commits；每个任务一个 commit，命令中已给出。
- 筛选测试命令：`$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~<类名>" --nologo -v q`。
- 全量验证：`$HOME/.dotnet/dotnet build CoreECS.sln --nologo`、`$HOME/.dotnet/dotnet test Test/Test.csproj --nologo -v q`。

---

### Task 1: Entity internal 访问面 + GetRO/GetRW/RequireComponent

**Files:**
- Modify: `Kernel/Entity.cs`（新增 internal 成员；`Orchestrator` 由 private 改 internal）
- Modify: `Kernel/EntityExtension.cs`（类注释 + 3 个扩展方法 + 3 个私有异常工厂）
- Test: `Test/EntityExtensionTestUnit.cs`（在 `GetComponents_OmitsTagsAndCollectionOverloadAddsDenseAndSparse` 之后、`// Test components` 之前插入 10 个测试）

**Interfaces:**
- Consumes: 现有 `Entity.HasComponent<T>()`、`Entity.GetComponent<T>()`、`Entity.EntityId`、`EntityLocation.Row/Structure`（internal）、`ComponentOrchestrator.GetComponentRef<T>(ulong)`（internal）
- Produces:
  - `internal EntityLocation Entity.Location { get; }`（无效抛 `InvalidOperationException`）
  - `internal EntityLocation Entity.RawLocation { get; }`
  - `internal uint Entity.Generation { get; }`
  - `internal IWorld Entity.RawWorld { get; }`
  - `internal ComponentManager Entity.ComponentManager { get; }`
  - `internal ComponentOrchestrator Entity.Orchestrator { get; }`
  - `public static ComponentRef<TComp> EntityExtension.RequireComponent<TComp>(this Entity)`
  - `public static ref readonly TComp EntityExtension.GetRO<TComp>(this Entity)`
  - `public static ref TComp EntityExtension.GetRW<TComp>(this Entity)`

- [ ] **Step 1: 写失败测试**

在 `Test/EntityExtensionTestUnit.cs` 中，把以下方法插入到 `GetComponents_OmitsTagsAndCollectionOverloadAddsDenseAndSparse` 方法之后、`// Test components` 注释之前：

```csharp
        [Test]
        public void GetRO_Dense_ReturnsCurrentValue()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new PositionComponent { X = 3.5f });

            ref readonly var position = ref entity.GetRO<PositionComponent>();

            Assert.AreEqual(3.5f, position.X);
        }

        [Test]
        public void GetRW_Dense_WritesValueAndBumpsRevision()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new PositionComponent { X = 1 });

            var before = entity.GetComponent<PositionComponent>().Revision;
            ref var position = ref entity.GetRW<PositionComponent>();
            position.X = 7;

            Assert.AreEqual(7, entity.GetComponent<PositionComponent>().RO.X);
            Assert.Greater(entity.GetComponent<PositionComponent>().Revision, before);
        }

        [Test]
        public void GetRO_Sparse_ReturnsCurrentValue()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new ManaComponent { Value = 4 });

            ref readonly var mana = ref entity.GetRO<ManaComponent>();

            Assert.AreEqual(4, mana.Value);
        }

        [Test]
        public void GetRW_Sparse_WritesValue()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new ManaComponent { Value = 4 });

            ref var mana = ref entity.GetRW<ManaComponent>();
            mana.Value = 9;

            Assert.AreEqual(9, entity.GetComponent<ManaComponent>().RO.Value);
        }

        [Test]
        public void GetRO_AbsentComponent_ThrowsWithMissingMessage()
        {
            var entity = _world.CreateEntity();

            var exception = Assert.Throws<InvalidOperationException>(
                () => { entity.GetRO<PositionComponent>(); });

            StringAssert.Contains("does not have component", exception.Message);
        }

        [Test]
        public void GetRW_TagComponent_ThrowsNoDataMessage()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            var exception = Assert.Throws<InvalidOperationException>(
                () => { entity.GetRW<PlayerTag>(); });

            StringAssert.Contains("carries no data", exception.Message);
        }

        [Test]
        public void GetRO_DestroyedEntity_Throws()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            _world.DestroyEntity(entity);

            Assert.Throws<InvalidOperationException>(() => { entity.GetRO<PositionComponent>(); });
        }

        [Test]
        public void RequireComponent_Present_ReturnsLiveRef()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new PositionComponent { X = 2 });

            var componentRef = entity.RequireComponent<PositionComponent>();

            Assert.IsTrue(componentRef.NotNull);
            Assert.AreEqual(2, componentRef.RO.X);
        }

        [Test]
        public void RequireComponent_Tag_ReturnsDefaultRef()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            var componentRef = entity.RequireComponent<PlayerTag>();

            Assert.IsFalse(componentRef.NotNull);
        }

        [Test]
        public void RequireComponent_Absent_Throws()
        {
            var entity = _world.CreateEntity();

            var exception = Assert.Throws<InvalidOperationException>(
                () => entity.RequireComponent<PositionComponent>());

            StringAssert.Contains("does not have component", exception.Message);
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityExtensionTestUnit" --nologo -v q`
Expected: 编译失败，`CS1061`：`Entity` 不包含 `GetRO` / `GetRW` / `RequireComponent`。

- [ ] **Step 3: 给 Entity 增加 internal 访问面**

在 `Kernel/Entity.cs` 的 `public bool IsValid` 属性之后插入：

```csharp
        /// <summary>Live location of this entity; throws when the handle is stale.</summary>
        internal EntityLocation Location => RequireLocation();

        /// <summary>Raw pooled location; may be stale or null. Do not cache.</summary>
        internal EntityLocation RawLocation => m_location;

        /// <summary>Generation captured by this handle, compared against the location.</summary>
        internal uint Generation => m_generation;

        /// <summary>Owning world without the default-entity guard; may be null.</summary>
        internal IWorld RawWorld => m_world;

        /// <summary>Owning component manager; may be null for a default entity.</summary>
        internal ComponentManager ComponentManager => m_componentManager;
```

把文件底部 `private ComponentOrchestrator Orchestrator` 改为 `internal ComponentOrchestrator Orchestrator`，并把该属性注释改为：

```csharp
        /// <summary>Component kernel orchestrator; throws when the entity is not associated with one.</summary>
```

- [ ] **Step 4: 实现 EntityExtension 方法**

在 `Kernel/EntityExtension.cs` 顶部加 `using System;`，把类注释第一行改为 `/// Entity component access helpers built on the Entity API (public + internal).`，并在类内追加：

```csharp
        /// <summary>
        /// Gets the component of type <typeparamref name="TComp"/>; throws when it is absent.
        /// Tag components have no data but are still "present": they yield a default ref.
        /// </summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <returns>A live ref for dense/sparse components; a default ref for tags.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not alive or the component is absent.</exception>
        public static ComponentRef<TComp> RequireComponent<TComp>(this Entity entity)
            where TComp : struct, IComponent<TComp>
        {
            if (!entity.HasComponent<TComp>()) throw MissingComponent<TComp>(entity);
            return entity.GetComponent<TComp>();
        }

        /// <summary>
        /// Gets a read-only reference to the component of type <typeparamref name="TComp"/>.
        /// Reading does not touch the revision.
        /// </summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <returns>A read-only reference to the live component data.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not alive, the component is absent, or the component is a tag.</exception>
        public static ref readonly TComp GetRO<TComp>(this Entity entity) where TComp : struct, IComponent<TComp>
        {
            _ = entity.Location;
            var core = entity.Orchestrator.GetComponentRef<TComp>(entity.EntityId);
            if (core == null) throw RefAccessError<TComp>(entity);

            var componentRef = new ComponentRef<TComp>(core);
            return ref componentRef.RO;
        }

        /// <summary>
        /// Gets a writable reference to the component of type <typeparamref name="TComp"/>.
        /// Acquiring the ref bumps the revision and may notify change handlers, matching
        /// <see cref="ComponentRef{T}.RW"/>.
        /// </summary>
        /// <param name="entity">Entity to get the component from.</param>
        /// <returns>A writable reference to the live component data.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not alive, the component is absent, or the component is a tag.</exception>
        public static ref TComp GetRW<TComp>(this Entity entity) where TComp : struct, IComponent<TComp>
        {
            _ = entity.Location;
            var core = entity.Orchestrator.GetComponentRef<TComp>(entity.EntityId);
            if (core == null) throw RefAccessError<TComp>(entity);

            var componentRef = new ComponentRef<TComp>(core);
            return ref componentRef.RW;
        }

        private static InvalidOperationException MissingComponent<TComp>(Entity entity)
            where TComp : struct, IComponent<TComp>
        {
            return new InvalidOperationException(
                $"Entity {entity.EntityId} does not have component {typeof(TComp).Name}.");
        }

        private static InvalidOperationException TagHasNoData<TComp>() where TComp : struct, IComponent<TComp>
        {
            return new InvalidOperationException($"Tag component {typeof(TComp).Name} carries no data.");
        }

        private static InvalidOperationException RefAccessError<TComp>(Entity entity)
            where TComp : struct, IComponent<TComp>
        {
            return entity.HasComponent<TComp>() ? TagHasNoData<TComp>() : MissingComponent<TComp>(entity);
        }
```

- [ ] **Step 5: 运行测试确认通过**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityExtensionTestUnit" --nologo -v q`
Expected: `已通过! - 失败: 0，通过: 19，已跳过: 0，总计: 19`

- [ ] **Step 6: Commit**

```bash
git add Kernel/Entity.cs Kernel/EntityExtension.cs Test/EntityExtensionTestUnit.cs
git commit -m "feat(core): add Entity internal accessors and RO/RW/Require helpers"
```

---

### Task 2: TryReadComponent / ReadComponent / TryDestroyComponent

**Files:**
- Modify: `Kernel/EntityExtension.cs`
- Test: `Test/EntityExtensionTestUnit.cs`（插入到 Task 1 最后一个测试 `RequireComponent_Absent_Throws` 之后、`// Test components` 之前）

**Interfaces:**
- Consumes: Task 1 的 `RequireComponent<T>`、私有 `TagHasNoData<T>()`；现有 `TryGetComponent<T>(out ComponentRef<T>)`、`HasComponent<T>()`、`DestroyComponent<T>()`
- Produces:
  - `public static bool EntityExtension.TryReadComponent<TComp>(this Entity, out TComp value)`
  - `public static TComp EntityExtension.ReadComponent<TComp>(this Entity)`
  - `public static bool EntityExtension.TryDestroyComponent<TComp>(this Entity)`

- [ ] **Step 1: 写失败测试**

在 `Test/EntityExtensionTestUnit.cs` 中，把以下方法插入到 Task 1 新增的 `RequireComponent_Absent_Throws` 之后、`// Test components` 注释之前：

```csharp
        [Test]
        public void TryReadComponent_Present_ReturnsCopy()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new PositionComponent { X = 5 });

            var found = entity.TryReadComponent<PositionComponent>(out var value);

            Assert.IsTrue(found);
            Assert.AreEqual(5, value.X);
        }

        [Test]
        public void TryReadComponent_Tag_ReturnsTrueWithDefault()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            var found = entity.TryReadComponent<PlayerTag>(out var value);

            Assert.IsTrue(found);
            Assert.AreEqual(default(PlayerTag), value);
        }

        [Test]
        public void TryReadComponent_Absent_ReturnsFalse()
        {
            var entity = _world.CreateEntity();

            var found = entity.TryReadComponent<PositionComponent>(out var value);

            Assert.IsFalse(found);
            Assert.AreEqual(default(PositionComponent), value);
        }

        [Test]
        public void ReadComponent_Present_ReturnsCopy()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent(new ManaComponent { Value = 6 });

            var value = entity.ReadComponent<ManaComponent>();

            Assert.AreEqual(6, value.Value);
        }

        [Test]
        public void ReadComponent_Absent_Throws()
        {
            var entity = _world.CreateEntity();

            Assert.Throws<InvalidOperationException>(() => entity.ReadComponent<PositionComponent>());
        }

        [Test]
        public void ReadComponent_Tag_ThrowsNoDataMessage()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            var exception = Assert.Throws<InvalidOperationException>(
                () => entity.ReadComponent<PlayerTag>());

            StringAssert.Contains("carries no data", exception.Message);
        }

        [Test]
        public void TryDestroyComponent_Present_ReturnsTrueAndRemoves()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();

            Assert.IsTrue(entity.TryDestroyComponent<PositionComponent>());
            Assert.IsFalse(entity.HasComponent<PositionComponent>());
        }

        [Test]
        public void TryDestroyComponent_Tag_ReturnsTrueAndRemoves()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PlayerTag>();

            Assert.IsTrue(entity.TryDestroyComponent<PlayerTag>());
            Assert.IsFalse(entity.HasComponent<PlayerTag>());
        }

        [Test]
        public void TryDestroyComponent_Absent_ReturnsFalse()
        {
            var entity = _world.CreateEntity();

            Assert.IsFalse(entity.TryDestroyComponent<PositionComponent>());
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityExtensionTestUnit" --nologo -v q`
Expected: 编译失败，`CS1061`：`Entity` 不包含 `TryReadComponent` / `ReadComponent` / `TryDestroyComponent`。

- [ ] **Step 3: 实现三个方法**

在 `Kernel/EntityExtension.cs` 的 `GetRW<TComp>` 之后、`MissingComponent<TComp>` 之前追加：

```csharp
        /// <summary>
        /// Copies the component of type <typeparamref name="TComp"/> by value.
        /// A present tag reports <c>true</c> with <c>default</c> as the value, matching
        /// <see cref="TryGetComponent{TComp}"/>; an absent component reports <c>false</c>.
        /// </summary>
        /// <param name="entity">Entity to read the component from.</param>
        /// <param name="value">Current component value, or default when absent/tag.</param>
        /// <returns>True when the component exists (tags included).</returns>
        public static bool TryReadComponent<TComp>(this Entity entity, out TComp value)
            where TComp : struct, IComponent<TComp>
        {
            if (!entity.TryGetComponent<TComp>(out var componentRef))
            {
                value = default;
                return false;
            }

            value = componentRef.NotNull ? componentRef.RO : default;
            return true;
        }

        /// <summary>
        /// Copies the component of type <typeparamref name="TComp"/> by value.
        /// </summary>
        /// <param name="entity">Entity to read the component from.</param>
        /// <returns>The current component value.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity is not alive, the component is absent, or the component is a tag.</exception>
        public static TComp ReadComponent<TComp>(this Entity entity) where TComp : struct, IComponent<TComp>
        {
            var componentRef = entity.RequireComponent<TComp>();
            if (!componentRef.NotNull) throw TagHasNoData<TComp>();
            return componentRef.RO;
        }

        /// <summary>Destroys the component of type <typeparamref name="TComp"/> when present.</summary>
        /// <param name="entity">Entity to remove the component from.</param>
        /// <returns>True when the component existed and was removed.</returns>
        public static bool TryDestroyComponent<TComp>(this Entity entity) where TComp : struct, IComponent<TComp>
        {
            if (!entity.HasComponent<TComp>()) return false;
            entity.DestroyComponent<TComp>();
            return true;
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityExtensionTestUnit" --nologo -v q`
Expected: `已通过! - 失败: 0，通过: 28，已跳过: 0，总计: 28`

- [ ] **Step 5: Commit**

```bash
git add Kernel/EntityExtension.cs Test/EntityExtensionTestUnit.cs
git commit -m "feat(core): add value-read and conditional-destroy entity helpers"
```

---

### Task 3: IEntityMatcher.IsMatch(Entity)

**Files:**
- Modify: `Kernel/EntityMatcherExtension.cs`（在类末尾 `Query` 重载之后、类结束 `}` 之前追加）
- Test: `Test/EntityMatcherTestUnit.cs`（插入 8 个测试 + 1 个 sparse 测试组件）

**Interfaces:**
- Consumes: Task 1 的 `internal EntityLocation Entity.Location`；`IEntityMatcher.EntityMask`、`IEntityMatcher.ComponentFilter(Structure, int)`（public）；`CoreECS.Utils.Assertion.ArgumentNotNull`
- Produces: `public static bool EntityMatcherExtension.IsMatch(this IEntityMatcher matcher, Entity entity)`

- [ ] **Step 1: 写失败测试**

在 `Test/EntityMatcherTestUnit.cs` 中，把以下方法插入到最后一个测试 `EntityMatcher_IsRelevantComponent_WithMaskNoConditions_TreatsAllAsRelevant` 之后、`// Test components` 注释之前：

```csharp
        [Test]
        public void IsMatch_OfAll_TrueWhenAllPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<VelocityComponent>();

            Assert.IsTrue(EntityMatcher.With.OfAll<PositionComponent>().OfAll<VelocityComponent>().IsMatch(entity));
            Assert.IsFalse(EntityMatcher.With.OfAll<PositionComponent>().OfAll<HealthComponent>().IsMatch(entity));
        }

        [Test]
        public void IsMatch_OfAny_TrueWhenAnyPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<VelocityComponent>();

            var matcher = EntityMatcher.With.OfAny<PositionComponent>().OfAny<VelocityComponent>();

            Assert.IsTrue(matcher.IsMatch(entity));
        }

        [Test]
        public void IsMatch_OfNone_FalseWhenExcludedPresent()
        {
            var entity = _world.CreateEntity();
            entity.CreateComponent<PositionComponent>();
            entity.CreateComponent<HealthComponent>();

            var matcher = EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>();

            Assert.IsFalse(matcher.IsMatch(entity));
        }

        [Test]
        public void IsMatch_MaskMismatch_ReturnsFalse()
        {
            var entity = _world.CreateEntity(0b0001);
            var matcher = EntityMatcher.WithMask(0b0010);

            Assert.IsFalse(matcher.IsMatch(entity));
        }

        [Test]
        public void IsMatch_EmptyMatcher_ReturnsTrue()
        {
            var entity = _world.CreateEntity();

            Assert.IsTrue(EntityMatcher.With.IsMatch(entity));
        }

        [Test]
        public void IsMatch_SparseCondition_ResolvesPerRow()
        {
            var withMana = _world.CreateEntity();
            withMana.CreateComponent(new ManaComponent { Value = 1 });
            var withoutMana = _world.CreateEntity();

            var matcher = EntityMatcher.With.OfAll<ManaComponent>();

            Assert.IsTrue(matcher.IsMatch(withMana));
            Assert.IsFalse(matcher.IsMatch(withoutMana));
        }

        [Test]
        public void IsMatch_DestroyedEntity_Throws()
        {
            var entity = _world.CreateEntity();
            _world.DestroyEntity(entity);

            Assert.Throws<InvalidOperationException>(() => EntityMatcher.With.IsMatch(entity));
        }

        [Test]
        public void IsMatch_NullMatcher_Throws()
        {
            var entity = _world.CreateEntity();

            Assert.Throws<ArgumentNullException>(() => EntityMatcherExtension.IsMatch(null, entity));
        }
```

并在文件末尾的测试组件区（`UnrelatedTagComponent` 之后）追加：

```csharp
        private struct ManaComponent : ISparseComponent<ManaComponent>
        {
            public int Value;
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityMatcherTestUnit" --nologo -v q`
Expected: 编译失败，`CS1061`：`IEntityMatcher` 不包含 `IsMatch`。

- [ ] **Step 3: 实现 IsMatch**

在 `Kernel/EntityMatcherExtension.cs` 的最后一个 `Query` 重载之后、类结束 `}` 之前追加：

```csharp
        /// <summary>
        /// Evaluates the matcher against a single entity with the same mask and component
        /// semantics used by queries and collectors.
        /// </summary>
        /// <param name="matcher">Matcher to evaluate.</param>
        /// <param name="entity">Entity to test; must be alive.</param>
        /// <returns>True when the entity satisfies the mask, all, any and none criteria.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="matcher"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown when the entity is no longer alive.</exception>
        public static bool IsMatch(this IEntityMatcher matcher, Entity entity)
        {
            CoreECS.Utils.Assertion.ArgumentNotNull(matcher, nameof(matcher));

            var location = entity.Location;
            return (matcher.EntityMask & location.Structure.Mask) != 0UL
                   && matcher.ComponentFilter(location.Structure, location.Row);
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~EntityMatcherTestUnit" --nologo -v q`
Expected: `已通过! - 失败: 0`，无失败用例。

- [ ] **Step 5: Commit**

```bash
git add Kernel/EntityMatcherExtension.cs Test/EntityMatcherTestUnit.cs
git commit -m "feat(core): add entity-level IsMatch to IEntityMatcher"
```

---

### Task 4: 分析器识别 Entity.GetRO/GetRW

**Files:**
- Modify: `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs`
- Modify: `Test/AnalyzerTestSource.cs`（在 `namespace CoreECS` 的 `Entity` 类之后插入 stub `EntityExtension`）
- Modify: `Test/RefInvalidationAnalyzerTestUnit.cs`（追加 5 个测试）
- Modify: `Test/RefParameterAnalyzerTestUnit.cs`（追加 1 个测试）

**Interfaces:**
- Consumes: 现有 `TryResolveSource`、`FormatTypeArguments`、`AnalyzerTestSource.Wrap`
- Produces: 分析器把 `GetRO<T>()` / `GetRW<T>()` 调用识别为 ref 来源；诊断 origin 显示为 `Entity.GetRO<T>()` / `Entity.GetRW<T>()`

- [ ] **Step 1: 写失败测试（先补 stub，再加测试）**

在 `Test/AnalyzerTestSource.cs` 的 `Stubs` 字符串里，`namespace CoreECS` 内的 `Entity` 类结束 `}` 之后、`World` 类之前插入：

```csharp
    public static class EntityExtension
    {
        private static class Storage<T> where T : struct, IComponent<T>
        {
            public static T Value;
        }

        public static ref readonly T GetRO<T>(this Entity entity) where T : struct, IComponent<T>
            => ref Storage<T>.Value;

        public static ref T GetRW<T>(this Entity entity) where T : struct, IComponent<T>
            => ref Storage<T>.Value;
    }
```

在 `Test/RefInvalidationAnalyzerTestUnit.cs` 末尾（`PragmaDisable_SuppressesEcs0001` 之后）追加：

```csharp
        [Test]
        public async Task EntityGetRw_UsedAfterCreateEntity_Reports()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetRW<PositionComponent>();
                position.X = 1;
                {|ECS0001:world.CreateEntity()|};
                position.Y = 2;
");
        }

        [Test]
        public async Task EntityGetRo_UsedAfterStructuralChange_Reports()
        {
            await VerifyAsync(@"
                ref readonly var position = ref entity.GetRO<PositionComponent>();
                var x = position.X;
                {|ECS0001:entity.DestroyComponent<PositionComponent>()|};
                var y = position.Y;
");
        }

        [Test]
        public async Task EntityGetRw_FinishedBeforeStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                ref var position = ref entity.GetRW<PositionComponent>();
                position.X = 1;
                position.Y = 2;
                world.CreateEntity();
");
        }

        [Test]
        public async Task EntityGetRw_AcquiredAfterStructuralChange_DoesNotReport()
        {
            await VerifyAsync(@"
                world.CreateEntity();
                ref var position = ref entity.GetRW<PositionComponent>();
                position.X = 1;
");
        }

        [Test]
        public async Task EntityGetRoValueCopy_DoesNotReport()
        {
            await VerifyAsync(@"
                var x = entity.GetRO<PositionComponent>().X;
                world.CreateEntity();
");
        }
```

在 `Test/RefParameterAnalyzerTestUnit.cs` 末尾（`RefPassedToUnsafeMethod_ReportsEcs0001AtCall` 之后）追加：

```csharp
        [Test]
        public async Task EntityGetRwRefParameter_UsedAfterStructuralChange_ReportsEcs0002()
        {
            await VerifyAsync(@"
                void Fill(ref PositionComponent value)
                {
                    {|ECS0002:entity.CreateComponent<VelocityComponent>()|};
                    value.X = 1;
                }
                Fill(ref entity.GetRW<PositionComponent>());
");
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~RefInvalidationAnalyzerTestUnit|FullyQualifiedName~RefParameterAnalyzerTestUnit" --nologo -v q`
Expected: 新增用例失败（`ECS0001` / `ECS0002` 未在标记位置报告），其余用例通过。

- [ ] **Step 3: 实现分析器识别**

在 `Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs` 的 `TryResolveSource` 的 `switch (expression)` 中，在结构体列访问器 case 之后追加：

```csharp
                case InvocationExpressionSyntax invocation
                    when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                         && IsEntityRefAccessor(method):
                    origin = GetEntityRefAccessorName(method, model, invocation.SpanStart);
                    return true;
```

并在 `IsStructureColumnAccessor` 方法之后追加：

```csharp
        private static bool IsEntityRefAccessor(IMethodSymbol method)
        {
            if (method.Name != "GetRO" && method.Name != "GetRW")
            {
                return false;
            }

            if (method.ContainingType?.ToDisplayString() != "CoreECS.EntityExtension")
            {
                return false;
            }

            return method.ReturnsByRef || method.ReturnsByRefReadonly;
        }

        private static string GetEntityRefAccessorName(
            IMethodSymbol method,
            SemanticModel model,
            int position)
        {
            return "Entity." + method.Name + FormatTypeArguments(method, model, position) + "()";
        }
```

- [ ] **Step 4: 运行测试确认通过**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~RefInvalidationAnalyzerTestUnit|FullyQualifiedName~RefParameterAnalyzerTestUnit" --nologo -v q`
Expected: 全部通过，无失败用例。

- [ ] **Step 5: 运行分析器相关的其余测试确认无回归**

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~Analyzer" --nologo -v q`
Expected: 全部通过（含 `KernelSourceAnalysisTestUnit` / `RefInvalidationAnalyzerTestUnit` / `RefParameterAnalyzerTestUnit` / `UnsafeTransitivityAnalyzerTestUnit` 等）。

- [ ] **Step 6: Commit**

```bash
git add Analyzers/RefInvalidatedByStructuralChangeAnalyzer.cs Test/AnalyzerTestSource.cs Test/RefInvalidationAnalyzerTestUnit.cs Test/RefParameterAnalyzerTestUnit.cs
git commit -m "feat(core): track Entity.GetRO/GetRW as ref origins in analyzer"
```

---

### Task 5: 文档更新（新方法 + 修正过期 API 名）

**Files:**
- Modify: `docs/QUICK_START.md`
- Modify: `docs/QUICK_START.zh-CN.md`
- Modify: `README.md`
- Modify: `README.zh-CN.md`

**Interfaces:**
- Consumes: Task 1-3 的最终方法签名
- Produces: 文档与实现一致

- [ ] **Step 1: 更新 QUICK_START.md 第 5 节**

把第 5 节末尾从 `### Extension helpers` 到 `GetOrCreateComponent` 说明段落的整块（当前约 202-215 行）替换为：

````markdown
### Extension helpers

```csharp
if (entity.TryGetComponent<PositionComponent>(out var pos))
    Console.WriteLine($"({pos.RO.X}, {pos.RO.Y})");

bool existed = entity.GetOrCreateComponent<VelocityComponent>(out var vel);
if (!existed)
    vel.RW = new VelocityComponent { X = 1, Y = 1 };

entity.GetOrCreateComponent(out var health, new HealthComponent { Value = 100 });
```

`GetOrCreateComponent` returns `true` if the component already existed, `false` if it was created.

### Ref helpers

```csharp
// Direct refs; throws when the component is absent or is a tag.
ref readonly var position = ref entity.GetRO<PositionComponent>();
ref var velocity = ref entity.GetRW<VelocityComponent>();
velocity.X += 1;   // GetRW bumps the revision and can feed RevisionAsChange collectors

// Value-copy reads; the copy stays safe across structural changes.
if (entity.TryReadComponent<PositionComponent>(out var snapshot))
    Console.WriteLine($"({snapshot.X}, {snapshot.Y})");
var required = entity.RequireComponent<HealthComponent>();
var healthValue = entity.ReadComponent<HealthComponent>();

// Remove only when present.
if (entity.TryDestroyComponent<PlayerTag>())
{
}

// Entity-level matcher check; same mask + component semantics as queries and collectors.
if (EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>().IsMatch(entity))
{
}
```

`GetRO` / `GetRW` throw `InvalidOperationException` for a missing component or a tag; `TryReadComponent` returns `true` for a present tag with `default` as the value, matching `TryGetComponent`.
````

- [ ] **Step 2: 更新 QUICK_START.zh-CN.md 第 5 节**

把 `### 扩展方法` 到 `GetOrCreateComponent` 说明段落的整块（当前约 202-215 行）替换为：

````markdown
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

### 引用辅助

```csharp
// 直达引用；组件缺失或为 tag 时抛异常
ref readonly var position = ref entity.GetRO<PositionComponent>();
ref var velocity = ref entity.GetRW<VelocityComponent>();
velocity.X += 1;   // GetRW 会 bump revision，可驱动收集器的 RevisionAsChange

// 值拷贝读取；拷贝不会随结构变更失效
if (entity.TryReadComponent<PositionComponent>(out var snapshot))
    Console.WriteLine($"({snapshot.X}, {snapshot.Y})");
var required = entity.RequireComponent<HealthComponent>();
var healthValue = entity.ReadComponent<HealthComponent>();

// 仅在存在时删除
if (entity.TryDestroyComponent<PlayerTag>())
{
}

// 实体级 matcher 判定；mask 与组件语义和 query / collector 完全一致
if (EntityMatcher.With.OfAll<PositionComponent>().OfNone<HealthComponent>().IsMatch(entity))
{
}
```

`GetRO` / `GetRW` 在组件缺失或为 tag 时抛 `InvalidOperationException`；`TryReadComponent` 对存在的 tag 返回 `true` 且值为 `default`，与 `TryGetComponent` 一致。
````

- [ ] **Step 3: 修正 QUICK_START 中过期的 API 名（中英各两处 + 破坏性变更一处）**

对 `docs/QUICK_START.md` 与 `docs/QUICK_START.zh-CN.md`：

- `world.Query(matcher)` → `world.CreateQuery(matcher)`（正文说明 + 两个代码块，共 3 处）
- `structure.RO<PositionComponent>()` → `structure.GetReadOnlyDenseColumn<PositionComponent>()`
- `query.Structures[0].RW<VelocityComponent>()` → `query.Structures[0].GetReadWriteDenseColumn<VelocityComponent>()`
- 第 13 节破坏性变更条目：`use \`world.Query(matcher)\`, which returns \`IEntityQuery\`` → `use \`world.CreateQuery(matcher)\`, which returns \`IEntityQuery\``；中文同理 `改用 \`world.Query(matcher)\`` → `改用 \`world.CreateQuery(matcher)\``

- [ ] **Step 4: 修正 README 中英文的批量访问描述**

`README.md` 第 23 行：

```markdown
| **Queries** | Fluent `EntityMatcher`, non-pooled `IEntityQuery`, batch `Structure.GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()` spans |
```

`README.zh-CN.md` 第 23 行：

```markdown
| **查询** | 流式 `EntityMatcher`、非池化 `IEntityQuery`、批量 `Structure.GetReadOnlyDenseColumn<T>()` / `GetReadWriteDenseColumn<T>()` Span |
```

- [ ] **Step 5: 全量验证**

Run: `$HOME/.dotnet/dotnet build CoreECS.sln --nologo`
Expected: `0 个错误`

Run: `$HOME/.dotnet/dotnet test Test/Test.csproj --nologo -v q`
Expected: 无失败用例（`失败: 0`）。

- [ ] **Step 6: Commit**

```bash
git add docs/QUICK_START.md docs/QUICK_START.zh-CN.md README.md README.zh-CN.md
git commit -m "doc(proj): document entity helper extensions and fix stale API names"
```
