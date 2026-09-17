# CoreECS v2 RW/RO 改造前性能基线（Task 0）

**采集日期：** 2026-09-18
**代码版本：** `v2` @ `3f51bab`（`doc(proj): add rw/ro performance design, plan and analyzer prompt`）
**对应计划：** `docs/superpowers/plans/2026-09-18-rw-ro-performance.md` Task 0

## 环境

| 项 | 值 |
|---|---|
| 机器 | MacBook Pro（Apple M3 Max，arm64） |
| 内存 | 36 GB（38654705664 bytes） |
| 操作系统 | macOS 26.6.2 (25G83) |
| .NET SDK | 8.0.425（`~/.dotnet/dotnet`） |
| 构建配置 | Debug（`net8.0`） |
| 测试框架 | NUnit 3.14.0 + NUnit3TestAdapter 4.5.0 |

## 采集方法

- 命令：`~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~PerformanceBaselineTestUnit" --verbosity normal`
- 每个场景取 **best-of-5**（1 次预热 + 4 轮计时，取最小值），单位为毫秒。
- `RO`/`RW` 场景：单实体、单 dense `Position` 组件，循环 200,000 次访问；collector 数量为 0/100/1000，全部使用 `EntityCollectorFlag.RevisionAsChange`。
- `F1`：1000 collectors，1 个实体，写入 200,000 次后测量 `Flush()` 总耗时。
- `F2`：1000 collectors，1000 个实体各写 1 次后测量 `Flush()` 总耗时。
- `F3`：1000 collectors，写入 200,000 次 + 紧随其后的 `Flush()` 总耗时（端到端管线）。

## 基线数值

| 场景 | collectors | 指标 | 数值 |
|---|---|---|---|
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 0 | RO / RW / ratio | 12.222ms / 29.795ms / **2.438x** |
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 100 | RO / RW / ratio | 12.500ms / 1403.016ms / **112.239x** |
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 1000 | RO / RW / ratio | 12.393ms / 13072.045ms / **1054.825x** |
| `Baseline_FlushSettlement` F1 | 1000 | flush（1 entity，200,000 writes） | **0.127ms** |
| `Baseline_FlushSettlement` F2 | 1000 | flush（1000 entities） | **0.987ms** |
| `Baseline_PipelineWritesPlusFlush` F3 | 1000 | write + flush 总计（200,000 writes） | **12463.047ms** |

**回填常量（`Test/PerformanceBaselineTestUnit.cs`）：** `F1Ms = 0.127`、`F2Ms = 0.987`、`F3Ms = 12463.047`。

### 观察

- `RO` 与 collector 数量无关（约 12.2–12.5ms），因为只读访问不触发 revision 变更信号。
- `RW` 随 collector 数量线性放大：1000 collectors 时单次写入约 65µs，是非缓存路径的主要瓶颈；改造目标为 0/100/1000 档 ratio < 1.2/1.5/2.0（见 Task 9）。
- `Flush` 本身很便宜（0.127–0.987ms），因为当前实现是写入时同步结算，`Flush` 只做双缓冲发布；F3 的耗时几乎全部来自写入阶段（12463ms ≈ RW 的 13072ms 同量级）。
- **Task 9 风险（0 collector 档）**：基线 ratio 2.438x，RW−RO ≈ 17.2ms / 200k ≈ **86ns/次**的 RW 专属开销（revision bump + 空 observer/sink 链）。要达到 < 1.2x，必须把这份开销压到 RO 的 ~20% 以内（约 <17ns）；Task 4（slot 快路径）与 Task 5（兴趣短路）是达标关键，若 Task 9 失败优先检查这两项。
- **口径说明**：`MeasureAccess` 只计时访问循环，循环后的 `FlushAll` 不计入 ratio；改造后结算移入 Flush，因此 ratio 只反映写路径成本。端到端不劣化由 F3 断言保证。

## 改造后数值（Task 9，2026-09-18）

**代码版本：** `v2` @ `a2087c6`（Task 1–8 优化 + Task 9 后备与追加优化）
**采集命令：** `~/.dotnet/dotnet test Test/Test.csproj --filter "FullyQualifiedName~Baseline_NonCachedRoVsRw" --verbosity normal`

| 场景 | collectors | RO | RW | ratio | 目标 | 结果 |
|---|---|---|---|---|---|---|
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 0 | 8.045ms | 7.337ms | **0.912x** | < 1.2 | PASS |
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 100 | 8.101ms | 8.389ms | **1.035x** | < 1.5 | PASS |
| `Baseline_NonCachedRoVsRw_ByCollectorCount` | 1000 | 7.976ms | 8.010ms | **1.004x** | < 2.0 | PASS |

- RW 单次开销已降到与 RO 同一量级；0 collector 档 RW 略快于 RO，因为 RW 快路径把「校验 + revision bump」融合为一次调用，而 RO 仍走 `RequireStructure` → `NotNull` → `Core.NotNull` 的链式校验（Debug 下每次访问都真实发生）。
- 与基线相比：0 档 2.438x → 0.912x；100 档 112.239x → 1.035x；1000 档 1054.825x → 1.004x。

### Task 9 追加的写路径优化（按提交顺序）

| 提交 | 内容 |
|---|---|
| `ca00721` | 后备 1：`IComponentChangeSink` 携带 `EntityLocation`，去掉写路径的 `Table.TryGetLocation` 字典查表 |
| `590a2f7` | 后备 2：`RevisionEntry` 延迟解析组件 `Type`，仅在存在 relevance-gated revision collector 时按条目解析 |
| `8391488` | 后备 3：`ChangeSink` 由接口改为 World 装配的直接委托（删除 `IComponentChangeSink`） |
| `e0f8e2f` | 兴趣短路：`Signal` 增加接收者变化回调；`ComponentManager`/`EntityManager`/`EntityMatchManager` 维护缓存兴趣标志并下发到每个 `Structure`；无监听时写路径完全跳过 observer 链；有公开监听者时通知后重新解析 live 结构 |
| `a2087c6` | 融合快路径：`ComponentRefCore.TryBumpDenseRevision`/`TryBumpSparseRevision` 一次完成校验与 bump；`ComponentRef<T>.RW` 对同一 (entity, type) 的 pending journal 条目跳过重复通知；journal floor 上升/清空时由 `EntityTable.InvalidatePendingRevisions` 失效 pending 标记 |

> 后备 1/2/3 单独均不足以达标（0 collector 档的固定开销在 Debug 下由三次 `NotNull` 与空转通知链主导），因此追加了兴趣短路与融合快路径。collector 硬门禁（86 例）与全量套件均零改动通过。

## 完整测试输出

```text
生成启动时间为 2026/9/18 01:37:12。
     1>项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”在节点 1 上(Restore 个目标)。
     1>_GetAllRestoreProjectPathItems:
         正在确定要还原的项目…
       Restore:
         X.509 证书链验证将使用 "/Users/cardidi/.dotnet/sdk/8.0.425/trustedroots/codesignctl.pem" 处的回退证书捆绑包。
         X.509 证书链验证将使用 "/Users/cardidi/.dotnet/sdk/8.0.425/trustedroots/timestampctl.pem" 处的回退证书捆绑包。
         资产文件未改变。跳过资产文件写入。路径: /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/obj/project.assets.json
         资产文件未改变。跳过资产文件写入。路径: /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Kernel/obj/project.assets.json
         已还原 /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj (用时 16 毫秒)。
         已还原 /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Kernel/Kernel.csproj (用时 16 毫秒)。
         
         使用的 NuGet 配置文件:
             /Users/cardidi/.nuget/NuGet/NuGet.Config
         
         使用的源:
             https://api.nuget.org/v3/index.json
         所有项目均是最新的，无法还原。
     1>已完成生成项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(Restore 个目标)的操作。
   1:7>项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”在节点 1 上(VSTest 个目标)。
     1>BuildProject:
         已开始生成，请等待...
   1:7>项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(1:7)正在节点 1 上生成“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(1:8) (默认目标)。
   1:8>项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(1:8)正在节点 1 上生成“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Kernel/Kernel.csproj”(2:16) (默认目标)。
     2>GenerateTargetFrameworkMonikerAttribute:
       正在跳过目标“GenerateTargetFrameworkMonikerAttribute”，因为所有输出文件相对于输入文件而言都是最新的。
       CoreGenerateAssemblyInfo:
       正在跳过目标“CoreGenerateAssemblyInfo”，因为所有输出文件相对于输入文件而言都是最新的。
       _GenerateSourceLinkFile:
         Source Link 文件 "obj/Debug/net8.0/Kernel.sourcelink.json" 是最新的。
       CoreCompile:
       正在跳过目标“CoreCompile”，因为所有输出文件相对于输入文件而言都是最新的。
       GenerateBuildDependencyFile:
       正在跳过目标“GenerateBuildDependencyFile”，因为所有输出文件相对于输入文件而言都是最新的。
       CopyFilesToOutputDirectory:
         Kernel -> /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Kernel/bin/Debug/net8.0/CoreECS.dll
     2>已完成生成项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Kernel/Kernel.csproj”(默认目标)的操作。
     1>GenerateTargetFrameworkMonikerAttribute:
       正在跳过目标“GenerateTargetFrameworkMonikerAttribute”，因为所有输出文件相对于输入文件而言都是最新的。
       CoreGenerateAssemblyInfo:
       正在跳过目标“CoreGenerateAssemblyInfo”，因为所有输出文件相对于输入文件而言都是最新的。
       _GenerateSourceLinkFile:
         Source Link 文件 "obj/Debug/net8.0/Test.sourcelink.json" 是最新的。
       CoreCompile:
       正在跳过目标“CoreCompile”，因为所有输出文件相对于输入文件而言都是最新的。
       _CopyOutOfDateSourceItemsToOutputDirectory:
       正在跳过目标“_CopyOutOfDateSourceItemsToOutputDirectory”，因为所有输出文件相对于输入文件而言都是最新的。
       GenerateBuildDependencyFile:
       正在跳过目标“GenerateBuildDependencyFile”，因为所有输出文件相对于输入文件而言都是最新的。
       GenerateBuildRuntimeConfigurationFiles:
       正在跳过目标“GenerateBuildRuntimeConfigurationFiles”，因为所有输出文件相对于输入文件而言都是最新的。
       CopyFilesToOutputDirectory:
         Test -> /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/bin/Debug/net8.0/Test.dll
     1>已完成生成项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(默认目标)的操作。
     1>BuildProject:
         完成的生成。
         
/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/bin/Debug/net8.0/Test.dll (.NETCoreApp,Version=v8.0)的测试运行
VSTest 版本 17.11.1 (arm64)

正在启动测试执行，请稍候...
总共 1 个测试文件与指定模式相匹配。
NUnit Adapter 4.5.0.0: Test execution started
Running selected tests in /Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/bin/Debug/net8.0/Test.dll
   NUnit3TestExecutor discovered 3 of 3 NUnit test cases using Current Discovery mode, Non-Explicit run
[baseline] F1 collectors=1000 entities=1 writes=200000 flush=0.127ms
[baseline] F2 collectors=1000 entities=1000 flush=0.987ms

  已通过 Baseline_FlushSettlement [1 m 7 s]
[baseline] collectors=0 RO=12.222ms RW=29.795ms ratio=2.438x
[baseline] collectors=100 RO=12.500ms RW=1403.016ms ratio=112.239x
[baseline] collectors=1000 RO=12.393ms RW=13072.045ms ratio=1054.825x

  已通过 Baseline_NonCachedRoVsRw_ByCollectorCount [1 m 14 s]
[baseline] F3 collectors=1000 writes=200000 total=12463.047ms

NUnit Adapter 4.5.0.0: Test execution complete
  已通过 Baseline_PipelineWritesPlusFlush [1 m 5 s]

测试运行成功。
测试总数: 3
     通过数: 3
总时间: 3.4718 分钟
     1>已完成生成项目“/Users/cardidi/.local/share/opencode/worktree/8f7a9ba8c7db6e082f756cf4376a7fb596d09af6/brave-otter/Test/Test.csproj”(VSTest 个目标)的操作。

已成功生成。
    0 个警告
    0 个错误

已用时间 00:03:29.77
```
