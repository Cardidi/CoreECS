# CoreECS v2 交接记录（Plan 1a/1b/1c 完成 → Phase 3 待规划）

- 日期：2026-09-17
- 分支：`v2`（工作树为 OpenCode harness 所有，勿删除）
- 当前 HEAD：`2cd0372`（Plan 1c Task 3 评审修订提交）
- 测试：`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj` → **430/430 通过，0 失败**；`dotnet build ECS/ECS.csproj` 双目标（net8.0 + netstandard2.1）0 错误
- v1 引用扫描（`EntityGraph|ComponentStore|IComponentRefLocator|IComponentRefCore`）在 `ECS/` 与 `Test/` 零命中

## 1. 已完成阶段（每任务均经过"实现 → spec 审查 → 质量审查 → 修复复审"）

| 计划 | 文件 | 任务数 | 状态 |
|---|---|---|---|
| 1a 内核容器 | `docs/superpowers/plans/2026-09-17-coreecs-v2-kernel-containers.md` | 9 | ✅ |
| 1b 集成内核 | `docs/superpowers/plans/2026-09-17-coreecs-v2-world-integration-internals.md` | 5 | ✅（评审修订见计划 Self-Review 22-26 条） |
| 1c 公开 API 切换 | `docs/superpowers/plans/2026-09-17-coreecs-v2-public-api-swap.md` | 3 | ✅（评审修订见计划 Self-Review 32-34 条） |

### 1b（5 个任务）落地内容

`EntityTable`（id 单调分配 + location 池 + `EntityIds`/`Clear`）、`ComponentRefCore`（无类型引用核心 + `Structure` 6 个非泛型访问器）、`ComponentHookDispatcher`（静态持有类缓存 hook 委托 + v1 语义 catch/log）、`ComponentOrchestrator`（实体生命周期、discrete/tag 增删、dense 迁移 `AddDenseComponent`/`RemoveDenseComponentCore`/`RemoveDiscreteComponentCore`/`RemoveComponent`、`m_destroying`/`m_mutating` 重入守卫、行重读、observer 事件）、`EntityMatcher.ComponentFilter(Structure, int)`（配置时按 kind 解析 typeId + mask/none/all/any 求值）。

关键评审修订（均已修复并复审）：
1. Task 2：generation 失效分支隔离测试；Dense `NotNull` 增加行存活护栏（`row < structure.Count`）
2. Task 3：`DestroyEntity` 重入安全（同实体 no-op 守卫、discrete 存储快照、行重读、finally 释放）；hook 异常 catch+`Log.Exp`；委托缓存；7 个回归测试
3. Task 4：`RemoveDenseComponent`/`RemoveDiscreteComponent` 的 hook 期间 `m_mutating` 实体级互斥（hook 内销毁/递归移除/迁移实体被拒绝），`RequireLocation`/`DestroyEntity` 拒绝 busy 实体；4 个回归测试
4. Task 5：注册表不变量回归测试（使用测试独占类型，避免进程级注册表掩盖）

### 1c（3 个任务）落地内容

- Task 1：公开 `Entity`/`ComponentRef`/`ComponentRef<T>`/`EntityExtension` 切 v2（结构相等、Tag 返回 default、失效实体抛异常、`GetComponents` 排除 tag）；discrete 调用链约束放宽（`DiscreteStore<T>`/`GetOrCreateStore`/`SetDiscrete`/`GetDiscreteRef`/`DiscreteHooks<T>`/`RegisterDiscrete`）；非泛型 `RemoveComponent` 复用守卫核心
- Task 2：`ComponentManager`（内核持有者 + observer 桥）、`EntityManager`（EntityTable + `(ulong, Type)` 信号 + 销毁补发 null lose 事件 + 重入守卫）、`EntityMatchManager`（结构求值接线）、`World`（实体/Query 接线）；删除 `EntityGraph`/`ComponentStore`/v1 接口/v1 matcher 重载
- Task 3：内部测试迁移（`ComponentManagerTestUnit` 10、`EntityManagerTestUnit` 14、`EntityExtensionTestUnit` 9、`EntityCollectorTestUnit` +1、`EntityGraphTestUnit` 删除、5 个行为文件适配、`EntityTable.Clear` + shutdown 释放）

## 2. 待办：按 spec「分阶段交付」表继续

| 阶段 | 内容 | 状态 |
|---|---|---|
| 3 | 查询：`IEntityQuery` + `s.RO/RW<T>()` + collector 结构级加速 | 📋 待规划/执行 |
| 4 | 调度：`RegisterGroup` / `Before` / `After` / 拓扑排序 | 待办 |
| 5 | World 合并与生命周期收敛 | 待办 |
| 6 | CommandBuffer + 文档：README / QUICK_START 中英文更新 | 待办（按用户指定的执行顺序：CommandBuffer 与文档同属 Phase 6；spec 表第 2 行的 CommandBuffer 位置以此为准） |

### Phase 3 范围（spec 5.2/5.3/5.4）

1. `Structure.RO<T>()` / `RW<T>()` 批量访问：`ReadOnlySpan<T>` / `Span<T>`，与行对齐；`RW` 获取即整结构标记该类型全部 row revision + change 事件；`s.Entities` 已存在（`ReadOnlySpan<ulong>`）。**实勘：`RO/RW` 已在 Plan 1a Task 7 落地（`Structure.cs:192-219`，commit `0d75744`）且与 spec 5.3 一致；Phase 3 Task 1 因此是契约测试补齐（生产代码预期零改动）**
2. `IEntityQuery`（不池化，`IDisposable`）：`Matcher` / `IReadOnlyList<Structure> Structures` / `IEnumerable<ulong> Entities` / `Refresh()`；`world.Query(matcher)` 创建；**删除** v1 `world.Query(matcher, ICollection<...>)` 重载并迁移调用点（`EntityMatcherTestUnit`、`WorldTestUnit` 等）
3. collector 结构级加速：`EntityMatchManager` 的 `_changeCollector` 利用结构级粗筛（Dense+Mask）缓存，行级只查 tag/discrete；用户 API 不变

### Phase 3 计划编写约定

- 计划文件命名：`docs/superpowers/plans/2026-09-17-coreecs-v2-phase3-query.md`（或等价）
- 格式与 1a/1b/1c 一致（头部说明 + File Structure 表 + 逐任务 TDD 步骤 + 完整代码 + 提交命令 + Self-Review）
- **计划编写子代理单次只写 1-2 个任务**（`write` 建文件、`edit` 追加），否则输出量过大可能静默失败
- 建议任务拆分：Task 1 批量访问（Structure RO/RW + 测试）→ Task 2 `IEntityQuery` + `World.Query(matcher)` + 删除 v1 重载 + 迁移现有测试 → Task 3 collector 结构级加速 + 测试

## 3. 已记录的已知非阻塞项（可选加固，不阻塞）

1. `StructureKey.DenseTypeIds` / `Structure.DenseTypeIds` 以 `IReadOnlyList<uint>` 暴露底层数组（文档声明不可变）
2. `Structure.GetDiscreteVersion/Revision` 无 store 时对死 row 返回 0（有 store 时抛异常）——语义已文档化
3. `ResolvedSet` 不去重；`ComponentKind` switch 无 `default` 分支
4. `ComponentOrchestrator.RemoveComponent` 无 `default: throw`；XML 文档未覆盖 `RequireLocation` 异常
5. `EntityManager.m_destroying` 在 `OnManagerDestroyed` 未 `Clear()`（无害）
6. 测试中 `Type capturedType = null` 在 `Nullable enable` 下产生 CS8600 警告（既有风格）
7. `EntityTable.Clear()` 不清理 archetype 行/store（shutdown 后不可重启，无影响）
8. hook 抛异常时 `DestroyEntity` 不补发 lose 事件（v1 同样行为，选择性加固）

## 4. 执行流程约定（延续 1a/1b/1c）

- 每个任务：实现 subagent（TDD：先失败测试 → 实现 → 全量绿 → 提交）→ spec 审查 subagent（独立读代码验证）→ 质量审查 subagent（跑测试 + 明确结论）→ Critical/Important 先改计划（doc 提交）再由同一实现者修复（fix 提交）→ 复审直至批准
- 验证命令统一带 `PATH="$HOME/.dotnet:$PATH"`（`global.json` 固定 SDK 8）
- 提交信息遵循 Conventional Commits（见 `AGENTS.md`），scope 常用 `core` / `test` / `proj`
- 审查子代理无法指定模型时，必须独立执行并给出实测证据（命令输出/探针）
- 上下文不足时：更新本交接记录并提交，再交接给新会话
