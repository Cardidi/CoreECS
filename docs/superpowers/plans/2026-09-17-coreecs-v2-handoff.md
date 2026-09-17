# CoreECS v2 交接记录（Phase 1-5 完成 → Phase 6 CommandBuffer + 文档 待规划）

- 日期：2026-09-17
- 分支：`v2`（工作树为 OpenCode harness 所有，勿删除）
- 当前 HEAD：`b42ab7a`（Phase 5 Task 2 评审修订提交）
- 测试：`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj` → **513/513 通过，0 失败**；`dotnet build ECS/ECS.csproj` 双目标（net8.0 + netstandard2.1）0 错误
- v1 引用扫描（`EntityGraph|ComponentStore|IComponentRefLocator|IComponentRefCore`）在 `ECS/` 与 `Test/` 零命中

## 1. 已完成阶段（每任务均经过"实现 → spec 审查 → 质量审查 → 修复复审"）

| 计划 | 文件 | 任务数 | 状态 |
|---|---|---|---|
| 1a 内核容器 | `docs/superpowers/plans/2026-09-17-coreecs-v2-kernel-containers.md` | 9 | ✅ |
| 1b 集成内核 | `docs/superpowers/plans/2026-09-17-coreecs-v2-world-integration-internals.md` | 5 | ✅（评审修订见计划 Self-Review 22-26 条） |
| 1c 公开 API 切换 | `docs/superpowers/plans/2026-09-17-coreecs-v2-public-api-swap.md` | 3 | ✅（评审修订见计划 Self-Review 32-34 条） |
| Phase 3 查询 | `docs/superpowers/plans/2026-09-17-coreecs-v2-phase3-query.md` | 3 | ✅（评审修订见计划 Self-Review 10/15 条） |
| Phase 4 调度 | `docs/superpowers/plans/2026-09-17-coreecs-v2-phase4-scheduling.md` | 3 | ✅（评审修订见计划 Self-Review 8/16/17/25/26 条） |
| Phase 5 World | `docs/superpowers/plans/2026-09-17-coreecs-v2-phase5-world.md` | 2 | ✅（评审修订见计划 Self-Review 8/17 条） |

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

### Phase 3（3 个任务）落地内容

- Task 1（契约测试，生产代码零改动）：`StructureBatchAccessTestUnit` 8 个测试钉死 `RO<T>()` / `RW<T>()`（Plan 1a Task 7 已实现）——行对齐、容量不外露、`RW` 获取即整结构逐行标记、非 Dense/缺失抛异常、RO 无副作用
- Task 2：`IEntityQuery`（`ECS/Defines/IEntityQuery.cs`）+ internal `EntityQuery` + `World.Query(matcher)`（纯工厂、不自动 Refresh）；删除 v1 `World.Query(matcher, ICollection<...>)` 两个重载；`EntityMatcherExtension` 保留签名迁移实现体；`EntityQueryTestUnit` 11 + `WorldTestUnit` 24（迁移 + 扩展追加覆盖）
- Task 3：collector 结构级加速——`ComponentFilter` 拆为 `EvaluateStructure`（mask + dense，返回 `StructureMatch{Passes,AnySatisfied}`）与 `RowFilter`（tag/discrete）；每 collector `Dictionary<Structure, StructureMatch>` 缓存（结构组成不可变，无需失效；matcher 须在创建 collector 前配置完毕，已写入文档）；第三方 `IEntityMatcher` 走未缓存回退；`CollectorAccelerationTestUnit` 9 个测试

### Phase 4（3 个任务）落地内容

- Task 1：`GroupInsertMode` + `SystemSchedule` 组树 + `RegisterGroup`（嵌套）/ `RegisterSystem`（可选组）返回 `GroupRegistration` / `SystemRegistration` 句柄（`Before/After` 锚点：系统类型或组名、跨层级、允许前向引用）；未知组/重复组名抛异常；注销/清理/关停同步树节点（组保留）
- Task 2：`SystemSchedule.BuildExecutionOrder()`——DFS 展平（子序 = 注册序 + Early/Later）、锚点解析（组目标 = 整个子树；组自锚/祖先锚 no-op；系统锚到本组只约束其他成员）、稳定拓扑排序（flatten 序最小者优先）、无法解析记 `Log.Err` 并忽略、成环 `Log.Err` + 全量回退展平序；`TeardownSystems` 重建 `m_systems` 顺序且复用实例
- Task 3：tick 内收敛——`m_cancelledAdds` 标记、注销仅排队系统 = 取消待添加、tick 内注销 + 重注册 = 复用实例并重定位（保留锚点、缺节点时重建）、`ExecuteSystems` 序列快照（当前 tick 不受注册图变更影响）、锚点句柄 shutdown 守卫、`OnWorldEnded` 清理标记

### Phase 5（2 个任务）落地内容

- Task 1：`MinimalWorld` 合并进 `World`（`public class World : IWorld`，核心 manager 内置且不可被子类覆盖丢失，`OnRegister` 保留为自定义 manager 扩展点）；`MinimalWorld.cs` 删除；`WorldTestUnit` 3 处机械适配 + `WorldMergeTestUnit` 3 个契约测试；QUICK_START 钩子名修正
- Task 2：生命周期钩子收敛为 `OnRegister(IManagerRegister, IServiceCollection)`（仅首次 `Startup`，吸收 `RegisterServices`）、`OnSetup()`（每次 `Startup`，吸收 `OnConstruct`/`OnFirstStart`/`OnStart`）、`OnCleanup()`（每次 `Shutdown`，吸收 `OnShutdown`）；删除 `OnTickBegin`/`OnTick`/`OnTickEnd` 虚钩子，`BeginTick`/`Tick(mask)`/`EndTick` 内部直接驱动 `SystemManager`（断言、`TickCount`、`Ticking`、`Log.Exp` 保留）；`RegisterRequiredServices` 改 `TryAdd*` 保证用户 DI 注册优先；测试迁移 + 2 个新契约测试 + 文档同步

## 2. 待办：Phase 6（CommandBuffer + 文档，spec 第 8 节与交付表第 2/6 行）

| 阶段 | 内容 | 状态 |
|---|---|---|
| 6 | CommandBuffer（spec 第 8 节）+ 文档：README / QUICK_START 中英文更新 | 📋 待规划/执行 |

### Phase 6 范围（spec 第 8 节 + 交付表）

1. `world.CreateCommandBuffer()` → `using var cmd`；手动 `Playback()`；未 Playback 直接 `Dispose` = 丢弃记录并释放资源
2. API：`cmd.CreateEntity(mask)`（返回 CommandBuffer 内部占位实体，Playback 时解析为真实实体）、`cmd.CreateComponent<T>(e[, value])`（三种 kind）、`cmd.DestroyComponent<T>(e)`、`cmd.SetMask(e, mask)`（1c 遗留的 `SetMask` 迁移在本阶段实现）、`cmd.DestroyEntity(e)`
3. 记录期间零结构迁移；`Playback()` 按记录顺序立即批量应用；Playback 后可复用（清空记录）
4. 用于大量数据变更场景
5. 文档：README / QUICK_START 中英文按 v2 行为更新（含 CommandBuffer、`IEntityQuery`、`s.RO/RW<T>()`、组调度、World 生命周期钩子、破坏性变更清单），文档评审通过
6. 实勘要点：占位实体如何在 Playback 前被 `DestroyComponent`/`SetMask` 引用（记录顺序 + 延迟解析）；`SetMask` 的迁移语义（含 dense 增删、tag/discrete 保留、mask 参与结构 key）；CommandBuffer 与 tick 的关系（Playback 可在 tick 中调用？）；`Entity` 句柄与占位实体的表示；现有 `Entity`/`World` API 的可复用性

### Phase 6 计划编写约定

- 计划文件命名：`docs/superpowers/plans/2026-09-17-coreecs-v2-phase6-commandbuffer.md`（或等价）
- 格式与既有计划一致；**计划编写子代理单次只写 1-2 个任务**（`write` 建文件、`edit` 追加）
- 建议任务拆分（写计划时按实勘调整）：Task 1 CommandBuffer 记录层（占位实体 + 记录结构 + `Dispose` 丢弃）→ Task 2 `Playback` 批量应用（按序应用、`SetMask` 迁移、Playback 后复用）→ Task 3 文档更新（README / QUICK_START 中英文）

## 3. 已记录的已知非阻塞项（可选加固，不阻塞）

1. `StructureKey.DenseTypeIds` / `Structure.DenseTypeIds` 以 `IReadOnlyList<uint>` 暴露底层数组（文档声明不可变）
2. `Structure.GetDiscreteVersion/Revision` 无 store 时对死 row 返回 0（有 store 时抛异常）——语义已文档化
3. `ResolvedSet` 不去重；`ComponentKind` switch 无 `default` 分支
4. `ComponentOrchestrator.RemoveComponent` 无 `default: throw`；XML 文档未覆盖 `RequireLocation` 异常
5. `EntityManager.m_destroying` 在 `OnManagerDestroyed` 未 `Clear()`（无害）
6. 测试中 `Type capturedType = null` 在 `Nullable enable` 下产生 CS8600 警告（既有风格）
7. `EntityTable.Clear()` 不清理 archetype 行/store（shutdown 后不可重启，无影响）
8. hook 抛异常时 `DestroyEntity` 不补发 lose 事件（v1 同样行为，选择性加固）
9. Phase 3 遗留（均已记录、不阻塞）：`EntityQuery` 仍按行调用 `ComponentFilter`，查询侧结构级缓存留待需要时评审；`ResolvedSet` 可暴露预计算 `HasRowConditions`（当前内联 `Tags.Count == 0 && Discretes.Count == 0`）；collector 的 `StructureMatches` 随结构数无界增长（结构只增不减，设计接受）；`EntityMatcher.StructureEvaluationCount` 钩子也计入查询路径的求值（当前无混用测试）
10. Phase 4 遗留（均已记录、不阻塞）：`OnWorldEnded` 的 `m_cancelledAdds.Clear()` 无独立测试（不可观测的防御清理）；"标记 vs 出队下溢"理由无直接测试（`OnCreate` 中注销后续排队系统的场景，已手工验证）；`CleanupSystems` 在 tick 中直接调用时的快照行为无测试（`TeardownSystems` 变体已测）；`RegisterSystem` 可变更分支在 `_instantSystem` 之前加取消标记——若 DI 构造抛异常，排队项会被静默丢弃（基线会重试），属异常路径低危；`ExecuteSystems` 每 tick `ToArray()` 分配（计划已接受）；计划 Task 3 Step 2 的合并红灯声明跨两个修订版本（9/7 在任何单一版本都不可复现，per-fix 红灯证据准确）
11. Phase 5 遗留（均已记录、不阻塞）：`Startup` 部分失败重试边界——若用户工厂/`RegisterRequiredServices` 在 `m_init = true` 之前抛异常，重试 `Startup()` 会跳过 `OnRegister`/`RegisterRequiredServices`（`firstStart == false`）但仍运行 `OnSetup`（可能 `InjectionProxy` 为 null）；计划明确冻结状态机未处理，留待需要时评审。`OnRegister` 从单参改为双参是 toolkit 子类的破坏性变更（spec §7 预期，文档已同步）。Phase 5 计划 Task 3 只排查 `MinimalWorld` 文档引用（已无），`OnRegisterManager` 文档引用已由 Task 1 修订提交修正

## 4. 执行流程约定（延续 1a/1b/1c）

- 每个任务：实现 subagent（TDD：先失败测试 → 实现 → 全量绿 → 提交）→ spec 审查 subagent（独立读代码验证）→ 质量审查 subagent（跑测试 + 明确结论）→ Critical/Important 先改计划（doc 提交）再由同一实现者修复（fix 提交）→ 复审直至批准
- 验证命令统一带 `PATH="$HOME/.dotnet:$PATH"`（`global.json` 固定 SDK 8）
- 提交信息遵循 Conventional Commits（见 `AGENTS.md`），scope 常用 `core` / `test` / `proj`
- 审查子代理无法指定模型时，必须独立执行并给出实测证据（命令输出/探针）
- 上下文不足时：更新本交接记录并提交，再交接给新会话
