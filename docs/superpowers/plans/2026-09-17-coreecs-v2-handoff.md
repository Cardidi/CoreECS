# CoreECS v2 交接记录（Plan 1a 完成 → Plan 1b 待执行）

- 日期：2026-09-17
- 分支：`v2`（工作树为 OpenCode harness 所有，勿删除）
- 当前 HEAD：`e8ca7c8`（Plan 1a 最终硬化提交）
- 测试：`PATH="$HOME/.dotnet:$PATH" dotnet test Test/Test.csproj` → **400/400 通过**；`dotnet build ECS/ECS.csproj` 双目标（net8.0 + netstandard2.1）0 错误

## 1. 已完成：Plan 1a（archetype 内核容器）

计划与逐任务状态：`docs/superpowers/plans/2026-09-17-coreecs-v2-kernel-containers.md`（9 个任务全部完成，每个任务都经过"实现 → spec 审查 → 质量审查 → 修复 → 复审"）

实现内容（全部在 `ECS/Structures/`、`ECS/Defines/`，未改动 v1 任何文件）：

| 类型 | 可见性 | 说明 |
|---|---|---|
| `IComponent<T>` / `IDiscreteComponent<T>` / `ITagComponent<T>` | public | 三类组件接口，最派生接口判定 kind |
| `ComponentKind` / `ComponentTypeInfo` / `ComponentTypeRegistry` | internal | 全局 TypeId + Kind 注册表（线程安全、只增不减） |
| `ComponentVersion` | internal | 进程级组件实例版本号（`Interlocked`） |
| `TagContainer` | internal | Structure 内每 row tag 位图（可增长宽度、swap-remove） |
| `DiscreteStore` / `DiscreteStore<T>` / `SpareSetComponentContainer` | internal | SpareSet 存储（row 对齐、存在位图、镜像拷贝语义） |
| `StructureKey` | public | `(排序 Dense TypeId 数组, Mask)` archetype 身份 |
| `EntityLocation` | internal | 池化锚点（Structure/Row/Generation），Entity 与 ComponentRef 共享 |
| `Structure` | public | archetype：Dense SoA + row 生命周期 + tag/discrete 操作 + 迁移辅助；`IStructureObserver` internal |
| `StructureRegistry` | internal | 按 key 去重的 Structure 注册表（key 自持拷贝） |

关键语义（已由测试钉死）：
- `Append` 清理复用 row 的 Dense 槽位；`SwapRemove` 先做 row 越界护栏再变更
- `CopyDenseTo` 保留 version/revision（迁移后 ComponentRef 仍有效）；`CopyTagsTo`/`MoveDiscreteTo` 为镜像语义（target-only 会被清除）
- `Structure.RW<T>()` 获取即整结构标记 revision + observer 事件
- 迁移辅助静默（不产生事件）；Dense 增删事件由 1b 编排层发出；discrete/tag 事件由 Structure 发出

## 2. 已知非阻塞遗留（评审记录，可在 1b 顺手处理）

1. `StructureKey.DenseTypeIds` / `Structure.DenseTypeIds` 以 `IReadOnlyList<uint>` 暴露底层数组（可强转回 `uint[]`），文档声明不可变；`ToArray()` 是安全拷贝
2. `Structure.GetDiscreteVersion/Revision` 无 store 时对死 row 返回 0（有 store 时抛异常）——语义已写入文档，未统一
3. `ComponentTypeRegistry.TryGet` 仅测试覆盖一次；`ComponentTypeRegistry.RegisteredTypeCount/IdCount` 为 internal 测试钩子
4. 测试项目存在既有 NUnit 经典断言分析器警告（全仓库风格，非本次引入）；`ComponentManager.cs` 的 CS8500 为 v1 既有警告

## 3. 下一步：Plan 1b（World 集成）

范围（spec Phase 1 剩余部分）：把 `World` / `Entity` / `ComponentRef` / 三个 v1 管理器切换到新内核，删除 v1 存储并迁移内部测试。**不包括**：系统分组排序（Phase 3/4）、`IEntityQuery`（Phase 3）、World 合并与生命周期收敛（Phase 4）、CommandBuffer（Phase 5）。

设计约束（来自 spec，写 Plan 1b 时必须遵守）：

1. **EntityManager v2**：实体 id 单调分配 + `EntityLocation.Pool` 取用/归还 + `entityId → EntityLocation` 注册表；generation 由 location 池化递增；销毁实体时对其所有组件调用 `OnDestroy` 并归还 location
2. **Entity v2**：`(world, entityId, EntityLocation, generation)`；相等/哈希 `(world, entityId, generation)`；`IsValid` 校验 location 存活 + generation 匹配；`CreateComponent<T>()` / `DestroyComponent<T>()` / `GetComponent<T>()` / `HasComponent<T>()` 统一三种 kind，无 `AddTag` 变形；`GetComponent<Tag>` 永远返回 `default` 不中断；`EntityExtension`（TryGet/GetOrCreate）同步适配
3. **ComponentRef v2**：内部 `(EntityLocation, generation, typeId, kind, version)`；`RO`/`RW`/`Revision`/`NotNull` 语义不变；`RW` 触发 revision + change 事件；迁移/swap-remove 后引用自动有效；Tag 无 ref
4. **ComponentManager v2（编排层）**：持有 `StructureRegistry`；`CreateComponent<T>` = 计算目标 key → `GetOrCreate` → `Append`（若实体当前在旧结构则先复制：`CopyDenseTo` + `CopyTagsTo` + `MoveDiscreteTo`，新 Dense 类型用 `SetDenseValue` + `ComponentVersion.Next()`）→ 旧结构 `SwapRemove` → 发 got 事件 + `OnCreate`；`DestroyComponent<T>` 反向；Dense 增删事件必须由编排层发出（Structure 静默）
5. **匹配求值 v2**：`IEntityMatcher` 需按 `Structure` 组合（Dense + Mask 结构级）+ row 级（tag 位图 / discrete 存在性）求值；`IsRelevantComponent` 语义保留；collector 用户 API 不变（`Collected/Matching/Clashing/Changed` + `Flush` + flags）
6. **EntityMatchManager v2**：`_changeCollector` 改用 v2 求值；订阅/退订信号逻辑保留
7. **World v2**：本阶段仍保留 `MinimalWorld`/`World` 拆分（合并是 Phase 4）；保留现有 `Query` 重载（Phase 3 再替换为 `IEntityQuery`）；`CreateCollector` 不变
8. **删除 v1 存储**：`ComponentStore<T>`/`ComponentRefCore`/`IComponentRefLocator`/`EntityGraph` 等；内部测试 `ComponentManagerTestUnit`/`EntityGraphTestUnit`/`EntityManagerTestUnit` 重写为新内核语义；`ComponentTestUnit`/`EntityTestUnit`/`WorldTestUnit`/`EntityCollectorTestUnit`/`IntegrationTestUnit`/`StressTestUnit` 保持行为通过
9. **mask**：`CreateEntity(mask)` 选择初始结构；`SetMask` 迁移留待 CommandBuffer 阶段实现

## 4. 执行流程约定（延续 Plan 1a）

- 计划先行：新计划放 `docs/superpowers/plans/`，格式与 Plan 1a 相同（头部说明 + 文件结构表 + 逐任务 TDD 步骤 + 完整代码 + 提交命令 + Self-Review 记录）
- 每个任务：独立 subagent 实现（先写失败测试）→ spec 合规审查 subagent → 代码质量审查 subagent → 修复并复审，全绿后进入下一任务
- 评审发现问题时：先修订计划（doc 提交）再让实现者修代码（fix 提交），保持计划与代码一致
- 验证命令统一带 `PATH="$HOME/.dotnet:$PATH"`（仓库 `global.json` 固定 SDK 8）
- 提交信息遵循 Conventional Commits（见 `AGENTS.md`），scope 常用 `core` / `test` / `proj`
- 上下文不足时：更新本交接记录后在新会话继续
