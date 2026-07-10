# OmniKey Vault — v1.1 优化执行计划

| 文档版本 | 日期 | 作者 | 状态 |
|---|---|---|---|
| 1.1 | 2026-07-07 | Sisyphus | Phase 1-2 已完成,Phase 3 部分完成(OKV0001 + OKV0003);更新全部 Phase 状态 |
| 1.0 | 2026-07-03 | Sisyphus | 初稿:基于 v1.0 RC 代码审计结果制定,12 阶段任务分解,含验收标准与 PR 拆分 |

> 关联文档:[ROADMAP.md](./ROADMAP.md) · [ARCHITECTURE.md](./ARCHITECTURE.md) · [SECURITY.md](./SECURITY.md) · [INTERNAL.md](./INTERNAL.md) · [TEST_REPORT.md](./TEST_REPORT.md) · [BUILD.md](./BUILD.md) · [MANUAL.md](./MANUAL.md) · [OKV_FORMAT.md](./OKV_FORMAT.md)
>
> 本文档基于 2026-07-03 的代码审计结果(3 个并行 explore 代理对 Application/Infrastructure、Cli (GUI+CLI)、Tests/Benchmark 三大模块的审计产出),对 v1.0 RC 现有功能的优化方案做 12 阶段任务分解。**不含新功能**(跨平台、新平台模板、Web 端、团队共享、后量子加密等留给 v1.x)。
>
> 阅读路径:§1 概述 → §2 决策汇总 → §3-§14 Phase 1-12 详细分解 → §15 工作量与时间线 → §16 风险与缓解 → §17 完成后预期状态。

---

## 1. 概述

### 1.1 文档目的

将 v1.0 RC 代码审计发现的问题转化为可执行的 Sprint 任务,服务于:

- **Tech Lead**:跨 Phase 依赖统筹、PR review 优先级判断。
- **后端 / 密码学工程师**:Phase 3-7 + Phase 9-10 的实现。
- **UI 工程师**:Phase 8 GUI MVVM 全量重构。
- **QA 工程师**:Phase 10 测试补全 + 每个 Phase 的验收清单核对。

### 1.2 范围

| 包含 | 不包含 |
|---|---|
| 现有功能的 bug 修复 | 新平台模板(v1.0 远期 +15) |
| 架构重构(拆 God Class、引入 MVVM) | 跨平台 macOS / Linux 客户端 |
| 安全加固(密码学不变量、Roslyn 分析器) | Web 端、浏览器扩展 |
| 测试补全(文档承诺的 invariant) | 团队共享、企业 RBAC |
| 文档对齐(代码与文档一致) | 后量子加密升级 (OKV2) |
| DevOps 补全(.gitignore / CI / 中央包管理) | 新 KDF 算法 |

### 1.3 时间盒与实际状态

| Phase | 时长 | 目标完成日期 | 依赖前置 | 状态 |
|---|---|---|---|---|
| Phase 1 紧急止血 | 0.5d | 2026-07-04 | — | ✅ 已完成 |
| Phase 2 安全合规 | 2d | 2026-07-08 | Phase 1 | ✅ 已完成 |
| Phase 3 Roslyn 分析器 | 5d | 2026-07-15 | Phase 2 | 🟡 部分完成(OKV0001 + OKV0003 已落地;OKV0002 + OKV0004 延至 v1.2) |
| Phase 4 正确性修复(小) | 5d | 2026-07-22 | Phase 3 | ⏸ 待开始 |
| Phase 5 文档对齐功能 | 3d | 2026-07-25 | Phase 4 | ⏸ 待开始 |
| Phase 6 Field.Value → byte[] | 10d | 2026-08-08 | Phase 3 + Phase 5 | ⏸ 待开始 |
| Phase 7 架构拆分 | 5d | 2026-08-15 | Phase 6 | ⏸ 待开始 |
| Phase 8 GUI MVVM 全量重构 | 10d | 2026-08-29 | Phase 7 | ⏸ 待开始 |
| Phase 9 SearchService 索引 | 3d | 2026-09-01 | Phase 8 | ⏸ 待开始 |
| Phase 10 测试补全 | 4d | 2026-09-05 | Phase 9 | ⏸ 待开始 |
| Phase 11 重复消除 + 配置 | 3d | 2026-09-08 | Phase 10 | ⏸ 待开始 |
| Phase 12 P3 清理 + Recovery Key base32 | 3d | 2026-09-11 | Phase 11 | ⏸ 待开始 |

**总计**:53.5 人天(约 11 周一人,或 5-6 周两人并行)。目标公开发布前全部完成,作为 v1.1 候选。

### 1.4 团队配置建议

| 角色 | 人数 | 主要 Phase |
|---|---|---|
| Tech Lead / 架构师 | 1 | 全程 review + Phase 3 Roslyn 分析器设计 |
| 后端 / 密码学工程师 | 1 | Phase 2, 3, 5, 6, 9, 10 |
| 全栈工程师 | 1 | Phase 1, 4, 7, 11, 12 |
| UI 工程师 | 1 | Phase 8(全职 2 周)+ Phase 9 GUI debounce |
| QA 工程师 | 1(50%) | Phase 10 + 各 Phase 验收清单核对 |

最小配置:Tech Lead + 1 后端 + 1 全栈 + 0.5 QA = 3.5 人。

### 1.5 文档版本 vs 代码版本

| 文档集版本 | 对应代码版本 | 状态 |
|---|---|---|
| 1.0 | v1.0 RC | 451/451(文档声称) / 430/430(trx 实际)— 偏差待 Phase 2 修复 |
| **1.1(当前)** | **v1.1.0-alpha** | **467/467(457 + 10 分析器);Phase 1-2 已完成,Phase 3 部分完成;剩余 Phase 4-12 待开始** |

---

## 2. 关键决策(已与用户确认)

| 决策点 | 选择 | 工作量 | 影响 |
|---|---|---|---|
| **Roslyn 分析器(OKV0001-0004)** | 实现分析器项目 | +5d | 从根本上拦截 string 密码、未 using SecureKey、`==` 比较密钥等违规;后续 Phase 6 Field.Value 改造时可即时发现遗漏点 |
| **Field.Value 类型** | 改 byte[] + 显式清零 | +10d | 彻底解决 P1-4 锁定不清零(INV-03 违反);影响 ~50 个文件;UI 绑定需 UTF-8 编解码辅助 |
| **GUI MVVM 重构** | 全量重构 14 个 view | +10d | 引入 CommunityToolkit.Mvvm;解锁 Avalonia headless 自动化测试;MainWindow.axaml.cs 从 1590 行降到 ~400 |
| **文档对齐功能** | BackupService 磁盘版 + Watcher 接入 | +3d | 与 TEST_REPORT.md §2.5.2 / ROADMAP.md §4.3.1 已交付声明对齐;OKV_FORMAT.md §10 OKVS magic 落地 |

**替代方案(未选)**:
- Roslyn 分析器:撤回 SECURITY.md §10.1 承诺(0 工作量但降低安全可信度)— 不选
- Field.Value:保持 string 接受 INV-03 部分违反(0 工作量)— 不选
- GUI MVVM:仅重构 MainWindow(~3d)— 不选
- 文档对齐:修订文档为 "v1.1 交付"(0 代码工作量)— 不选

---

## 3. Phase 1 — 紧急止血(0.5d)— ✅ 已完成

### 3.1 目标

移除最直接的安全 / 契约违反点,不引入回归。可独立部署的"热点修复",不需要等大重构。

### 3.2 任务清单

| ID | 任务 | 文件:行 | 验收标准 | 状态 | 预估 |
|---|---|---|---|---|---|
| P1-T1 | 删除主密码 Debug.WriteLine | `src/OmniKeyVault.Cli/CommandHandlers.cs:853` | `grep "Debug.WriteLine.*pw" src/` 返回 0 | ✅ 已完成 | 5min |
| P1-T2 | 修复 sync 退出码错误 | `src/OmniKeyVault.Cli/CommandHandlers.cs:743-745` | `SyncOutcome.Failed` 拆为 `FailedConflict` / `FailedNetwork`,分别返回 14 / 20;新增测试覆盖 | ✅ 已完成 | 30min |
| P1-T3 | 实现 `vault change-password` 子命令 | `src/OmniKeyVault.Cli/CommandHandlers.cs:167` `default` 分支前新增 `case "change-password"` | `okv vault change-password --old-password-env X --new-password-env Y` 退出码 0;旧密码错退出码 4;新增测试 `V1CommandTests.VaultChangePassword_*` | ✅ 已完成(4 tests) | 1h |
| P1-T4 | 创建 `.gitignore` | 仓库根新文件 | 包含 `[Bb]in/` / `[Oo]bj/` / `*.trx` / `[Tt]est[Rr]esults/` / `*.user` / `.vs/`;`git status` 不再显示这些 | ✅ 已完成 | 15min |
| P1-T5 | 删除 Obsolete SearchMatches | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs:154-157` | `grep "Obsolete" src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs` 返回 0 | ✅ 已完成 | 10min |

### 3.3 验收标准(Phase 1 整体)— ✅ 全部通过

- [x] `dotnet build OmniKeyVault.sln -c Release` → 0 warnings, 0 errors
- [x] `dotnet test tests/OmniKeyVault.Tests/` → ≥ 430 通过(本 Phase 不应回退)
- [x] `git status` 干净(除 5 项预期修改 + .gitignore 新增)
- [x] 手动执行 `okv vault change-password` 流程通过(本地烟测)
- [x] grep 不再出现 `Debug.WriteLine.*password` / `Debug.WriteLine.*pw`
- [x] grep 不再出现 `Obsolete` 在 MainWindow.axaml.cs

### 3.4 PR 拆分建议

**单 PR**:`fix(v1.1): phase 1 emergency stopgap (P1-T1..P1-T5)`

- 5 项紧密相关,合计 < 80 行改动
- 适合一次 review,降低 review 焦点分散
- 标签:`phase-1` `security` `docs-alignment`
- 合并方式:Squash merge,保持主线历史清晰

### 3.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| P1-T2 SyncOutcome 拆分可能影响依赖该枚举的代码 | 低 | 拆分时保留 `Failed` 作为 `[Obsolete]` 别名指向 `FailedConflict`,1 个 Sprint 后删除 |
| P1-T3 `change-password` 实现漏掉某些边缘场景 | 中 | 复用 `VaultService.ChangePasswordAsync`(已存在),CLI 仅做参数解析;新增 3 个测试覆盖 happy/旧密码错/新密码弱 |

---

## 4. Phase 2 — 安全合规(2d)— ✅ 已完成

### 4.1 目标

落地 INTERNAL.md §7.2-§7.3 承诺的进程退出清理钩子;核对 TEST_REPORT.md 测试数与实际 trx 一致。

### 4.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 状态 | 预估 |
|---|---|---|---|---|---|
| P2-T1 | 注册 `AppDomain.ProcessExit` + `Console.CancelKeyPress` | `src/OmniKeyVault.Cli/Program.cs` | 进程异常退出 / Ctrl+C 时调用 `_container.Lock.Dispose()`;MK/KEK/DEK 内存清零;新增测试 `ProcessExit_Hook_RegistersAndCallsDispose` | ✅ 已完成(2 tests) | 4h |
| P2-T2 | 核对测试数 451 vs 430 | `docs/TEST_REPORT.md` §1, §2.5 + `tests/OmniKeyVault.Tests/TestResults/current.trx` | 重跑 `dotnet test` 3 次,记录稳定数值;若 430 则修订 TEST_REPORT.md 为 430 + 解释 21 差额(v0.3+v0.4 实际只 +73 不是 +94);若 451 则找出 21 个未运行原因 | ✅ 已完成(确认 457) | 2h |
| P2-T3 | 修订 TEST_REPORT.md 测试目录树 | `docs/TEST_REPORT.md` §3.1 | 与 `tests/OmniKeyVault.Tests/` 实际 .cs 文件一致(当前文档列 22 类,实际 30 文件) | ✅ 已完成 | 1h |
| P2-T4 | 删除陈旧 tests.trx | `tests/tests.trx` + `tests/OmniKeyVault.Tests/TestResults/tests.trx` | 仅保留 `current.trx`(P1-T4 .gitignore 已排除未来生成);手动 `git rm` 两份陈旧文件 | ✅ 已完成 | 10min |
| P2-T5 | SECURITY.md §10.1 Roslyn 分析器承诺暂时标注"v1.1 落地" | `docs/SECURITY.md:510-514` | 4 条规则标注 `[v1.1 落地,见 docs/plan-v1.1-optimization.md Phase 3]`;防止 v1.0 RC 审计期被误解为已实现 | ✅ 已完成 | 30min |

### 4.3 验收标准(Phase 2 整体)— ✅ 全部通过

- [x] `dotnet test` 3 次稳定输出 N 通过(N 为最终核对值,确认 457)
- [x] TEST_REPORT.md §1 表格数字与 trx 一致
- [x] TEST_REPORT.md §3.1 目录树与实际一致
- [x] `tests/tests.trx` 与 `TestResults/tests.trx` 已删除
- [x] 进程退出钩子注册测试通过
- [x] SECURITY.md §10.1 标注 v1.1 落地

### 4.4 PR 拆分建议

**两个 PR**:
1. `feat(v1.1): phase 2 process-exit hooks (P2-T1)` — 仅 Program.cs 改动 + 新增测试,~80 行
2. `docs(v1.1): phase 2 test count reconciliation (P2-T2..P2-T5)` — TEST_REPORT.md / SECURITY.md 修订 + 删除陈旧 trx

理由:P2-T1 是代码改动需 review 逻辑;P2-T2-T5 是文档 + 清理,可独立 merge。

### 4.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| ProcessExit 钩子在 Linux .NET 8 行为差异 | 中 | Linux 下 `AppDomain.ProcessExit` 触发但不保证 2 秒内完成;`CancelKeyPress` 在 SIGTERM 不可靠。文档登记 Linux 限制,Windows 完整 |
| 测试数核对发现 actual < 430 | 中 | 说明有测试不稳定运行(flaky);先定位再修订文档 |
| 测试数核对发现 actual > 430 | 低 | 说明 trx 过期;重跑后修订 TEST_REPORT.md 数字上升 |

---

## 5. Phase 3 — Roslyn 分析器(5d)— 🟡 部分完成

### 5.1 目标

新建 `tools/OmniKeyVault.Analyzers/` 项目,实现 SECURITY.md §10.1 承诺的 4 条分析器规则(OKV0001-OKV0004),作为 v1.0 RC 外部审计的实质性证据。

### 5.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 状态 | 预估 |
|---|---|---|---|---|---|
| P3-T1 | 创建分析器项目骨架 | `tools/OmniKeyVault.Analyzers/OmniKeyVault.Analyzers.csproj` + `tools/OmniKeyVault.Analyzers/okv-diag.png`(可选) | `OutputType=Library` + `<IsRoslynComponent>true</IsRoslynComponent>` + 引用 `Microsoft.CodeAnalysis.CSharp.Workspaces`;`dotnet build` 通过 | ✅ 已完成 | 2h |
| P3-T2 | 实现 OKV0001:禁止 string 作为 ICryptoProvider 参数 | `tools/OmniKeyVault.Analyzers/Okv0001StringForCryptoAnalyzer.cs` | 扫描 `ICryptoProvider` 所有方法调用,若参数为 `string` 或 `string` 派生类型,报错 `OKV0001:string 不得作为密码学参数`;v1.0 代码库扫 0 误报 | ✅ 已完成(5 tests) | 1d |
| P3-T3 | 实现 OKV0002:SecureKey 必须 using 或 try/finally | `tools/OmniKeyVault.Analyzers/Okv0002SecureKeyDisposeAnalyzer.cs` | 扫描所有 `SecureKey` / `MasterKey` / `KeyEncryptionKey` / `DataEncryptionKey` 局部变量,若不在 `using` / `try-finally` 中报错;v1.0 代码库扫应发现 P1-5 提到的 `ChangePasswordAsync` 泄漏点 | ⏸ 延至 v1.2 | 1d |
| P3-T4 | 实现 OKV0003:禁止 == 比较密钥 / MAC / 签名 | `tools/OmniKeyVault.Analyzers/Okv0003NoEqualityOnSecretsAnalyzer.cs` | 扫描 `byte[]` / `ReadOnlySpan<byte>` 类型的密钥变量比较,要求用 `CryptographicOperations.FixedTimeEquals`;报错并提示修复 | ✅ 已完成(5 tests) | 1d |
| P3-T5 | 实现 OKV0004:Service 方法必须以 EnsureUnlocked 开头 | `tools/OmniKeyVault.Analyzers/Okv0004EnsureUnlockedAnalyzer.cs` | 扫描 `VaultService` / `EntryService` / `ProfileService` 等标记 `[OmniKeyVaultService]` 特性的类,公开方法首行必须是 `EnsureUnlocked()` 调用(或在方法体顶部)[Note: 需先引入 `[OmniKeyVaultService]` 特性] | ⏸ 延至 v1.2 | 1d |
| P3-T6 | 引入 `[OmniKeyVaultService]` 特性 | `src/OmniKeyVault.Application/OmniKeyVaultServiceAttribute.cs` | 标记 VaultService / EntryService / ProfileService / BackupService / SeedExporter / SeedImporter / SyncService / AttachmentService / SearchService / ImportExportService 等 | ⏸ 延至 v1.2 | 30min |
| P3-T7 | 将分析器注入所有 src/ 项目 | `Directory.Build.props` 新增 `<Analyzer Include="..\..\tools\OmniKeyVault.Analyzers\..." />` | `dotnet build` 时分析器自动运行;故意写 `string password = null; crypto.DeriveMasterKey(password, ...)` 应构建失败 | ✅ 已完成 | 1h |
| P3-T8 | 先以 warning 级别发布,扫全代码库 | `tools/OmniKeyVault.Analyzers/OkvAnalysisRules.cs` 设 `severity = DiagnosticSeverity.Warning` | 跑全代码库 build,统计 false positive;确认 0 后升级为 Error | ✅ 已完成(0 误报) | 2h |
| P3-T9 | 升级为 error 级别 + 修复发现的违规 | 各 src/ 文件 | `TreatWarningsAsErrors=true` 下分析器 warning 即构建失败;修复所有真实违规 | ✅ 已完成(0 违规) | 4h |
| P3-T10 | 单元测试分析器自身 | `tests/OmniKeyVault.Analyzers.Tests/` 新项目 | 每条规则 ≥ 3 个测试:正例(应通过)/ 反例(应报错)/ 边界(如 `using` 嵌套) | ✅ 已完成(10 tests: 5 OKV0001 + 5 OKV0003) | 1d |

### 5.3 验收标准(Phase 3 整体)— 🟡 部分通过

- [x] `tools/OmniKeyVault.Analyzers/` 项目存在,`dotnet build` 通过
- [ ] ~~4 条规则(OKV0001-0004)实现 + 单元测试覆盖~~ → 2/4 实现(OKV0001 + OKV0003);OKV0002 + OKV0004 延至 v1.2
- [x] `Directory.Build.props` 注入分析器到所有 src/ 项目
- [x] `dotnet build OmniKeyVault.sln -c Release` → 0 warnings 0 errors(含分析器检查)
- [x] 故意写违规代码 → 构建失败,错误消息含 `OKV000X`(OKV0001 / OKV0003)
- [x] SECURITY.md §10.1 标注从"v1.1 落地"改为"✅ v1.1 已交付"(OKV0001 / OKV0003)
- [x] 分析器测试项目加入 `OmniKeyVault.sln`

### 5.4 PR 拆分建议

**5 个 PR**(每条规则一个 + 基础设施):

1. `feat(v1.1): phase 3 analyzer infrastructure (P3-T1, P3-T6, P3-T7, P3-T10)` — 项目骨架 + 特性 + 注入 + 测试框架,~300 行
2. `feat(v1.1): phase 3 OKV0001 string-for-crypto analyzer (P3-T2)` — 含 3+ 测试
3. `feat(v1.1): phase 3 OKV0002 secure-key-dispose analyzer (P3-T3)` — 含 3+ 测试,**预期会报 P1-5 的 ChangePasswordAsync 泄漏**,在 Phase 4 修复
4. `feat(v1.1): phase 3 OKV0003 no-equality-on-secrets analyzer (P3-T4)`
5. `feat(v1.1): phase 3 OKV0004 ensure-unlocked analyzer (P3-T5, P3-T8, P3-T9)` — 先 warning 后 error,合并到本 PR

**Review 顺序**:PR1 → PR2-4 并行 → PR5。每个 PR 合并后跑全测试。

### 5.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 分析器误报已有合规代码 | 中 | P3-T8 先 warning 级别扫全代码库;发现的 false positive 在规则代码中加白名单或调整匹配条件;确认 0 误报后才升级 error |
| OKV0004 `[OmniKeyVaultService]` 特性需大范围标注 | 中 | 用 source generator 自动标注 `*Service` 命名的类;或人工标注 12 个 service 类 |
| 分析器在 IDE 中实时运行可能卡顿 | 低 | Roslyn 分析器本就增量运行;若卡顿则调 `Category.Dynamics.Latest` |
| 测试分析器自身需要 Roslyn SDK | 中 | 用 `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 包,提供 `CSharpAnalyzerVerifier<TAnalyzer>` |

---

## 6. Phase 4 — 正确性修复(小)(5d)

### 6.1 目标

修复 8 项不涉及大范围重构的正确性 bug。这些是 v1.0 RC 已知但未修的"小漏",修复后可显著提升稳定性。

### 6.2 任务清单

| ID | 任务 | 文件:行 | 验收标准 | 预估 |
|---|---|---|---|---|
| P4-T1 | 修复 AttachmentService LRU→真 LRU | `src/OmniKeyVault.Application/Services/AttachmentService.cs:37,121,218` | 改 `LinkedList<string> + Dictionary<string, LinkedListNode<string>>`;访问时 `RemoveFirst` + `AddLast` 提升;新增测试 `Lru_OnRead_PromotesToMostRecent` + `Lru_OnOverflow_EvictsLeastRecentlyUsed` | 4h |
| P4-T2 | 修复 ChangePasswordAsync 密钥泄漏 | `src/OmniKeyVault.Application/Services/VaultService.cs:308-412` | `newMk` / `newKek` 改 `using var`;成功路径"取出"所有权(赋值给字段 + `GC.SuppressFinalize`);异常路径自动 Dispose;OKV0002 分析器验证 | 2h |
| P4-T3 | 修复 SodiumCryptoProvider.Verify 异常吞噬 | `src/OmniKeyVault.Infrastructure/Crypto/SodiumCryptoProvider.cs:156-166` | 只捕获 `CryptographicException`;其他异常(`OutOfMemoryException` / `DllNotFoundException` / `SEHException`)上抛;新增测试 `Verify_LibsodiumFault_ThrowsNotReturnsFalse`(模拟需 mock,可用 `#if DEBUG` 注入故障) | 1h |
| P4-T4 | 修复 LockService 同步 .Wait() 阻塞 | `src/OmniKeyVault.Application/Services/LockService.cs:42,95` | `_gate.Wait()` → `await _gate.WaitAsync(ct).ConfigureAwait(false)`;`ActivateKeys` 改 `async Task`;调用方 `VaultService.UnlockAsync` 已是 async,无破坏;新增测试 `ActivateKeys_DoesNotBlockThreadPool` | 2h |
| P4-T5 | 实现 `--password-file` 权限检查 | `src/OmniKeyVault.Cli/CommandHandlers.cs:846-851` | 读取前检查 ACL:Windows 用 `FileSecurity.GetAccessRules` 验证仅 owner 可读;Linux 用 `Mono.Posix` 或 `stat -c %a` 验证 600;非合规权限警告并要求 `--yes`;新增测试 `PasswordFile_PermissionTooOpen_WarnsAndRequiresYes` | 4h |
| P4-T6 | 实现 stdout 30 秒清零 | `src/OmniKeyVault.Cli/SecureStdout.cs` 新文件 + `CommandHandlers.cs` 集成 | `entry get --format raw` 输出后,30 秒延迟 Dispose + `ZeroMemory` 底层 buffer;`AppDomain.ProcessExit` 提前触发清零;新增测试 `Stdout_After30Seconds_IsZeroed`(注:terminal scrollback 无法清零,文档已承认) | 1d |
| P4-T7 | 修复 AttachmentService 写附件非原子 | `src/OmniKeyVault.Application/Services/AttachmentService.cs:98-109` | 改用 `IStorageProvider.WriteAtomicAsync`;新增测试 `Save_DuringCrash_LeavesNoTruncatedFile`(模拟崩溃) | 2h |
| P4-T8 | 修复退出码与 INTERNAL.md §3 偏差 | `src/OmniKeyVault.Cli/CommandHandlers.cs:50-60` | 在 `catch (FileNotFoundException)` 后增加 `catch (IOException)` / `catch (UnauthorizedAccessException)` / `catch (PathTooLongException)` 都返回 6;新增测试 `IoException_Returns6` | 1h |
| P4-T9 | 修复 GUI 事件处理器泄漏(Mark 1) | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs:76,99,105,1569,1599` | override `OnClosed`,`Dispose` `_clipboardClearTimer` / `_lockCountdownTimer`,取消 `PointerMoved` / `KeyDown` / `SystemEvents.SessionLocked` / `Watcher.FileChanged` 订阅 | 2h |
| P4-T10 | 修复 GUI TOTP timer 泄漏 | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs:956-958` | `System.Timers.Timer` 改 `DispatcherTimer`(UI 线程自带);`OnClosed` 中 `Stop + Dispose`;多个 TOTP 字段共用一个 timer,不再累积 handler | 1h |

### 6.3 验收标准(Phase 4 整体)

- [ ] AttachmentService LRU 测试通过(2 个新测试)
- [ ] ChangePasswordAsync 异常路径 `newMk` / `newKek` 已 Dispose(OKV0002 分析器扫 0 违规)
- [ ] SodiumCryptoProvider.Verify 仅捕获 `CryptographicException`
- [ ] LockService 不再使用 `.Wait()`
- [ ] `--password-file` 权限检查在 Windows + Linux 各通过 1 个测试
- [ ] stdout 30 秒清零测试通过
- [ ] AttachmentService.Save 崩溃恢复测试通过
- [ ] 退出码 6 在 IOException / UnauthorizedAccessException / PathTooLongException 场景正确返回
- [ ] MainWindow 关闭后无事件订阅残留(可用 `dotnet monitor` 验证)
- [ ] TOTP timer 不再随字段数累积
- [ ] `dotnet test` 通过数 ≥ 440(本 Phase 新增 ~10 测试)

### 6.4 PR 拆分建议

**4 个 PR**:

1. `fix(v1.1): phase 4 crypto & key lifecycle (P4-T2, P4-T3, P4-T4)` — ChangePasswordAsync + SodiumCryptoProvider + LockService,密码学相关聚焦
2. `fix(v1.1): phase 4 attachment & io (P4-T1, P4-T7, P4-T8)` — AttachmentService LRU + 原子写 + 退出码
3. `feat(v1.1): phase 4 cli security (P4-T5, P4-T6)` — password-file 权限 + stdout 清零
4. `fix(v1.1): phase 4 gui leak fixes (P4-T9, P4-T10)` — GUI 事件 + TOTP timer

**依赖**:PR1 必须在 Phase 3 PR5(OKV0002/OKV0004 error 级别)合并后;否则 P4-T2 的 `using` 改动会让 OKV0002 在 PR review 时报当前已存在的泄漏。其他 PR 可并行。

### 6.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| P4-T1 真 LRU 改动破坏现有附件测试 | 中 | 跑 `AttachmentServiceTests.cs` 10 个测试全部通过;新增 2 个 LRU 特定测试 |
| P4-T2 ChangePasswordAsync using 改动可能引入编译问题 | 中 | `using var` + 字段所有权转移需 `GC.SuppressFinalize` 谨慎;在 PR 中加详细注释 |
| P4-T3 Verify 改异常策略可能让某些原本静默失败的场景变抛异常 | 中 | 跑 `CryptoTests.cs` 全部 29 个测试;新增 libsodium 故障注入测试 |
| P4-T4 LockService 改 async 可能连锁影响调用方 | 中 | `VaultService.UnlockAsync` / `CreateAsync` 已 async,直接 `await`;CLI 调用方若 sync-over-async 用 `.GetAwaiter().GetResult()`(本 Phase 不改,Phase 7 重构时统一) |
| P4-T5 Linux ACL 检查需 `Mono.Posix` 或 p/invoke | 中 | 用 `stat` 命令通过 `Process.Start` 调用,简单但稍慢;或用 `Mono.Posix.NETStandard` NuGet 包 |
| P4-T6 stdout 清零在 .NET 8 Console 难度高 | 高 | `Console.OpenStandardOutput()` 返回的 stream 内部 buffer 不暴露;可能需自定义 `TextWriter` 包装 + `StringBuilder` 暂存;若不可行则降级为"30 秒后 GC.Collect + 文档登记限制" |

---

## 7. Phase 5 — 文档对齐功能(3d)

### 7.1 目标

落地 TEST_REPORT.md §2.5.2 / ROADMAP.md §4.3.1 声称"已交付"但代码未实现的两项功能:BackupService 磁盘版 + SyncService 接入 IWatcherProvider。

### 7.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P5-T1 | BackupService 磁盘版实现 | `src/OmniKeyVault.Application/Services/BackupService.cs` 重写 + `src/OmniKeyVault.Infrastructure/Format/SnapshotFormat.cs` 新文件 | 写入 `.okv.snapshots/<profile>/<entry-id>/<version>.entry.enc`;OKVS magic(0x4F 4B 56 53);签名覆盖除签名字段外所有字节;API 兼容(`Capture` / `Restore` / `ListSnapshots` 签名不变) | 1d |
| P5-T2 | SnapshotFormat 实现 | `src/OmniKeyVault.Infrastructure/Format/SnapshotFormat.cs` | 按 OKV_FORMAT.md §10 实现:Magic(4B) + SnapshotVersion(2B) + EntryId(16B) + Version(4B) + CreatedAt(8B) + AEAD Payload + Ed25519 Signature(64B);复用 `IStorageProvider.WriteAtomicAsync` | 4h |
| P5-T3 | BackupService 测试补全 | `tests/Backup/BackupServiceTests.cs` 扩展 | 新增 5 个测试:`Capture_WritesToDisk` / `Restore_ReadsFromDisk` / `ListSnapshots_ReturnsAllVersions` / `TamperedSnapshotFile_RejectedBySignature` / `CrashDuringWrite_LeavesNoPartialFile` | 4h |
| P5-T4 | SyncService 注入 IWatcherProvider | `src/OmniKeyVault.Application/Services/SyncService.cs` 构造函数 + `StartWatchAsync` / `StopWatch` 方法 | 构造函数加 `IWatcherProvider watcher` 参数;`StartWatchAsync` 调用 `watcher.Watch(syncDir, filter, OnFileChanged)`;`OnFileChanged` 200ms debounce 后调用 `MergeWithRemoteAsync`;`CliContainer` / `GuiShell` 注册 IWatcherProvider 实现 | 1d |
| P5-T5 | OnFileChanged debounce 实现 | `src/OmniKeyVault.Application/Services/SyncService.cs` 私有 ` DebounceFileChanged` | 用 `System.Threading.Channels` 序列化事件 + 200ms 合并;多次事件合并为一次 `MergeWithRemoteAsync` 调用 | 4h |
| P5-T6 | SyncService 测试补全 | `tests/Sync/SyncServiceTests.cs` 扩展 | 新增 5 个测试:`Watch_DetectsRemoteChange_TriggersMerge` / `Watch_DebouncesMultipleEvents` / `Watch_OnMergeFailure_DoesNotCrash` / `StopWatch_ReleasesWatcherResource` / `Watch_DuringLock_DoesNotTrigger` | 4h |
| P5-T7 | GuiShell 启动时调用 SyncService.StartWatchAsync | `src/OmniKeyVault.Cli/Gui/GuiShell.cs` 解锁后 | 解锁成功后 `await _container.Sync.StartWatchAsync()`;锁定时 `StopWatch` | 1h |
| P5-T8 | CliContainer 注册 OSWatcherProvider | `src/OmniKeyVault.Cli/CliContainer.cs` DI 注册 | `services.AddSingleton<IWatcherProvider, FileSystemWatcherProvider>()`;Linux/macOS 兜底 `NoOpWatcherProvider` | 30min |
| P5-T9 | 修订 TEST_REPORT.md §9 偏差登记 | `docs/TEST_REPORT.md:373-401` | A.1 watcher 偏差改为"✅ v1.1 已交付";A.5 snapshot 持久化改为"✅ v1.1 已交付" | 30min |

### 7.3 验收标准(Phase 5 整体)

- [ ] `.okv.snapshots/<profile>/<entry-id>/<version>.entry.enc` 在磁盘生成
- [ ] OKVS magic 文件头正确
- [ ] 签名验证:篡改 1 字节 → `CryptoException`
- [ ] 崩溃恢复:`.okv.tmp` 残留下次启动清理
- [ ] SyncService 启动 watcher 后,远端文件变更 5 秒内触发 merge
- [ ] 200ms 内多次事件合并为一次 merge
- [ ] 锁定状态下 watcher 不触发 merge
- [ ] CliContainer DI 注册 IWatcherProvider
- [ ] TEST_REPORT.md §9 偏差登记更新
- [ ] `dotnet test` 通过数 ≥ 450(本 Phase 新增 ~10 测试)

### 7.4 PR 拆分建议

**3 个 PR**:

1. `feat(v1.1): phase 5 backup service disk persistence (P5-T1, P5-T2, P5-T3, P5-T9 部分)` — BackupService 重写 + SnapshotFormat + 测试
2. `feat(v1.1): phase 5 sync watcher integration (P5-T4, P5-T5, P5-T6, P5-T8)` — SyncService 注入 + debounce + 测试 + DI
3. `feat(v1.1): phase 5 gui watcher wiring (P5-T7, P5-T9 收尾)` — GuiShell 启动 + 文档收尾

**依赖**:PR1 与 PR2 可并行;PR3 依赖 PR2。

### 7.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| P5-T1 磁盘版 BackupService 与现有 HistoryWindow UI 行为不一致 | 低 | API 签名不变,UI 无需改动;测试覆盖 `Restore` 路径 |
| P5-T4 watcher 在某些云盘(OneDrive)误触发 | 中 | 200ms debounce + 文件 hash 比对(变更才 merge);若仍误触发则加白名单 |
| P5-T5 debounce 用 Channels 可能死锁 | 低 | `Channel.CreateBounded(1)` + `await Task.Delay(200, ct)` 模式,ct 在 StopWatch 时取消 |
| Linux/macOS `NoOpWatcherProvider` 兜底意味着非 Windows 无自动同步 | 中 | 文档登记;v1.x 引入 `FSEvents` / `inotify` 实现 |

---

## 8. Phase 6 — Field.Value 改 byte[] + 显式清零(10d)

### 8.1 目标

彻底解决 P1-4 锁定不清零 INV-03 违反。`Field.Value` 从 `string` 改 `byte[]`,所有 UI 绑定通过 UTF-8 编解码辅助。

### 8.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P6-T1 | Field.Value 类型改 byte[] | `src/OmniKeyVault.Domain/Field.cs` | `record Field(... byte[] Value ...)`;`FieldKind` 不变;构建错误数统计(预计 ~50 文件需改) | 30min |
| P6-T2 | 创建 UTF-8 编解码辅助 | `src/OmniKeyVault.Application/FieldCodec.cs` 新文件 | `static class FieldCodec { static byte[] Encode(string s); static string Decode(byte[] b); }` + 单元测试;`Encode` 用 `Encoding.UTF8.GetBytes` + `Decode` 用 `Encoding.UTF8.GetString` | 1h |
| P6-T3 | VaultFormat / SeedFormat 适配 | `src/OmniKeyVault.Infrastructure/Format/VaultFormat.cs` + `SeedFormat.cs` | Field 序列化时 `Value` 直接写 byte[](length-prefixed),不再 UTF-8 编码;反序列化直接读 byte[];OKV1 / OKVD 格式版本 +1(Header Version 0x01 0x01)| 1d |
| P6-T4 | ProfilePayloadCodec 适配 | `src/OmniKeyVault.Application/Format/ProfilePayloadCodec.cs` | 同 P6-T3,内部编解码改 byte[] | 4h |
| P6-T5 | VaultService / EntryService 适配 | `src/OmniKeyVault.Application/Services/VaultService.cs` + `EntryService.cs` | PutEntry / GetEntry 接受 / 返回 byte[];调用方负责 UTF-8 编解码 | 1d |
| P6-T6 | BitwardenImporter / KeePassXmlImporter 适配 | `src/OmniKeyVault.Application/Services/BitwardenImporter.cs` + `KeePassXmlImporter.cs` | JSON / XML 字符串字段值用 `FieldCodec.Encode` 转 byte[] 再赋给 Field.Value | 4h |
| P6-T7 | SearchService 适配 | `src/OmniKeyVault.Application/Services/SearchService.cs` | 搜索时 `FieldCodec.Decode(field.Value)` 后匹配;`FieldHit` 缓存解码后的 string 避免重复解码 | 4h |
| P6-T8 | AttachmentService 适配 | `src/OmniKeyVault.Application/Services/AttachmentService.cs` | file_ref 字段值原本是 attachment-id 字符串,改 byte[] 后用 `FieldCodec.Encode` | 2h |
| P6-T9 | CLI CommandHandlers 适配 | `src/OmniKeyVault.Cli/CommandHandlers.cs` | `entry set --field <key>` 从 stdin 读 string 后 `FieldCodec.Encode` 转 byte[];`entry get --field <key>` 输出时 `FieldCodec.Decode` | 4h |
| P6-T10 | GUI 适配(Mark 1:EditorWindow) | `src/OmniKeyVault.Cli/Gui/Views/EditorWindow.axaml.cs` | TextBox.Text → `FieldCodec.Encode` → Field.Value;反向 `FieldCodec.Decode` → TextBox.Text;Reveal / Copy 路径同步 | 1d |
| P6-T11 | GUI 适配(Mark 2:MainWindow 条目列表) | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs` | 显示字段值时 `FieldCodec.Decode` + MaskValue;Copy 时 `FieldCodec.Decode` 后写剪贴板 | 1d |
| P6-T12 | GUI 适配(Mark 3:其余 12 个 view) | 各 .axaml.cs | 显示 / 编辑 Field.Value 的地方全部适配;SearchWindow 高亮路径同步 | 1d |
| P6-T13 | Lock 时显式清零 | `src/OmniKeyVault.Application/Services/VaultService.cs:271-277` | Lock 前遍历 `_profiles` 所有 Entry.Fields,`Sensitive=true` 的 `Value` 调用 `CryptographicOperations.ZeroMemory` | 1h |
| P6-T14 | INV-03 测试 | `tests/Crypto/SecureKeyMemoryTests.cs` 新文件 | `Lock_ZerosAllSensitiveFieldValues`:创建 Entry 含 sensitive field → unlock → 拿 Value 引用 → lock → 验证引用指向的 byte[] 全 0 | 2h |
| P6-T15 | OKV1 格式版本升级 + 向后兼容 | `src/OmniKeyVault.Infrastructure/Format/VaultFormat.cs` | 读旧 Header Version 1.0 时,Field.Value 按字符串读后 `Encoding.UTF8.GetBytes` 转 byte[];写新 Header Version 1.1 时直接写 byte[] | 1d |
| P6-T16 | 测试套件全量适配 | `tests/OmniKeyVault.Tests/` 全部 | ~30 个测试文件中凡构造 Field 的地方加 `FieldCodec.Encode`;跑 `dotnet test` 全绿 | 1d |

### 8.3 验收标准(Phase 6 整体)

- [ ] `Field.Value` 类型为 `byte[]`
- [ ] `dotnet build` 0 warnings 0 errors
- [ ] `dotnet test` 通过(测试数 ≥ 450,本 Phase 不新增测试除 P6-T14)
- [ ] INV-03 测试 `Lock_ZerosAllSensitiveFieldValues` 通过
- [ ] OKV0001 分析器扫 0 违规(Field.Value 是 byte[] 不再是 string)
- [ ] 旧 v1.0 .okv 文件可读(向后兼容)
- [ ] 新 v1.1 .okv 文件 Header Version = 1.1
- [ ] GUI 各 view 显示 / 编辑 Field.Value 正常
- [ ] CLI `entry get` / `entry set` 正常
- [ ] Bitwarden / KeePass 导入导出正常
- [ ] 搜索正常

### 8.4 PR 拆分建议

**6 个 PR**(按依赖顺序):

1. `refactor(v1.1): phase 6 fieldCodec + domain Field.Value byte[] (P6-T1, P6-T2)` — 类型改 + 编解码辅助,构建会大量失败,作为基础
2. `refactor(v1.1): phase 6 infrastructure format adapt (P6-T3, P6-T4, P6-T15)` — VaultFormat / SeedFormat / ProfilePayloadCodec 适配 + 向后兼容
3. `refactor(v1.1): phase 6 application services adapt (P6-T5, P6-T6, P6-T7, P6-T8)` — VaultService / EntryService / Importers / SearchService / AttachmentService
4. `refactor(v1.1): phase 6 cli adapt (P6-T9)` — CommandHandlers
5. `refactor(v1.1): phase 6 gui adapt (P6-T10, P6-T11, P6-T12)` — 14 个 view 适配,可拆 3 子 PR(EditorWindow / MainWindow / 其余)
6. `feat(v1.1): phase 6 lock zeroing + INV-03 test (P6-T13, P6-T14, P6-T16 收尾)` — 锁定清零 + 测试 + 全测试套件适配收尾

**依赖**:PR1 → PR2 → PR3 → PR4 + PR5 并行 → PR6。每个 PR 合并后跑全测试,任何回退立即修复。

### 8.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 改动范围 ~50 文件,引入回归 | 高 | 每个 PR 合并后跑全测试;关键路径(解锁 / 保存 / 同步)手动烟测 |
| 旧 .okv 文件无法读 | 高 | P6-T15 向后兼容必须先在 PR2 落地,测试覆盖 `ReadV1_0_ConvertsStringToBytes` |
| SearchService 解码性能下降 | 中 | P6-T7 缓存解码结果;benchmark 验证 ≤ 200ms 仍达标 |
| GUI TextBox 绑定 byte[] 不直观 | 中 | 用 `IValueConverter` 自动 UTF-8 编解码;ViewModel 暴露 `string FieldValueText { get; set; }`,内部转 byte[] |
| 测试套件 ~30 文件全改 | 中 | 用脚本批量替换 `new Field(... Value: "xxx" ...)` → `Value: FieldCodec.Encode("xxx")`;人工审核 |
| INV-03 测试需不安全代码 | 中 | 用 `fixed` / `GCHandle.Alloc(Pinned)` 拿引用;或用 `Marshal.AllocHGlobal` + unsafe 拷贝验证 |

---

## 9. Phase 7 — 架构拆分(5d)

### 9.1 目标

拆 God Class:VaultService(817 行)→ 3 个 service;CommandHandlers(1204 行)→ 7 个 handler 文件;CliParser(225 行手写)→ System.CommandLine。

### 9.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P7-T1 | 拆 VaultService 为 VaultLifecycleService + ProfileService(扩展) + FolderService | `src/OmniKeyVault.Application/Services/VaultLifecycleService.cs` 新 + `ProfileService.cs` 扩展 + `FolderService.cs` 新 | VaultService 保留作为兼容门面(forward 调用),~50 行;实际逻辑迁移;28 个公开方法按职责分配 | 1d |
| P7-T2 | 拆 CommandHandlers 为 7 个 handler 文件 | `src/OmniKeyVault.Cli/Handlers/VaultHandler.cs` 等 7 个新文件 | 每个 handler ~150 行;`CommandHandlers.cs` 改名为 `CommandDispatcher.cs`,~80 行只做路由 | 1d |
| P7-T3 | 提取 HelpText 到独立文件 | `src/OmniKeyVault.Cli/HelpText.cs` | 200 行 HelpText 移出;未来按命令拆分到各 handler | 30min |
| P7-T4 | 提取 ConfigKeys + SyncPauseState | `src/OmniKeyVault.Cli/ConfigKeys.cs` + `SyncPauseState.cs` | 两个独立文件;`CommandDispatcher.cs` 不再含 | 15min |
| P7-T5 | 迁移 CliParser 到 System.CommandLine | `src/OmniKeyVault.Cli/CliParser.cs` 重写 + `OmniKeyVault.Cli.csproj` 加 `<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />` | 225 行 → ~80 行;支持 `--key=value` 短选项 `-q`;自动 help 生成;`CliParserTests` 全部通过(可能需调整测试) | 1d |
| P7-T6 | 16 env var demo 路由改 Dictionary | `src/OmniKeyVault.Cli/Gui/App.axaml.cs:75-185` | `Dictionary<string, Action<GuiShell>> _demoRoutes`;查找 O(1);注释清晰 | 1h |
| P7-T7 | App.axaml.cs catch 不 rethrow 修复 | `src/OmniKeyVault.Cli/Gui/App.axaml.cs:178-181` | `Log(ex); throw;`;启动失败弹错误对话框 | 15min |
| P7-T8 | GuiShell sync-over-async 修复 | `src/OmniKeyVault.Cli/Gui/GuiShell.cs:348,386,412,441,591,619` | `.GetAwaiter().GetResult()` → `async Task` + `await`;调用方 `await` | 2h |
| P7-T9 | 文档同步更新 | `docs/ARCHITECTURE.md` §4.3 服务层 + §4.1 UI 模块 + `docs/BUILD.md` §1 仓库结构 + `docs/INTERNAL.md` §2 命令结构 | VaultLifecycleService / ProfileService / FolderService / 7 handler 文件加入文档;Cli/ 子目录描述修正 | 2h |

### 9.3 验收标准(Phase 7 整体)

- [ ] VaultService.cs ≤ 100 行(门面)
- [ ] VaultLifecycleService / ProfileService(扩展) / FolderService 各 ≤ 400 行
- [ ] CommandDispatcher.cs ≤ 100 行
- [ ] 7 个 handler 文件各 ≤ 200 行
- [ ] CliParser.cs ≤ 100 行
- [ ] App.axaml.cs demo 路由用 Dictionary
- [ ] App.axaml.cs 启动失败 rethrow
- [ ] GuiShell 无 `.GetAwaiter().GetResult()`
- [ ] `dotnet test` 通过(测试数 ≥ 450)
- [ ] ARCHITECTURE.md / BUILD.md / INTERNAL.md 同步更新
- [ ] OKV0004 分析器扫所有 service 方法首行 EnsureUnlocked(Phase 3 落地后)

### 9.4 PR 拆分建议

**5 个 PR**:

1. `refactor(v1.1): phase 7 split VaultService (P7-T1)` — 最大改动,~800 行迁移
2. `refactor(v1.1): phase 7 split CommandHandlers (P7-T2, P7-T3, P7-T4)` — CLI handler 拆分
3. `refactor(v1.1): phase 7 migrate to System.CommandLine (P7-T5)` — CliParser 重写,可能影响 CliParserTests
4. `fix(v1.1): phase 7 gui app startup (P7-T6, P7-T7, P7-T8)` — App.axaml.cs + GuiShell
5. `docs(v1.1): phase 7 architecture doc sync (P7-T9)` — 文档同步

**依赖**:PR1 与 PR2 可并行;PR3 依赖 PR2(handler 已拆,parser 改动测试易定位);PR4 独立;PR5 最后。

### 9.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| VaultService 拆分破坏 VaultIntegrationTests | 高 | 门面模式保留 VaultService 类与所有公开方法,内部 forward;测试 0 改动;后续逐步迁移测试到新 service |
| System.CommandLine 仍是 beta 版本 | 中 | 2.0.0-beta4 已稳定,Microsoft 官方推荐;若担心可锁版本 + hash |
| CliParserTests 大量改写 | 中 | System.CommandLine 提供 `Parser.InvokeAsync` 测试入口;~28 个测试改写,工作量 1d |
| GuiShell sync-over-async 改 async 影响调用方 | 中 | 调用方均为 event handler `async void`,直接 `await`;无 sync 上下文死锁风险 |

---

## 10. Phase 8 — GUI MVVM 全量重构(10d)

### 10.1 目标

引入 CommunityToolkit.Mvvm;14 个 view 全部拆 ViewModel;MainWindow.axaml.cs 从 1590 行降到 ~400;解锁 Avalonia headless 自动化测试。

### 10.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P8-T1 | 引入 CommunityToolkit.Mvvm 包 | `src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj` | `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />`;`dotnet restore` 通过 | 15min |
| P8-T2 | 创建 ViewModels/ 目录 + ViewModelBase | `src/OmniKeyVault.Cli/Gui/ViewModels/ViewModelBase.cs` | `ObservableObject` 子类;`[ObservableProperty]` / `[RelayCommand]` 模式示例 | 1h |
| P8-T3 | MainViewModel 实现 | `src/OmniKeyVault.Cli/Gui/ViewModels/MainViewModel.cs` | 迁移 MainWindow.axaml.cs 的状态字段 + 命令;~400 行;`ProfileList` / `EntryList` / `SelectedEntry` / `SearchQuery` 等 observable property | 1d |
| P8-T4 | MainWindow.axaml 改 compiled bindings | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml` | `x:DataType="vm:MainViewModel"`;`ItemsSource="{Binding EntryList}"` 等;code-behind 移除 `Text = ...` 赋值 | 1d |
| P8-T5 | MainWindow.axaml.cs 瘦身到 ~400 行 | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml.cs` | 仅保留:构造注入 VM、控件事件转 VM 命令、UI 特殊逻辑(拖拽、TOTP timer);1590 → ~400 | 1d |
| P8-T6 | 提取 Base32Decode + ParseOtpAuthUri 到 Application | `src/OmniKeyVault.Application/OtpAuthUtils.cs` 新文件 | 从 MainWindow:933-1000 迁移;`TotpService.ParseSecretFromUri` 复用;新增单元测试 | 2h |
| P8-T7 | 提取 MaskValue 到 Application | `src/OmniKeyVault.Application/FieldMasking.cs` 新文件 | 从 MainWindow:1657-1662 迁移;`MaskValue(byte[] value, string? mask)` 接 byte[];新增单元测试 | 1h |
| P8-T8 | 提取 PlatformBrush 到 App.axaml 资源 | `src/OmniKeyVault.Cli/Gui/App.axaml` | 6 个平台颜色改 `<Color x:Key="PlatformOpenAIBrush">...</Color>`;MainWindow:1667-1676 改 `FindResource` | 1h |
| P8-T9 | EditorViewModel + EditorWindow 重构 | `src/OmniKeyVault.Cli/Gui/ViewModels/EditorViewModel.cs` + `EditorWindow.axaml(.cs)` | 512 行 → ~150 行 code-behind;`FieldRowControl : UserControl` 提取(P2-6) | 1d |
| P8-T10 | SettingsViewModel + SettingsWindow 重构 | `SettingsViewModel.cs` + `SettingsWindow.axaml(.cs)` | 595 行 → ~200 行;`ChangePasswordDialog` 提取为 `UserControl`(P2-5);`BuildDeviceInfoPanel` 改 VM 内 async | 1d |
| P8-T11 | CreateVaultViewModel + CreateVaultWizard 重构 | `CreateVaultViewModel.cs` + `CreateVaultWizard.axaml(.cs)` | 493 行 → ~150 行;6 步状态机改 VM | 1d |
| P8-T12 | 其余 10 个 view 重构 | `UnlockViewModel` / `ProfileSwitcherViewModel` / `SearchViewModel` / `HistoryViewModel` / `RecoveryKeyViewModel` / `SeedImportViewModel` / `SeedExportViewModel` / `KeePassImportViewModel` / `SyncConflictResolverViewModel` / `DeviceTrustDialogViewModel` + 各 .axaml.cs | 各 view code-behind ≤ 100 行;compiled bindings | 2d |
| P8-T13 | 提取 ConfirmDialog / PromptDialog 通用 helper | `src/OmniKeyVault.Cli/Gui/Dialogs/ConfirmDialog.cs` + `PromptDialog.cs` | `ShowAsync(parent, message, yesText, noText)` 静态;EditorWindow:519 / MainWindow:342,637 三处复用 | 4h |
| P8-T14 | SearchTextBox 加 debounce | `src/OmniKeyVault.Cli/Gui/Views/MainWindow.axaml` 或 `MainViewModel.cs` | `OnSearchTextChanged` 250ms debounce;`ObservableProperty<string> SearchQuery` + `DelayedBinding` 模式 | 1h |
| P8-T15 | 111 处硬编码中文抽 UIStrings | `src/OmniKeyVault.Cli/Gui/Resources/Strings.zh-CN.resx` + `Strings.en-US.resx` + 各 .axaml.cs | 53(MainWindow) + 18(Settings) + 13(Editor) + 10(CreateVault) + 17(其余)= 111 处全抽;`UIStrings.Get("main.empty_hint")` 模式 | 1d |
| P8-T16 | 12 处 magic hex 颜色抽 App.axaml | `src/OmniKeyVault.Cli/Gui/App.axaml` + `Res.cs` | `<Color x:Key="...">` 12 个;`Res.Brush("PlatformOpenAIBrush")` 模式 | 2h |
| P8-T17 | GuiShell 改 VM 注入 | `src/OmniKeyVault.Cli/Gui/GuiShell.cs` + `App.axaml.cs` | `new MainWindow(new MainViewModel(_container))` 模式;DI 注册 VM | 2h |
| P8-T18 | Avalonia headless 测试 PoC | `tests/OmniKeyVault.Tests/Gui/MainWindowHeadlessTests.cs` 新文件 | 用 `Avalonia.Headless` 包;`MainWindow` 渲染 + 按钮点击 + 验证 VM 状态变更;1-2 个示例测试 | 1d |

### 10.3 验收标准(Phase 8 整体)

- [ ] `CommunityToolkit.Mvvm` 包引入
- [ ] 14 个 ViewModel 文件存在
- [ ] MainWindow.axaml.cs ≤ 400 行(从 1590)
- [ ] 14 个 view code-behind 平均 ≤ 150 行
- [ ] compiled bindings 启用(`x:DataType`)
- [ ] Base32Decode / ParseOtpAuthUri / MaskValue / PlatformBrush 提取
- [ ] ConfirmDialog / PromptDialog 通用 helper
- [ ] SearchTextBox 250ms debounce
- [ ] 111 处中文字符串全抽 UIStrings
- [ ] 12 处 hex 颜色全抽 App.axaml 资源
- [ ] en-US 切换后全 UI 英文(手动验证)
- [ ] Avalonia headless 测试 PoC 通过
- [ ] `dotnet test` 通过数 ≥ 460(本 Phase 新增 ~10 测试)

### 10.4 PR 拆分建议

**8 个 PR**:

1. `refactor(v1.1): phase 8 mvvm infrastructure (P8-T1, P8-T2, P8-T17)` — 包 + ViewModelBase + GuiShell 注入
2. `refactor(v1.1): phase 8 MainViewModel (P8-T3, P8-T4, P8-T5)` — 最大单 PR,~1000 行迁移
3. `refactor(v1.1): phase 8 extract utils (P8-T6, P8-T7, P8-T8, P8-T13)` — Application 层工具 + App.axaml 资源 + 通用 Dialog
4. `refactor(v1.1): phase 8 EditorViewModel (P8-T9)` + `SettingsViewModel (P8-T10)` — 两个大 view 各一 PR,可并行
5. `refactor(v1.1): phase 8 CreateVaultViewModel (P8-T11)` + `其余 10 view (P8-T12)` — 拆 2-3 个 PR
6. `feat(v1.1): phase 8 i18n + debounce (P8-T14, P8-T15, P8-T16)` — UIStrings 抽取 + debounce + 颜色抽取
7. `test(v1.1): phase 8 avalonia headless PoC (P8-T18)` — 测试框架引入 + 1-2 个示例

**依赖**:PR1 → PR2 → PR3 与 PR4 并行 → PR5 → PR6 / PR7 并行。

### 10.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 14 view 全量重构期间 v1.0 RC 漂移 | 高 | 在 `refactor/v1.1-mvvm` 分支进行;主分支继续接收 bug fix;重构完成前不切主线;每 PR 合并后跑全测试 |
| compiled bindings 在 Avalonia 11 配置复杂 | 中 | `x:DataType` 必须在 `Window` / `UserControl` 根节点;`xmlns:vm="clr-namespace:..."`;若失败回退到反射 binding |
| CommunityToolkit.Mvvm 源生成器与 Roslyn 分析器冲突 | 低 | 两者独立;`[ObservableProperty]` 源生成 OKV0001-0004 不应误报;若误报在分析器加白名单 |
| 111 处中文抽取工作量大 | 中 | 用脚本扫 `[\u4e00-\u9fa5]{2,}` 在 .axaml.cs;批量替换 + 人工审核 key 命名;resx 文件人工编辑 |
| Avalonia headless 测试在 CI 跨平台差异 | 中 | `Avalonia.Headless` 依赖 X11 / Windows / macOS;CI 用 `Avalonia.Headless.X11` 在 Linux 容器跑 |
| TOTP timer 从 System.Timers.Timer 改 DispatcherTimer 后行为变 | 低 | DispatcherTimer 在 UI 线程触发,无 `InvokeAsync` 开销;测试覆盖 |

---

## 11. Phase 9 — SearchService 索引(3d)

### 11.1 目标

构建真倒排索引;predicate 缓存;GUI debounce 已在 Phase 8 落地。

### 11.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P9-T1 | SearchService 改倒排索引 | `src/OmniKeyVault.Application/Services/SearchService.cs` | `Dictionary<string, HashSet<EntryId>>` _invertedIndex;Entry 写入时增量更新;`Search` 用索引查找 O(1) | 1d |
| P9-T2 | 索引增量更新 | `src/OmniKeyVault.Application/Services/SearchService.cs` | `OnEntrySaved` / `OnEntryDeleted` 事件订阅;token 提取 + 索引更新 | 4h |
| P9-T3 | predicate 缓存 | `src/OmniKeyVault.Application/Services/SearchService.cs` | `Dictionary<string, Predicate<Entry>> _predicateCache`;query string 为 key;LRU 限制 100 | 2h |
| P9-T4 | SearchService 测试补全 | `tests/V03/SearchServiceTests.cs` 扩展 | 新增 5 测试:`Index_IncrementalUpdate_OnEntrySave` / `Index_OnEntryDelete_RemovesToken` / `PredicateCache_HitsSameQuery` / `Search_LargeDataset_PerformanceUnder200ms`(1万条目) / `Index_RebuiltAfterLockUnlock` | 4h |
| P9-T5 | benchmark 验证 | `tools/OmniKeyVault.Benchmark/Program.cs` `BenchSearch` | 1万条目搜索 P50 ≤ 200ms(当前 1.5ms,改后应 ≤ 0.5ms) | 1h |

### 11.3 验收标准(Phase 9 整体)

- [ ] 倒排索引实现
- [ ] 增量更新事件订阅
- [ ] predicate 缓存
- [ ] 5 个新测试通过
- [ ] benchmark 1万条目搜索 ≤ 200ms
- [ ] 锁定 / 解锁后索引可重建
- [ ] `dotnet test` 通过数 ≥ 465

### 11.4 PR 拆分建议

**2 个 PR**:

1. `refactor(v1.1): phase 9 SearchService inverted index (P9-T1, P9-T2, P9-T3)` — 索引实现
2. `test(v1.1): phase 9 SearchService tests + benchmark (P9-T4, P9-T5)` — 测试 + benchmark 验证

**依赖**:PR1 → PR2。Phase 8 PR2(MainViewModel)合并后(VM 调 SearchService 路径稳定)。

### 11.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 倒排索引内存占用大(1万条目 × ~10 token / entry) | 低 | `HashSet<EntryId>` 引用紧凑;估算 ~5MB,可接受 |
| 增量更新事件订阅可能漏掉某些路径 | 中 | EntryService.PutEntry / DeleteEntry 都触发事件;LockService.Lock 时清空索引 |
| predicate 缓存 LRU 又是 FIFO bug 重蹈 | 低 | 复用 Phase 4 P4-T1 的真 LRU 实现 |

---

## 12. Phase 10 — 测试补全(4d)

### 12.1 目标

补全 SECURITY.md 承诺的 4 个 invariant 测试;Benchmark 场景扩展 + CI 集成。

### 12.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P10-T1 | SEC-T3-01 锁定后内存扫描测试 | `tests/Crypto/SecureKeyMemoryTests.cs` 新文件 | `SEC_T3_01_AfterLock_ProcessMemoryScan_NoMkResidue`:unlock → 拿 MK 引用 → lock → 验证引用指向的 byte[] 全 0(类似 P6-T14,但更严格) | 4h |
| P10-T2 | INV-10 崩溃转储不含 MK 测试 | `tests/Crypto/CrashDumpTests.cs` 新文件(Windows-only) | `INV_10_CrashDump_NoMkBytes`:注册 `SetUnhandledExceptionFilter` + 触发异常 + MiniDumpWriteDump + 扫描 dump 文件无 MK 字节;Linux/macOS 标 `[Fact(Skip="Windows-only")]` | 1d |
| P10-T3 | SEC-T8-01 显式命名的 1 字节篡改测试 | `tests/Format/FormatTests.cs` 扩展 | `SEC_T8_01_OneByteTamper_RejectsFile`:打开真 vault → 翻转 body 1 字节 → 尝试 unlock → `CryptoException`;Theory 数据翻转 5 个偏移 | 2h |
| P10-T4 | INV-09 同步路径仅密文测试 | `tests/Integration/SyncCiphertextTests.cs` 新文件 | `INV_09_SyncPath_OnlyCiphertext`:sync 后磁盘文件 + manifest.json 字节扫描,无 `sk-` / `ghp_` / `AKIA` 等明文 prefix | 4h |
| P10-T5 | Benchmark 增加 entry-save 场景 | `tools/OmniKeyVault.Benchmark/Program.cs` | `BenchEntrySave(int count)`:save 单条目 ≤ 100ms;1万次 save 总耗时 | 2h |
| P10-T6 | Benchmark 增加 attachment 场景 | `tools/OmniKeyVault.Benchmark/Program.cs` | `BenchAttachment(int sizeKB)`:1KB / 100KB / 1MB / 10MB 加解密耗时 | 2h |
| P10-T7 | Benchmark 增加 TOTP 场景 | `tools/OmniKeyVault.Benchmark/Program.cs` | `BenchTotp(int iterations)`:1万次 TOTP 生成 ≤ 1ms 单次 | 1h |
| P10-T8 | Benchmark 增加 password-change 场景 | `tools/OmniKeyVault.Benchmark/Program.cs` | `BenchChangePassword()`:含 4 profile 的 vault 改密码 ≤ 1s | 1h |
| P10-T9 | Benchmark 集成 dotnet test | `tools/OmniKeyVault.Benchmark/OmniKeyVault.Benchmark.csproj` 改 `<IsTestProject>true</IsTestProject>` + 加 `Microsoft.NET.Test.Sdk` + 包装为 `[Fact]` | `dotnet test` 包含 benchmark;CI 自动跑;性能回归即测试失败 | 4h |
| P10-T10 | 引入 BenchmarkDotNet | `tools/OmniKeyVault.Benchmark/` + `OmniKeyVault.Benchmark.csproj` 加 `<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />` | 替换 Stopwatch;输出 mean / median / p99 / stddev / 分配内存 | 1d |
| P10-T11 | xunit.runner.json 加超时 | `tests/OmniKeyVault.Tests/xunit.runner.json` 新文件 | `<Method name="*" Timeout="5000" />` 5 秒超时;`Thread.Sleep` 回归即失败 | 30min |
| P10-T12 | V04GuiFlowTests.cs:61 修复 flaky | `tests/V04/V04GuiFlowTests.cs:61` | `Task.Delay(1200)` → 1500ms 或 polling 模式(每 100ms 检查直到条件满足,总 5s 超时) | 30min |
| P10-T13 | V1CommandTests / V2CommandTests 加 IDisposable | `tests/Cli/v1/V1CommandTests.cs` + `v2/V2CommandTests.cs` | 构造函数设环境变量,Dispose 清理;`Environment.SetEnvironmentVariable(..., null)` | 1h |
| P10-T14 | V03GuiFlowTests:174 拼写修复 | `tests/V03/V03GuiFlowTests.cs:174` | `Roundtrips` → `RoundTrips` | 5min |
| P10-T15 | 测试目录树文档同步 | `docs/TEST_REPORT.md` §3.1 | 22 类 → 实际文件数;新增 4 个测试文件 | 30min |

### 12.3 验收标准(Phase 10 整体)

- [ ] SEC-T3-01 测试通过
- [ ] INV-10 测试通过(Windows)
- [ ] SEC-T8-01 测试通过
- [ ] INV-09 测试通过
- [ ] Benchmark 4 个新场景通过
- [ ] `dotnet test` 包含 benchmark
- [ ] BenchmarkDotNet 输出统计
- [ ] xunit.runner.json 超时配置生效
- [ ] V04GuiFlowTests:61 不再 flaky(跑 10 次 0 失败)
- [ ] V1/V2CommandTests IDisposable 实现
- [ ] `dotnet test` 通过数 ≥ 480

### 12.4 PR 拆分建议

**5 个 PR**:

1. `test(v1.1): phase 10 invariant tests (P10-T1, P10-T2, P10-T3, P10-T4)` — 4 个 invariant 测试
2. `perf(v1.1): phase 10 benchmark scenarios (P10-T5, P10-T6, P10-T7, P10-T8)` — 4 个新场景
3. `perf(v1.1): phase 10 benchmark ci integration (P10-T9, P10-T10)` — dotnet test 集成 + BenchmarkDotNet
4. `test(v1.1): phase 10 test hygiene (P10-T11, P10-T12, P10-T13, P10-T14)` — 超时 / flaky / IDisposable / 拼写
5. `docs(v1.1): phase 10 test report sync (P10-T15)` — 文档同步

**依赖**:PR1 独立;PR2 独立;PR3 依赖 PR2;PR4 独立;PR5 最后。

### 12.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| INV-10 崩溃转储测试在 CI 难度大 | 高 | Windows-only 用 `MiniDumpWriteDump` P/Invoke;Linux/macOS 标 Skip;CI 矩阵 Windows 跑该测试 |
| SEC-T3-01 内存扫描需不安全代码 | 中 | `fixed` / `GCHandle.Alloc(Pinned)`;或 `Marshal.AllocHGlobal` 模拟 |
| BenchmarkDotNet 与现有 Stopwatch 共存 | 低 | BenchmarkDotNet 仅在 `tools/OmniKeyVault.Benchmark/`,不影响生产代码 |
| xunit.runner.json 超时过严 | 中 | 5s 足够大部分测试;`IdleTimerTests` 最长 3s,余量 2s;若失败则单独调高 |
| Benchmark 集成 dotnet test 拖慢 CI | 中 | Benchmark 用 `[Trait("Category","Benchmark")]` 标签;CI 默认跑 `--filter "Category!=Benchmark"`;nightly 跑全套 |

---

## 13. Phase 11 — 重复消除 + 配置(3d)

### 13.1 目标

提取重复代码(原子写、BuildProfileAad、CollectTags、Magic strings);补 .editorconfig / .gitattributes / Directory.Packages.props。

### 13.2 任务清单

| ID | 任务 | 文件 | 验收标准 | 预估 |
|---|---|---|---|---|
| P11-T1 | 4 处原子写统一到 IStorageProvider | `VaultFormat.cs:146-164` + `SeedFormat.cs:121-139` + `ManifestService.cs:56-68` + `AttachmentService.cs:98-109`(Phase 4 已改一处) | 全部调用 `_storage.WriteAtomicAsync(path, async stream => { ... })`;4 份内联代码删除 | 2h |
| P11-T2 | 提取 BuildProfileAad | `src/OmniKeyVault.Application/Crypto/AadBuilder.cs` 新文件 | 4 处调用(VaultService:786 / SyncService:350 / SeedExporter:112 / SeedImporter:104)改 `AadBuilder.BuildProfileAad(...)` | 1h |
| P11-T3 | 提取 CollectTags | `src/OmniKeyVault.Application/Services/TagCollector.cs` 新文件 | 2 处调用(VaultService:777 / SeedExporter:128)改 `TagCollector.Collect(entries)` | 30min |
| P11-T4 | 提取 BinaryIoExtensions | `src/OmniKeyVault.Infrastructure/Format/BinaryIoExtensions.cs` 新文件 | `WriteString` / `ReadString` / `WriteBytes` / `ReadBytes` 扩展方法;VaultFormat + SeedFormat 用 | 1h |
| P11-T5 | 提取 CryptoConstants | `src/OmniKeyVault.Application/CryptoConstants.cs` 新文件 | `"okv-kek-v1"` / `"okv-kwrap-v1"` / `"okv-verify-v1"` / `"okv-seed-kek-v1"` 4 个常量;7 处引用改 | 1h |
| P11-T6 | SignWith 改注入 | `VaultFormat.cs:282` + `SeedFormat.cs:226` | 构造函数注入 `ICryptoProvider`,不再 `new SodiumCryptoProvider()` | 1h |
| P11-T7 | .editorconfig | 仓库根新文件 | root=true + LF + 4 空格 + `csharp_style_var_for_built_in_types:false` 等 Roslyn 规则 | 30min |
| P11-T8 | .gitattributes | 仓库根新文件 | `* text=auto eol=lf` + `*.cs diff=csharp` + `*.json merge=union` | 15min |
| P11-T9 | Directory.Packages.props | 仓库根新文件 + `Directory.Build.props` 加 `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` | 4 个 NuGet 包(Sodium.Core / xunit / FluentAssertions / Microsoft.NET.Test.Sdk + CommunityToolkit.Mvvm + System.CommandLine + BenchmarkDotNet + Microsoft.CodeAnalysis.CSharp.Workspaces)版本集中 | 1h |
| P11-T10 | .csproj 移除版本号 | 各 .csproj | `<PackageReference Include="..." Version="..." />` 改 `<PackageReference Include="..." />` | 30min |
| P11-T11 | Directory.Build.props 加确定性构建 | `Directory.Build.props` | `<Deterministic>true</Deterministic>` + `<ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>` + `<SourceLink>true</SourceLink>` | 30min |

### 13.3 验收标准(Phase 11 整体)

- [ ] `IStorageProvider.WriteAtomicAsync` 4 处全调
- [ ] AadBuilder / TagCollector / BinaryIoExtensions / CryptoConstants 提取
- [ ] SignWith 注入 ICryptoProvider
- [ ] .editorconfig / .gitattributes / Directory.Packages.props 创建
- [ ] 所有 .csproj 用中央包版本
- [ ] `Directory.Build.props` 确定性构建启用
- [ ] `dotnet build` 0 warnings 0 errors
- [ ] `dotnet restore` 走 Directory.Packages.props

### 13.4 PR 拆分建议

**3 个 PR**:

1. `refactor(v1.1): phase 11 dedup (P11-T1, P11-T2, P11-T3, P11-T4, P11-T5, P11-T6)` — 6 项重复消除
2. `chore(v1.1): phase 11 build config (P11-T7, P11-T8, P11-T9, P11-T10, P11-T11)` — 配置文件 + 中央包管理
3. (可选)`chore(v1.1): phase 11 ci workflow` — GitHub Actions / Gitea Actions 配置(若用户使用)

**依赖**:PR1 独立;PR2 独立;可并行。

### 13.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 中央包管理迁移可能影响 NuGet restore | 中 | PR2 合并后跑 `dotnet restore --force` 验证;`Directory.Packages.props` 版本与原 .csproj 一致 |
| .editorformat 严格规则可能让现有代码大量 warning | 中 | 先用 `dotnet format --verify-no-changes` 看差异;分批格式化 |
| SignWith 注入改动 VaultFormat 构造函数签名 | 中 | 调用方(VaultService)同步改;测试同步改 |

---

## 14. Phase 12 — P3 清理 + Recovery Key base32(3d)

### 14.1 目标

低优先级清理 + Recovery Key 格式迁移(向后兼容)。

### 14.2 任务清单

| ID | 任务 | 文件:行 | 验收标准 | 预估 |
|---|---|---|---|---|
| P12-T1 | Recovery Key 改 base32 | `src/OmniKeyVault.Application/Services/VaultService.cs:795-805` | `Convert.ToHexString` → base32(8 组 × 8 字符 + CRC-8 校验);复用 `TotpService` 的 base32 工具 | 4h |
| P12-T2 | Recovery Key 读取向后兼容 | `src/OmniKeyVault.Application/Services/VaultService.cs` | 读取时同时支持 hex(旧) + base32(新);先试 base32 失败再试 hex | 2h |
| P12-T3 | RecoveryKeyRenderer 适配 | `src/OmniKeyVault.Cli/Gui/RecoveryKeyRenderer.cs` | 显示 8 组 × 8 字符 base32;打印格式调整 | 1h |
| P12-T4 | 测试覆盖 | `tests/` Recovery Key 相关测试 | 新增 `RecoveryKey_Base32_Format` + `RecoveryKey_HexLegacy_ReadStillWorks` + `RecoveryKey_Crc8_DetectsTypo` | 2h |
| P12-T5 | VaultFormat.cs:171 注释修正 | `src/OmniKeyVault.Infrastructure/Format/VaultFormat.cs:171` | "80 bytes" → "72 bytes" | 5min |
| P12-T6 | VaultService.cs:326-333 自相矛盾注释删除 | `src/OmniKeyVault.Application/Services/VaultService.cs:326-333` | 删除引用不存在的 `PersistUpdatedRecordAsync` 注释 | 5min |
| P12-T7 | SyncService.cs:297-299 死代码删除 | `src/OmniKeyVault.Application/Services/SyncService.cs:297-299` | 删除 `var record_ = record ?? null;` | 5min |
| P12-T8 | SeedImporter AllowedTargetProfiles 加 ci-* | `src/OmniKeyVault.Application/Services/SeedImporter.cs:16-19` | 实现 `ci-*` 前缀匹配;`StartsWith("ci-")` 通过;其他非 dev/test/ci-* 拒绝 | 30min |
| P12-T9 | BitwardenImporter magic string 改 enum | `src/OmniKeyVault.Application/Services/BitwardenImporter.cs:78` | `enum BitwardenFieldType { Text=0, Hidden=1, Boolean=2 }` | 30min |
| P12-T10 | Program.cs deviceId 持久化 | `src/OmniKeyVault.Cli/Program.cs:39-40` | 从 `%APPDATA%/OmniKeyVault/device.id` 读;不存在则生成 + 写;不再 `MachineName + ProcessId` | 1h |
| P12-T11 | BUILD.md §1 文档树修正 | `docs/BUILD.md` §1 | `Cli/` 子目录描述改为实际根目录;或移动文件 | 30min |
| P12-T12 | App.axaml.cs catch rethrow(若 Phase 7 未做) | `src/OmniKeyVault.Cli/Gui/App.axaml.cs:178-181` | Phase 7 P7-T7 应已做,本任务兜底 | 5min |
| P12-T13 | OpenAiRotator / GitHubPatRotator 改 IHttpClientFactory | `src/OmniKeyVault.Application/Services/OpenAiRotator.cs:36` + `GitHubPatRotator.cs:37` | 注入 `IHttpClientFactory`;CliContainer 注册 `services.AddHttpClient()` | 1h |
| P12-T14 | ProfilePayloadCodec vs VaultFormat 字符串长度上限统一 | `src/OmniKeyVault.Application/Format/ProfilePayloadCodec.cs:225` + `VaultFormat.cs:264` | 提取 `const int MaxStringLength = 1 * 1024 * 1024;`(1 MiB);统一 | 30min |

### 14.3 验收标准(Phase 12 整体)

- [ ] Recovery Key 输出 base32 格式(8 组 × 8 字符 + CRC-8)
- [ ] 旧 hex 格式 Recovery Key 仍可读
- [ ] CRC-8 校验检测 1 字符抄写错误
- [ ] 所有 P3 注释 / 死代码 / magic string 清理
- [ ] `ci-*` Profile 导入 seed 通过
- [ ] deviceId 持久化,重启进程不变
- [ ] IHttpClientFactory 注入
- [ ] `dotnet test` 通过数 ≥ 485

### 14.4 PR 拆分建议

**3 个 PR**:

1. `feat(v1.1): phase 12 recovery key base32 (P12-T1, P12-T2, P12-T3, P12-T4)` — 格式迁移 + 向后兼容 + 测试
2. `chore(v1.1): phase 12 p3 cleanup (P12-T5, P12-T6, P12-T7, P12-T8, P12-T9, P12-T14)` — 注释 / 死代码 / magic string / ci-* / enum / 长度上限
3. `chore(v1.1): phase 12 device id + http + docs (P12-T10, P12-T11, P12-T12, P12-T13)` — deviceId 持久化 + IHttpClientFactory + 文档

**依赖**:全部独立,可并行。

### 14.5 风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| Recovery Key 格式迁移破坏现有 Vault | 中 | P12-T2 向后兼容必须先落地;测试 `RecoveryKey_HexLegacy_ReadStillWorks` 通过 |
| CRC-8 实现错误 | 低 | 复用 libsodium 或 `CRC8` 标准实现;测试 1 字符翻转检测 |
| deviceId 持久化在多用户环境共享 | 低 | 文件权限 600;`%APPDATA%` 用户隔离 |
| IHttpClientFactory 注入需 DI 改造 | 低 | CliContainer + GuiShell 都注册 `services.AddHttpClient()` |

---

## 15. 工作量与时间线汇总

### 15.1 工作量明细

| Phase | 工作量 | 累计 | 主要交付物 |
|---|---|---|---|
| Phase 1 紧急止血 | 0.5d | 0.5d | 5 项 P0 修复 |
| Phase 2 安全合规 | 2d | 2.5d | ProcessExit 钩子 + 测试数核对 |
| Phase 3 Roslyn 分析器 | 5d | 7.5d | 4 条分析器规则 + 注入 |
| Phase 4 正确性修复(小) | 5d | 12.5d | LRU + ChangePassword + Verify + LockService + password-file + stdout + 附件原子 + 退出码 + GUI 泄漏 + TOTP timer |
| Phase 5 文档对齐功能 | 3d | 15.5d | BackupService 磁盘 + Watcher 接入 |
| Phase 6 Field.Value → byte[] | 10d | 25.5d | Field 类型改 + INV-03 落地 + 向后兼容 |
| Phase 7 架构拆分 | 5d | 30.5d | VaultService 拆 + CommandHandlers 拆 + System.CommandLine |
| Phase 8 GUI MVVM 全量重构 | 10d | 40.5d | 14 ViewModel + i18n + 通用 Dialog + Avalonia headless PoC |
| Phase 9 SearchService 索引 | 3d | 43.5d | 倒排索引 + predicate 缓存 |
| Phase 10 测试补全 | 4d | 47.5d | 4 invariant 测试 + Benchmark 扩展 + CI 集成 + BenchmarkDotNet |
| Phase 11 重复消除 + 配置 | 3d | 50.5d | 6 项提取 + .editorconfig / .gitattributes / Directory.Packages.props |
| Phase 12 P3 清理 + Recovery Key base32 | 3d | 53.5d | Recovery Key base32 + 14 项 P3 清理 |

**总计:53.5 人天**(约 11 周一人,或 5-6 周两人并行)。

### 15.2 时间线(单人执行)

```
2026-07-04 ─ Phase 1 完成
2026-07-08 ─ Phase 2 完成
2026-07-15 ─ Phase 3 完成
2026-07-22 ─ Phase 4 完成
2026-07-25 ─ Phase 5 完成
2026-08-08 ─ Phase 6 完成
2026-08-15 ─ Phase 7 完成
2026-08-29 ─ Phase 8 完成
2026-09-01 ─ Phase 9 完成
2026-09-05 ─ Phase 10 完成
2026-09-08 ─ Phase 11 完成
2026-09-11 ─ Phase 12 完成 → v1.1 RC 候选
```

### 15.3 两人并行时间线(后端 + UI)

```
后端:  Phase 1 → 2 → 3 → 4 → 5 → 6 → 9 → 10 → 11 → 12
UI:                 Phase 3 协助 → 8(全职 2 周) → 12 GUI 部分
全栈:                              Phase 7 → 11
```

两人并行约 6-7 周完成。

### 15.4 关键依赖图

```
Phase 1 ─► Phase 2 ─► Phase 3 ─► Phase 4 ─► Phase 5 ─► Phase 6 ─► Phase 7 ─► Phase 8 ─► Phase 9 ─► Phase 10 ─► Phase 11 ─► Phase 12
                                              │
                                              └─ Phase 8 依赖 Phase 7 拆分完成
                                              └─ Phase 6 依赖 Phase 3 分析器就位
                                              └─ Phase 10 依赖 Phase 9 SearchService 稳定
```

**可并行 Phase**:
- Phase 4 + Phase 5(都依赖 Phase 3,但内部独立)
- Phase 11 + Phase 12(都低优先级,可并行)
- Phase 8(UI 全职)+ Phase 9(后端,不冲突)

---

## 16. 风险与缓解(全局)

### 16.1 技术风险

| 风险 | 等级 | 影响范围 | 缓解 |
|---|---|---|---|
| Field.Value → byte[] 引入大范围破坏性变更 | 高 | Phase 6 | 分两步:先加 `byte[] ValueBytes` 字段并行存在 + 一个 view 一个 view 迁移,每迁移完一个跑全测试 |
| GUI MVVM 全量重构期间 v1.0 RC 漂移 | 高 | Phase 8 | 在 `refactor/v1.1-mvvm` 分支进行;主分支继续接收 bug fix;重构完成前不切主线 |
| Roslyn 分析器误报已有合规代码 | 中 | Phase 3 | 先 warning 级别扫全代码库;确认 0 误报后才升级 error |
| Recovery Key 格式迁移破坏现有 Vault | 中 | Phase 12 | 读取时同时支持 hex(旧) + base32(新);v1.1 升级路径提示用户重新生成 |
| BackupService 磁盘版与 HistoryWindow UI 行为不一致 | 低 | Phase 5 | API 兼容(`Capture` / `Restore` / `ListSnapshots` 签名不变) |
| System.CommandLine 仍是 beta | 中 | Phase 7 | 锁版本 + hash;若担心用 `McMaster.Extensions.CommandLineUtils` 替代 |
| Avalonia headless 测试在 CI 跨平台差异 | 中 | Phase 8 | `Avalonia.Headless.X11` 在 Linux 容器跑;Windows / macOS 跳过 |
| INV-10 崩溃转储测试在 CI 难度大 | 高 | Phase 10 | Windows-only 用 `MiniDumpWriteDump`;Linux/macOS 标 Skip |
| stdout 30 秒清零在 .NET 8 Console 难度高 | 高 | Phase 4 | 自定义 `TextWriter` 包装 + `StringBuilder` 暂存;若不可行降级为"30 秒后 GC.Collect + 文档登记限制" |

### 16.2 进度风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 团队成员流失导致知识断层 | 中 | 每 Phase 完成后做内部 tech share;文档同步更新 |
| Phase 6 工作量超预期(byte[] 改动波及更多文件) | 高 | Phase 6 预留 2 天缓冲;若超预期则 Phase 12 P3 清理延后到 v1.2 |
| 外部审计公司对接延期 | 中 | 本计划不依赖外部审计;审计可在 Phase 12 完成后任意时间开始 |
| v1.0 RC 期间用户报告 bug 需紧急修复 | 中 | 主分支维护 v1.0 RC bug fix;v1.1 分支定期 rebase |

### 16.3 安全风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| Phase 6 Field.Value 改 byte[] 期间出现 INV-03 回归 | 高 | 每 PR 合并后跑 INV-03 测试(Phase 6 P6-T14 落地后) |
| Phase 3 Roslyn 分析器自身有 bug 漏报 | 中 | 分析器单元测试 ≥ 3 个/规则;外部审计复核 |
| Recovery Key 旧 hex 格式仍可读 = 攻击面 | 低 | hex Recovery Key 文档登记为 deprecated;v1.2 强制重新生成 base32 |

---

## 17. 完成后预期状态

### 17.1 代码层面

| 指标 | v1.0 RC 当前 | v1.1 目标 |
|---|---|---|
| 产品代码行数 | ~9,200 | ~10,500(+Field.Value + 分析器) |
| 测试代码行数 | ~7,500 | ~9,000(+4 invariant + Benchmark + ViewModel) |
| 单文件最大行数(产品) | 1590(MainWindow.axaml.cs) | ~400(MainViewModel) |
| 单文件最大行数(测试) | 439(VaultIntegrationTests) | ~500 |
| God Class 数(>500 行) | 4 | 0 |
| TODO / FIXME | 0 | 0 |
| `as any` / cast hacks | 0 | 0 |
| `Thread.Sleep` in tests | 8 | 8(都是 IdleTimer 必需) |
| Roslyn 分析器规则 | 0(仅文档) | 4(OKV0001-0004) |

### 17.2 测试层面

| 指标 | v1.0 RC 当前 | v1.1 目标 |
|---|---|---|
| 测试总数 | 430(trx) / 451(文档) | ≥ 485 |
| INV-01 ~ INV-10 全有测试 | 8/10 | 10/10 |
| SEC-T1-01 ~ SEC-T8-01 全有测试 | 6/8 | 8/8 |
| GUI 自动化测试 | 12(服务层) | 12 + 5(Avalonia headless PoC) |
| Benchmark 场景 | 4 | 8 |
| Benchmark 集成 dotnet test | ❌ | ✅ |
| BenchmarkDotNet 统计 | ❌ | ✅ |
| xunit.runner.json 超时 | ❌ | ✅(5s) |
| flaky 测试 | 1(V04GuiFlowTests:61) | 0 |

### 17.3 文档层面

| 文档 | v1.0 RC 偏差点 | v1.1 状态 |
|---|---|---|
| TEST_REPORT.md | 测试数 451 vs 430 | ✅ 一致 |
| SECURITY.md §10.1 | Roslyn 分析器承诺未实现 | ✅ v1.1 落地 |
| INTERNAL.md §7.2-§7.5 | stdout 清零 / password-file 权限未实现 | ✅ v1.1 落地 |
| OKV_FORMAT.md §10 | OKVS snapshot 磁盘格式未实现 | ✅ v1.1 落地 |
| OKV_FORMAT.md §11 | Recovery Key hex 而非 base32 | ✅ v1.1 落地 |
| BUILD.md §1 | Cli/ 子目录描述错误 | ✅ 修正 |
| BUILD.md §9 | .gitignore / .editorconfig 等缺失 | ✅ v1.1 落地 |
| ARCHITECTURE.md §4.3 | VaultService 817 行 god class | ✅ 拆为 3 service |
| MANUAL.md §15 | 111 处中文硬编码 | ✅ 抽 UIStrings |

### 17.4 部署 / DevOps

| 指标 | v1.0 RC | v1.1 |
|---|---|---|
| .gitignore | ❌ | ✅ |
| .editorconfig | ❌ | ✅ |
| .gitattributes | ❌ | ✅ |
| Directory.Packages.props(中央包管理) | ❌ | ✅ |
| 确定性构建 | ❌ | ✅ |
| CI 配置 | ❌ | ✅(GitHub Actions / Gitea) |
| Benchmark CI 集成 | ❌ | ✅ |
| MSIX / 单文件 / Portable 三形态 | ❌(v1.0 RC 待 Sprint 10) | ✅(v1.1 落地) |

---

## 18. 附录

### 18.1 审计原始产出

本计划基于以下 3 个 explore 代理的审计结果(2026-07-03):

1. `bg_23d722cb` — Application + Infrastructure 层审计(6m 18s)
2. `bg_4895983f` — Cli 项目(GUI + CLI)审计(4m 38s)
3. `bg_50ddb502` — Tests + Benchmark 审计(5m 48s)

完整审计输出存于 `C:\Users\MWS\.local\share\opencode\tool-output\` 下三个 `tool_f26f96a4*.txt` 文件。

### 18.2 与既有文档的关系

| 文档 | 关系 |
|---|---|
| [ROADMAP.md](./ROADMAP.md) | v1.0 RC Sprint 9-10 任务在本计划完成后开始;本计划是 v1.1 路线图 |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | Phase 7 拆 VaultService 后 §4.3 服务层表更新 |
| [SECURITY.md](./SECURITY.md) | Phase 3 Roslyn 分析器落地后 §10.1 标注 ✅ |
| [INTERNAL.md](./INTERNAL.md) | Phase 4 stdout 清零 + password-file 权限落地后 §7 全部 ✅ |
| [TEST_REPORT.md](./TEST_REPORT.md) | 每 Phase 完成后 §1 表格 + §3.1 目录树更新 |
| [BUILD.md](./BUILD.md) | Phase 11 .editorconfig 等落地后 §9 偏差表更新 |
| [MANUAL.md](./MANUAL.md) | Phase 8 i18n 抽取后 §15 中文优先原则 ✅ |
| [OKV_FORMAT.md](./OKV_FORMAT.md) | Phase 5 OKVS 落地 + Phase 6 Header Version 1.1 + Phase 12 Recovery Key base32 后更新 |

### 18.3 PR 标签约定

| 标签 | 用途 |
|---|---|
| `phase-1` ~ `phase-12` | 标识所属 Phase |
| `security` | 安全相关改动 |
| `crypto` | 密码学改动 |
| `gui` | GUI 改动 |
| `cli` | CLI 改动 |
| `refactor` | 重构(无行为变化) |
| `fix` | bug 修复 |
| `feat` | 新功能(本计划中仅指落地文档承诺的功能) |
| `test` | 测试新增 / 修复 |
| `docs` | 文档改动 |
| `chore` | 构建 / 配置 / 清理 |
| `perf` | 性能改动 |

### 18.4 Commit 规范

遵循 [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <subject>

<body>

<footer>
```

示例:
```
fix(v1.1): phase 1 remove password debug log (P1-T1)

Removes `Debug.WriteLine($"[ReadPassword] env var '{args_PasswordEnv}' -> '{pw}'")`
at CommandHandlers.cs:853 which violates SECURITY.md §7.4.

Refs: docs/plan-v1.1-optimization.md §3 P1-T1
```

### 18.5 修订记录

| 版本 | 日期 | 修订 |
|---|---|---|
| 1.0 | 2026-07-03 | 初稿:基于 v1.0 RC 代码审计(3 个 explore 代理产出)制定 12 阶段任务分解;含每 Phase 任务清单 / 验收标准 / PR 拆分 / 风险;总计 53.5 人天;4 项关键决策已与用户确认(实现 Roslyn 分析器 / Field.Value 改 byte[] / GUI 全量 MVVM 重构 / 文档对齐功能全部实现) |

---

**文档结束**。本计划作为 v1.1 RC 的执行蓝图,每 Phase 完成后应:
1. 更新本文件对应 Phase 状态为 ✅;
2. 更新 [TEST_REPORT.md](./TEST_REPORT.md) §1 表格测试数;
3. 在 [ROADMAP.md](./ROADMAP.md) §7 v1.0 RC 段落增加"v1.1 优化进行中"链接;
4. 提交 PR 时在描述中引用本文件对应 Phase 段落。
