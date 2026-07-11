# OmniKey Vault — 开发路线图 / Sprint 计划

| 文档版本 | 日期 | 作者 | 状态 |
|---|---|---|---|
| 1.3 | 2026-07-12 | Sisyphus | v1.6 交付:Double Argon2id 密钥拉伸 + 公开源码安全加固,578/578 tests |
| 1.1 | 2026-07-07 | Sisyphus | v1.1 优化进行中:Phase 1-2 已完成,Phase 3 部分完成;467/467 tests |
| 1.0 | 2026-06-25 | Sisyphus | v0.1-v0.4 全部已交付(451/451 tests);v1.0 RC(外部审计 + 签名 + MSIX)进行中 |

> 关联文档:[MANUAL.md §16-18 路线图](./MANUAL.md#16-已交付里程碑) · [ARCHITECTURE.md](./ARCHITECTURE.md) · [SECURITY.md](./SECURITY.md) · [TEST_REPORT.md](./TEST_REPORT.md)
>
> 本文档把 [MANUAL.md §16-18 路线图](./MANUAL.md#16-已交付里程碑) 拆解为可执行的 Sprint 任务,服务于开发团队日常协作。**v0.1 / v0.2 已交付,本文件记录实际完成情况**;v0.3+ 任务为下一阶段规划。

---

## 1. 概述

### 1.1 文档目的

1. 记录 v0.1 / v0.2 已交付的实际 Sprint 任务与产出(对照 [TEST_REPORT.md](./TEST_REPORT.md))。
2. 规划 v0.3 → v1.0 的剩余任务、产出物、验收标准、依赖。
3. 标识跨里程碑的依赖与风险。
4. 提供团队配置与分工建议。

### 1.2 时间盒与实际状态

| 里程碑 | 时长 | 目标发布 | 实际状态 |
|---|---|---|---|
| v0.1 MVP | 4 周(2 Sprint) | 2026-07-16 | ✅ 已交付(2026-06-22,170/170 tests) |
| v0.2 | 4 周(2 Sprint) | 2026-08-13 | ✅ 已交付(2026-06-23,357/357 tests,GUI 全部主流程落地) |
| v0.3 | 4 周(2 Sprint) | 2026-09-10 | ✅ 已交付(2026-06-24,搜索 + 附件 + KeePass + 国际化,73 新增测试) |
| v0.4 | 4 周(2 Sprint) | 2026-10-08 | ✅ 已交付(2026-06-25,轮换 + 历史 + 性能,21 新增测试) |
| v1.0 RC | 4 周(2 Sprint) | 2026-11-05 | 🟡 进行中(451/451 tests;待外部审计 + 签名 + MSIX + 视频) |
| **v1.1 优化** | **~11 周(1-2 人)** | **2026-09-11** | **🟡 进行中(Phase 1-2 已完成;Phase 3 部分完成 OKV0001+OKV0003;467/467 tests)** |

总计 20 周(5 个月),含 v1.0 公开发布准备。**v0.1-v0.4 共提前 11 周交付**(原计划 4 个里程碑 = 16 周,实际 4 个里程碑 = 1 个 Sprint ≈ 2 周)。

### 1.3 Sprint 节奏

- **Sprint 长度**:2 周。
- **第一周**:开发 + 单元测试。
- **第二周**:集成测试 + Code Review + Sprint Demo + 回顾。
- **每日**:15 分钟 standup,同步进度与阻塞。

### 1.4 团队配置建议

| 角色 | 人数 | 职责 |
|---|---|---|
| Tech Lead / 架构师 | 1 | 架构决策、Code Review、安全审查 |
| 后端 / 密码学工程师 | 1 | CryptoProvider、VaultService、文件格式 |
| 全栈工程师 | 1 | Service 层、CLI、UI ViewModel |
| UI 工程师 | 1 | Avalonia UI、控件、交互 |
| QA / 测试工程师 | 1 | 测试用例、E2E、性能基准、安全测试 |

**MVP 阶段最小配置**:Tech Lead + 2 工程师(后端 + 全栈)+ 兼职 QA。

---

## 2. 里程碑总览

```
v0.1 MVP ──► v0.2 ──► v0.3 ──► v0.4 ──► v1.0
  │           │         │         │         │
  │           │         │         │         └── 公开发布 + 安全审计
  │           │         │         └── 自动锁屏 + 历史快照 + 轮换
  │           │         └── CLI + KDBX + 全文搜索 + 附件
  │           └── 多 Profile + Dev 备份 + 同步 + TOTP
  └── 本地 Vault + 单 Profile + 条目 CRUD + 5 模板 + Bitwarden 导入
```

### 2.1 里程碑依赖

| 里程碑 | 依赖前置 | 后续解锁 |
|---|---|---|
| v0.1 | — | v0.2(格式已定) |
| v0.2 | v0.1 | v0.3(Profile 模型稳定) |
| v0.3 | v0.2 | v0.4(同步稳定) |
| v0.4 | v0.3 | v1.0(功能完整) |
| v1.0 | v0.4 | v1.x(跨平台) |

---

## 3. v0.1 MVP — 4 周

### 3.1 目标(对应 [MANUAL.md §16.1 v0.1 MVP](./MANUAL.md#161-v01-mvp已交付))

- 本地 Vault 创建 / 解锁 / 锁定
- 条目 CRUD(单 Profile)
- 平台模板 5 个(GitHub / OpenAI / AWS / Stripe / Supabase)
- 剪贴板复制 + 自动清空
- 导入 Bitwarden JSON
- `.okv` 格式第一版

> **v0.1 已交付**(2026-06-22,170/170 测试通过)。实际状态详见 [TEST_REPORT.md §1 / §2](./TEST_REPORT.md)。

### 3.2 Sprint 1(W1-W2):核心密码学与文件格式

#### 3.2.1 任务清单

| ID | 任务 | 负责 | 产出 | 验收标准 | 预估 |
|---|---|---|---|---|---|
| S1-T1 | 项目脚手架 + DI 容器 + Serilog | 全栈 | 解决方案结构 + 6 个项目 | `dotnet build` 通过 + DI 注册完整 | 1d |
| S1-T2 | `ICryptoProvider` 接口 + LibSodiumCryptoProvider 实现 | 后端 | CryptoProvider 单元测试 | INV-01 ~ INV-04 通过(见 [SECURITY.md §10](./SECURITY.md#10-密码学不变量)) | 3d |
| S1-T3 | Argon2id KDF + MK/KEK 派生 + verifyTag | 后端 | KDF 单元测试 | 解锁耗时 ≥500ms + 验证恒定时间 | 2d |
| S1-T4 | XChaCha20-Poly1305 加解密 + DEK 包装 | 后端 | AEAD 单元测试 | INV-02 / INV-08 通过 | 2d |
| S1-T5 | Ed25519 设备密钥对生成 + 签名 / 验证 | 后端 | 签名单元测试 | INV-05 通过 | 1d |
| S1-T6 | `.okv` 头部二进制布局读写器 | 后端 | FormatReader / FormatWriter | FMT-RW-01 / FMT-INTEG-01 通过 | 3d |
| S1-T7 | `IStorageProvider` + 原子写入 + 文件锁 | 后端 | StorageProvider 集成测试 | FMT-RECOV-01 ~ 03 通过 | 2d |
| S1-T8 | `LockService` + `EnsureUnlocked` 模式 | 全栈 | LockService 单元测试 | INV-04 通过 | 1d |

#### 3.2.2 Sprint 1 Demo

- 命令行演示:`okv vault create` → `vault unlock` → 创建 Entry → 保存 → `vault lock`。
- 无 UI,纯 CLI 验证密码学链路。

### 3.3 Sprint 2(W3-W4):UI + 条目 CRUD + 模板 + 导入

#### 3.3.1 任务清单

| ID | 任务 | 负责 | 产出 | 验收标准 | 预估 |
|---|---|---|---|---|---|
| S2-T1 | Avalonia 项目脚手架 + 主窗口布局 | UI | MainWindow.axaml | 主窗口布局符合 [MANUAL.md §10.1](./MANUAL.md#101-总体布局) | 2d |
| S2-T2 | 创建 Vault 向导(6 步) | UI + 全栈 | CreateVaultWizard | UI-AUTO-01 通过 | 2d |
| S2-T3 | 解锁页面 + 主密码输入 | UI | UnlockView | 主密码错误震动 + 5 次锁定 30s | 1d |
| S2-T4 | Recovery Key 生成 + 展示页 | 后端 + UI | RecoveryKeyView | 32 组字符 + 校验位 + 长按 1s 确认 | 1d |
| S2-T5 | `EntryService` + 条目列表 ViewModel | 全栈 | EntryListViewModel | 增删改查 + 版本递增 | 2d |
| S2-T6 | 条目编辑器(字段编辑 + secret 掩码 + 悬停显示) | UI | EntryEditorView | UI-INT-02 通过 | 3d |
| S2-T7 | 5 个平台模板 JSON + 模板加载器 | 全栈 | templates/*.json | 5 模板可创建条目,字段定义符合 [PLATFORM_TEMPLATES.md §3](./PLATFORM_TEMPLATES.md#3-v01-mvp-模板5-个完整-json) | 1d |
| S2-T8 | 剪贴板服务 + 8 秒自动清空 | 全栈 + UI | ClipboardService | UI-INT-01 通过 | 1d |
| S2-T9 | Bitwarden JSON 导入器 | 全栈 | BitwardenImporter | 导入 100 条目无错 | 2d |
| S2-T10 | 状态栏 + 同步状态占位(无 watcher) | UI | StatusBar | 布局符合 [MANUAL.md §10.4](./MANUAL.md#104-状态栏底) | 1d |
| S2-T11 | 单元测试补全 + 集成测试套件 | QA | 测试报告 | 单元 ≥80% 覆盖 + 集成通过 | 2d |

#### 3.3.2 Sprint 2 Demo

- 完整 GUI 演示:创建 Vault → 生成 Recovery Key → 创建 OpenAI 条目(模板)→ 复制 api_key → 8 秒清空 → 导入 Bitwarden JSON → 锁定。
- 性能基准:解锁 ≤1.5s + 1000 条目加载 ≤2s。

### 3.4 v0.1 验收标准

| 类别 | 标准 |
|---|---|
| 功能 | PRD §14 v0.1 全部功能可用 |
| 安全 | INV-01 ~ INV-10 全部通过(见 [SECURITY.md §10](./SECURITY.md#10-密码学不变量)) |
| 格式 | FMT-RW-01 ~ 05 + FMT-INTEG-01 ~ 04 + FMT-RECOV-01 ~ 03 通过 |
| UI | UI-AUTO-01 ~ 02 + UI-INT-01 ~ 02 通过 |
| 性能 | 解锁 ≤1.5s + 启动 ≤2s + 1000 条目加载 ≤2s |
| 文档 | [ARCHITECTURE.md](./ARCHITECTURE.md) / [SECURITY.md](./SECURITY.md) / [OKV_FORMAT.md](./OKV_FORMAT.md) / [MANUAL.md §10](./MANUAL.md#10-主窗口布局) 同步更新 |

### 3.5 v0.1 风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| libsodium 在 .NET 8 的包装兼容性问题 | 中 | Sprint 1 第 1 天做 PoC;若失败切 BouncyCastle |
| Argon2id 256MiB 在低端设备 OOM | 中 | 动态调整:首次解锁测量,根据可用内存调整 |
| Avalonia XAML 复杂控件(掩码文本框)开发耗时 | 中 | Sprint 2 优先核心控件,复杂控件延后 |
| Bitwarden JSON 格式版本差异 | 低 | 支持最新格式 + 向后兼容 1 个版本 |

---

## 4. v0.2 — 4 周

> **v0.2 已交付**(2026-06-23,357/357 测试通过)。实际状态详见 [TEST_REPORT.md §1 / §2](./TEST_REPORT.md)。本节记录实际完成的 Sprint 任务;**GUI 任务(S3-T2 / S4-T5 / S4-T8)在 v0.2 中一并落地**(`src/OmniKeyVault.Cli/Gui/Views/` 24 个 XAML 视图)。

### 4.1 目标(对应 [MANUAL.md §16.2 v0.2](./MANUAL.md#162-v02已交付--多-profile--dev-备份--同步))

- 多 Profile(Prod / Dev / Test)
- Dev 备份 / seed 导出导入
- 文件系统级同步(OneDrive / Dropbox / Syncthing)
- 向量时钟合并
- TOTP 字段
- **GUI 全部主流程落地**(Avalonia 11)

### 4.2 Sprint 3(W5-W6):多 Profile + Dev 备份

#### 4.2.1 任务清单

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 | 预估 |
|---|---|---|---|---|---|---|
| S3-T1 | `ProfileService` + Profile CRUD + DEK 包装 | 后端 | ProfileService | Profile 切换 + DEK 独立 | ✅ 已交付 | 2d |
| S3-T2 | Profile 切换器 UI + banner + 水印 | UI | `ProfileSwitcherWindow.axaml` | UI-INT-03 通过 | ✅ **v0.2 落地** | 2d |
| S3-T3 | seed.okv.dev 格式(OKVD magic)+ 导出 | 后端 | SeedExporter | SEC-T5-01 通过 + strip-secrets | ✅ 已交付 | 2d |
| S3-T4 | seed 导入器 + 强制 dev/test Profile 隔离 | 全栈 | SeedImporter + `SeedImportWindow` | 模态确认 + 红色 banner | ✅ 已交付 | 2d |
| S3-T5 | `BackupService` + snapshot 自动保存(内存版) | 后端 | BackupService | 每次保存生成 snapshot | ✅ 已交付(磁盘版 v0.4) | 2d |
| S3-T6 | TOTP 字段(`totp_uri`)+ 6 位显示 + 倒计时环 | 全栈 + UI | TotpService + `TotpDisplay` | TOTP 正确 + 自动计算可关闭 | ✅ 已交付(31 测试含 6 RFC 6238 向量) | 2d |
| S3-T7 | Profile 设置(同步参与、自动锁定) | 全栈 | ProfileSettings + `SettingsWindow` | dev/test 默认不参与同步 | ✅ 已交付 | 1d |
| S3-T8 | 集成测试 + Profile 隔离测试 | QA | ProfileServiceTests (15) | Profile 间数据不互泄 | ✅ 已交付 | 2d |

#### 4.2.2 Sprint 3 Demo

- 多 Profile 切换演示:创建 dev Profile → 黄色 banner + DEV 水印 → 导入 seed.okv.dev → 切回 prod 不污染。
- TOTP 演示:创建 totp_uri 字段 → 显示 6 位 + 倒计时。

### 4.3 Sprint 4(W7-W8):文件系统同步 + 向量时钟

#### 4.3.1 任务清单

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 | 预估 |
|---|---|---|---|---|---|---|
| S4-T1 | `IWatcherProvider` + FileSystemWatcher 封装 | 后端 | `OSWatcherProvider` | 200ms 去抖 + Channel 序列化 | 🟡 部分(v0.2.1 + GUI 一起交付完整实现) | 2d |
| S4-T2 | manifest.json 读写 + 同步感知 | 后端 | ManifestService | manifest 原子写入 + 签名 | ✅ 已交付 | 1d |
| S4-T3 | VectorClock 实现 + 合并算法 | 后端 | VectorClock | §10.2 规则全部覆盖 | ✅ 已交付(8 新增测试) | 2d |
| S4-T4 | `SyncService` + 冲突检测 + 合并 | 全栈 | SyncService | 2 设备并发写正确合并 | ✅ 已交付 | 3d |
| S4-T5 | 同步冲突解决向导 UI | UI | `SyncConflictResolver.axaml` | UI-AUTO-03 通过 | ✅ **v0.2 落地** | 2d |
| S4-T6 | 同步状态栏 + 设备列表 | UI | `MainWindow` 状态栏 + `sync status` 命令 | UI-INT-04 通过 | ✅ 已交付 | 1d |
| S4-T7 | 崩溃恢复:`.okv.tmp` 检测与清理 | 后端 | RecoveryService | FMT-RECOV-01 ~ 03 通过 | ✅ 已交付(v0.1 既有) | 1d |
| S4-T8 | 多设备签名信任建立 + 未知设备提示 | 后端 + UI | `DeviceTrustDialog` + `manifest.json` 公钥字典 | 新设备签名 → UI 提示信任 | ✅ **v0.2 落地** | 2d |
| S4-T9 | 同步集成测试(2 实例 + 临时目录) | QA | SyncServiceTests (10) | 同步延迟 ≤5s + 冲突正确处理 | ✅ 已交付 | 2d |

#### 4.3.2 Sprint 4 Demo

- 双设备同步演示:笔记本创建条目 → 工作站 5 秒内显示。
- 冲突演示:两设备同时编辑 → 冲突向导解决。

### 4.4 v0.2 验收标准

| 类别 | 标准 | 状态 |
|---|---|---|
| 功能 | [MANUAL.md §16.2 v0.2 全部目标](./MANUAL.md#162-v02已交付--多-profile--dev-备份--同步) | ✅ 通过 |
| 同步 | 2 设备同步延迟 ≤5s + 冲突正确合并 | ✅ 通过(实测 ~1s) |
| 隔离 | dev/test Profile 默认不参与生产同步 + seed 强制隔离 | ✅ 通过 |
| TOTP | 6 位 TOTP ±1 窗口正确 + 自动计算可关闭 | ✅ 通过(31 测试含 RFC 6238) |
| 安全 | SEC-T4-01 / SEC-T5-01 / SEC-T7-01 通过 | ✅ 通过 |
| **GUI 主流程** | **解锁 / 创建 Vault / 编辑条目 / Profile 切换 / 同步冲突 / Dev 备份 / 设备信任** | **✅ v0.2 全部落地** |

---

## 5. v0.3 — 4 周(已交付,2026-06-24)

### 5.1 目标(对应 [MANUAL.md §16.3 v0.3](./MANUAL.md#163-v03已交付--富功能--国际化))

- KDBX 导入(实际落地为 KeePass 2.x XML,见 [INTERNAL.md §2.2 `import --format kdbx-xml`](./INTERNAL.md))
- 全文搜索 + 字段级搜索
- 附件 Blob 加密存储
- 国际化(英文)

### 5.2 Sprint 5(W9-W10):CLI + KDBX

> **状态:全部已交付**。v0.2 已提前交付 CLI 子命令框架;v0.3 落地的 KDBX 实际以 KeePass 2.x XML 形式实现(标准 KeePass "File → Export → KeePass 2 XML" 输出;二进制 KDBX 加密格式需要 KeePassLib 全套密码学栈,留作 v2.x)。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S5-T1 | ~~CLI 框架 + 子命令路由 + 全局选项~~ | ~~全栈~~ | ~~CliApp~~ | ~~CLI-PARSE-01 ~ 04 通过~~ | ✅ v0.2 提前交付 |
| S5-T2 | ~~`vault` 子命令(create / unlock / lock / info / change-password)~~ | ~~全栈~~ | ~~VaultCommands~~ | ~~退出码 0 / 3 / 4 正确~~ | ✅ v0.2 提前交付 |
| S5-T3 | ~~`entry get / set / list / delete` + stdin 读取~~ | ~~全栈~~ | ~~EntryCommands~~ | ~~CLI-SEC-01 / CLI-EXIT-01 通过~~ | ✅ v0.2 提前交付 |
| S5-T4 | ~~stdout 30 秒清零 + 日志脱敏~~ | ~~全栈~~ | ~~SecureOutput~~ | ~~CLI-SEC-02 / CLI-SEC-04 通过~~ | ✅ v0.2 提前交付 |
| S5-T5 | ~~`template list / show / apply`~~ | ~~全栈~~ | ~~TemplateCommands~~ | ~~模板创建条目~~ | ✅ v0.2 提前交付 |
| S5-T6 | KeePass 2.x XML 导入器(`KeePassXmlImporter`)+ GUI 入口(`KeePassImportWindow`) | 后端 | KeePassXmlImporter + GUI 向导 | 导入 100 条目无错 + GUI 向导 | ✅ 已交付(7 tests) |
| S5-T7 | watcher 完整实现(`FileSystemWatcherProvider` + `NoOpSystemEventProvider` 兜底) | 后端 | WatcherProvider | Windows FSW + Linux/macOS 兜底 + 7 tests | ✅ 已交付 |

#### 5.2.1 Sprint 5 Demo(已演示)

- 导入演示:`okv import --format kdbx-xml --input kp-export.xml --profile dev` → 100 条目成功导入。

### 5.3 Sprint 6(W11-W12):全文搜索 + 附件 + 国际化

> **状态:全部已交付**。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S6-T1 | `SearchService` + 倒排索引(在内存中按需计算) | 后端 | SearchService | 1万条目搜索 ≤200ms(实测 1.5ms) | ✅ 已交付(13 tests) |
| S6-T2 | 字段级搜索语法(`tags:dev AND platform:openai AND field:api_key:sk-*`) | 后端 | SearchParser | §5.7 语法支持 | ✅ 已交付 |
| S6-T3 | 搜索 UI + 高亮 + 筛选器(`SearchWindow` + `OKV_GUI_DEMO_SEARCH` demo 入口) | UI | SearchView | 搜索结果布局清晰 + 字段命中元数据 | ✅ 已交付 |
| S6-T4 | 附件 Blob 存储(单文件 + per-blob DEK + Profile KEK 包装) | 后端 | AttachmentService | 1MB Blob 加解密 ≤100ms | ✅ 已交付(10 tests) |
| S6-T5 | `file_ref` 字段 UI + 上传 / 下载(`EditorWindow` 集成) | UI | FileRefField | 上传 / 下载正常 | ✅ 已交付 |
| S6-T6 | en-US 资源文件 + 本地化切换(`EnUsLocalizer` + `UIStrings.SetLocale`) | UI | Resources.en-US | UI-I18N-02 通过(全 UI 英文) | ✅ 已交付(5 tests) |
| S6-T7 | 搜索 / 附件集成测试 | QA | V03GuiFlowTests | 全文搜索 + 附件下载 E2E | ✅ 已交付(4 tests) |

#### 5.3.1 Sprint 6 Demo(已演示)

- 搜索演示:`okv entry search --query "tags:ai AND field:api_key:sk-*"` → 高亮匹配字段。
- 附件演示:上传 PEM 私钥 → 下载还原。
- 英文 UI 演示:`OKV_GUI_DEMO_SETTINGS=1` → 切换到 en-US → 全 UI 英文。

### 5.4 v0.3 验收标准

| 类别 | 标准 | 状态 |
|---|---|---|
| 内部 CLI(已交付) | [INTERNAL.md §3 退出码表](./INTERNAL.md#3-退出码权威表) 全部正确 + §7.1-7.4 安全测试通过 | ✅ 通过(74 v0.2 CLI tests + 14 v1 CLI tests) |
| 搜索 | 1万条目搜索 ≤200ms + 字段级语法支持 | ✅ 通过(实测 1.5ms;SearchServiceTests 13 + V03GuiFlowTests 4) |
| 附件 | 单文件加密 + 上传 / 下载正常 | ✅ 通过(AttachmentServiceTests 10) |
| 国际化 | en-US 全 UI 文案无截断 | ✅ 通过(LocaleTests 5) |
| KeePass XML 导入 | 100 条目无错 | ✅ 通过(KeePassXmlImporterTests 7) |
| Watcher 完整实现 | Windows FSW + 跨平台兜底 | ✅ 通过(WatcherProviderTests 7) |

---

## 6. v0.4 — 4 周(已交付,2026-06-25)

### 6.1 目标(对应 [MANUAL.md §16.4 v0.4](./MANUAL.md#164-v04已交付--一键轮换--历史--性能))

- 自动锁屏 / 空闲定时器(订阅 `SessionSwitch` / `PowerModeChanged`)
- 历史快照查看 + 还原 GUI(`HistoryWindow`)
- 一键轮换(平台 API 集成首批:OpenAI / GitHub PAT)
- 1 万条目性能压测
- 全部 GUI 自动化测试套件(服务层 — 完整 Avalonia headless 留作 v2.x)

### 6.2 Sprint 7(W13-W14):自动锁定 + 历史快照

> **状态:全部已交付**。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S7-T1 | `ISystemEventProvider` + SessionSwitch 订阅(`WindowsSystemEventProvider` + `NoOpSystemEventProvider`) | 后端 | SystemEventProvider | UI-INT-05 通过 | ✅ 已交付 |
| S7-T2 | 空闲检测 + 自动锁定定时器(`IdleTimer`,默认 15 分钟 + `RecordActivity` 重置) | 后端 | IdleTimer | UI-INT-04 通过 | ✅ 已交付(9 tests) |
| S7-T3 | 锁定动作:清零密钥 + 清空缓存 + UI 切换(`SettingsStore.LockOnSessionLock` + `LockOnSuspend`) | 全栈 | LockAction | SEC-T3-01 通过 | ✅ 已交付 |
| S7-T4 | 历史快照查看 UI + 版本列表(`HistoryWindow` + `OKV_GUI_DEMO_HISTORY` demo 入口) | UI | HistoryView | 显示 3 版本列表 | ✅ 已交付 |
| S7-T5 | 历史快照还原 + 版本递增(`BackupService.Restore`) | 后端 | RestoreService | 还原后 version + 1 | ✅ 已交付 |
| S7-T6 | `entry history` CLI 子命令 + `--restore <version>`(v1.0 新增) | 全栈 | HistoryCommand | CLI 退出码 0 / 7 | ✅ 已交付(2 tests) |
| S7-T7 | seed `--strip-secrets` 模式(已 v0.2 交付 CLI;GUI 端:`SeedExportWindow`) | 后端 | StripSecrets | SEC-T5-01 通过 | ✅ 已交付(v0.2 + GUI v0.3 入口) |
| S7-T8 | 自动锁定 + 历史 E2E 测试 | QA | V04GuiFlowTests | UI-INT-04 / 05 + 还原 E2E | ✅ 已交付(3 tests) |

#### 6.2.1 Sprint 7 Demo(已演示)

- 自动锁定演示:15 分钟无操作 → 自动锁定;`SettingsStore.LockOnSessionLock` 系统锁屏立即锁定。
- 历史快照演示:`OKV_GUI_DEMO_HISTORY=1` → 编辑条目 3 次 → 查看历史 → 还原到 v1。

### 6.3 Sprint 8(W15-W16):一键轮换 + v1.0 准备

> **状态:全部已交付**。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S8-T1 | `IPlatformRotator` 接口 + OpenAI rotator | 后端 | OpenAiRotator | rotate 生成新 key + 旧 key 归档 | ✅ 已交付(PlatformRotatorTests) |
| S8-T2 | GitHub PAT rotator | 后端 | GitHubPatRotator | rotate 生成新 PAT + 旧 PAT revoke | ✅ 已交付 |
| S8-T3 | `entry rotate` CLI 子命令(v1.0 新增)+ 新值输出(密文不入 JSON) | 全栈 | RotateCommand | CLI 退出码 0 / 7 / 2 | ✅ 已交付(V1CommandTests) |
| S8-T4 | rotate UI(条目编辑器内 `RefreshRotatePanel` + `OKV_GUI_DEMO_EDITOR` demo 入口) | UI | RotateButton | UI 触发 rotate + 进度显示 | ✅ 已交付 |
| S8-T5 | 性能基准 + 1 万条目压测(`tools/OmniKeyVault.Benchmark`) | QA | 性能报告 | 启动 ≤2s + 搜索 ≤200ms + 同步 ≤5s | ✅ 已交付(全部达标,见下表) |
| S8-T6 | 安全测试清单全量执行(见 [SECURITY.md §11](./SECURITY.md#11-安全审计清单)) | QA | 安全报告 | 全部清单项通过(8/8 落地的不变量) | ✅ 已交付(v0.2 测试报告 + 增量 v0.3/v0.4) |
| S8-T7 | 文档全面校对 + 与代码一致性检查 | Tech Lead | 文档报告 | 9 份文档与实现一致 | ✅ 已交付(本 ROADMAP + 全套文档 v1.0 同步) |
| S8-T8 | v0.4 发布候选(RC)构建 + 内部 dogfood | 全员 | RC 包 | 团队 1 周日常使用无重大问题 | 🟡 v1.0 RC(待外部审计) |

#### 6.3.1 v0.4 性能基准(`tools/OmniKeyVault.Benchmark` 实测)

| 场景 | 实际 | 目标 | 状态 |
|---|---|---|---|
| 创建 1万条目 Vault | 0.7s | ≤ 60s | ✅ |
| 解锁(Argon2id 64 MiB) | 0.1s | ≤ 1.5s | ✅ |
| 全文搜索 1万条目 | 1.5ms | ≤ 200ms | ✅ |
| 2 实例同步 | 0.0s | ≤ 5s | ✅ |

> **注**:Benchmark 使用 `OKV_TEST_MODE=1` 切换到 64 MiB Argon2id(生产 256 MiB);真实解锁时间 = benchmark × ~3.5(实测 ~350ms)。其余场景不依赖 KDF,与生产值一致。

#### 6.3.2 Sprint 8 Demo(已演示)

- 轮换演示:EditorWindow 中 "Rotate" 按钮(仅在 platform_id ∈ {openai, github} 时显示)。
- 性能演示:`dotnet run --project tools/OmniKeyVault.Benchmark` → 4 场景全达标。

### 6.4 v0.4 验收标准

| 类别 | 标准 | 状态 |
|---|---|---|
| 功能 | PRD §14 v0.4 全部功能可用 | ✅ 通过 |
| 自动锁定 | 空闲 15 分钟 + 系统锁屏立即锁定 + 内存清零 | ✅ 通过(IdleTimerTests 9) |
| 历史 | snapshot 查看与还原 + CLI 支持 | ✅ 通过(HistoryWindow + V04GuiFlowTests 3 + V1CommandTests 2) |
| 轮换 | OpenAI / GitHub PAT 一键轮换 + 旧值归档 | ✅ 通过(PlatformRotatorTests 7 + EditorWindow UI 集成) |
| 性能 | PRD §6 全部指标达标 | ✅ 通过(1万条目压测 4/4) |
| 安全 | SECURITY.md §11 清单全通过 | ✅ 通过(8/8 落地不变量 + 增量覆盖) |

---

## 7. v1.0 RC — 4 周(当前进行中,2026-06-25 → 2026-07-15)

> **v1.1 优化前置说明**:v1.0 RC 公开发布需在 v1.1 优化完成后进行。v1.1 优化计划详见 [plan-v1.1-optimization.md](./plan-v1.1-optimization.md)。当前 v1.1 Phase 1-2 已完成,Phase 3 部分完成。

### 7.1 目标(对应 [MANUAL.md §17 v1.0 RC](./MANUAL.md#17-v10-候选下一步--公开发布准备))

- 完整文档(密码学白皮书 + 协议规范 + 威胁模型)
- 安全审计(外部)
- 应用签名 + MSIX 商店分发
- 视频教程
- **当前代码状态**:v1.0 RC 候选(451/451 tests,1万条目性能压测全部达标)

### 7.2 Sprint 9(W17-W18):外部审计 + 文档定稿

> **状态:待开始**。代码 + 单元测试就绪;等待外部审计公司对接。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S9-T1 | 外部安全审计公司对接 + 提供材料(本仓库 + 内部白皮书) | Tech Lead | 审计材料包 | 审计公司接收材料 | ⏸ 待开始 |
| S9-T2 | 审计配合 + 漏洞修复 | 全员 | 修复 PR | Critical / High 漏洞全修复 | ⏸ 待开始 |
| S9-T3 | 密码学白皮书(基于 [SECURITY.md §3-§4](./SECURITY.md) 扩展) | Tech Lead | [CryptoWhitepaper.md](./CryptoWhitepaper.md) | 外部可理解 | ⏸ 待开始 |
| S9-T4 | 同步协议规范独立文档(基于 [ARCHITECTURE.md §10](./ARCHITECTURE.md) 扩展) | 后端 | [SyncProtocol.md](./SyncProtocol.md) | 独立可读 | ⏸ 待开始 |
| S9-T5 | 威胁模型文档独立化(基于 [SECURITY.md §2](./SECURITY.md) 扩展,STRIDE) | Tech Lead | [ThreatModel.md](./ThreatModel.md) | STRIDE 完整覆盖 | ⏸ 待开始 |
| S9-T6 | 用户手册 + 快速入门(从 [MANUAL.md](./MANUAL.md) 提取面向新用户版本) | UI / QA | [UserGuide.md](./UserGuide.md) | 新用户 5 分钟上手 | ⏸ 待开始 |

### 7.3 Sprint 10(W19-W20):签名 + 分发 + 发布

> **状态:待开始**。依赖 Sprint 9 全部完成 + 审计 Critical / High 漏洞全部修复。

| ID | 任务 | 负责 | 产出 | 验收标准 | 状态 |
|---|---|---|---|---|---|
| S10-T1 | EV 代码签名证书申请 + 签名流程(Windows + macOS 公证) | Tech Lead | 签名构建 | SmartScreen 无警告 | ⏸ 待开始 |
| S10-T2 | MSIX 打包 + Microsoft Store 提交 | Tech Lead | MSIX 包 | Store 审核通过 | ⏸ 待开始 |
| S10-T3 | 单文件可执行 + Portable ZIP 构建(基于 [BUILD.md §5](./BUILD.md)) | 全栈 | 发行包 | 3 种形态可下载 | ⏸ 待开始 |
| S10-T4 | 官网 / 下载页 + GitHub Release | Tech Lead | LandingPage | 下载链接可用 | ⏸ 待开始 |
| S10-T5 | 视频教程(5 分钟快速入门 + 10 分钟深度) | UI | 视频脚本 + 录制 | 公开发布 | ⏸ 待开始 |
| S10-T6 | v1.0 公开发布 + 监控漏洞报告渠道 | 全员 | v1.0 Release | 公开发布日 | ⏸ 待开始 |
| S10-T7 | v1.x 规划会议 + 跨平台评估 | Tech Lead | v1.x Plan | 评估报告 | ⏸ 待开始 |

### 7.4 v1.0 验收标准

| 类别 | 标准 | 状态 |
|---|---|---|
| 安全 | 外部审计无 Critical / High 漏洞 | ⏸ 待外部审计 |
| 文档 | 6 份开发文档 + 白皮书 + 用户手册公开 | 🟡 进行中(MANUAL/ROADMAP/ARCHITECTURE/SECURITY/INTERNAL/BUILD/OKV_FORMAT/PLATFORM_TEMPLATES/TEST_REPORT/CHANGELOG 共 10 份就绪) |
| 分发 | MSIX + 单文件 + Portable 三种形态可用 | ⏸ 待开始 |
| 签名 | EV 代码签名 + SmartScreen 无警告 | ⏸ 待开始 |
| 性能 | v0.4 性能标准 + 1 万条目压测 | ✅ 通过(实测全达标) |
| 视频 | 快速入门 + 深度教程发布 | ⏸ 待开始 |

---

## 8. 远期规划(v1.x,需 v1 成功验证后再决定)

### 8.1 候选方向

| 方向 | 价值 | 估计工作量 |
|---|---|---|
| macOS / Linux 客户端 | 扩大用户群 | 12 周(Avalonia 复用 70%) |
| 浏览器扩展(只读) | Web 场景便利 | 6 周 |
| Web 端(只读 + WebAuthn 解锁) | 跨设备便利 | 12 周 |
| 后量子加密升级(OKV2) | 长期安全 | 8 周 |
| 官方托管 E2EE 同步服务 | 降低用户配置成本 | 16 周(含服务端) |
| 团队共享(零知识 + 安全聚合) | 企业市场 | 20 周 |

### 8.2 决策门槛

v1.0 发布后 3 个月,基于以下指标决策 v1.x 优先级:

- 用户数 / 活跃度
- 漏洞报告数量与严重度
- 用户反馈最强烈的功能需求
- 团队资源与资金

---

## 9. 跨里程碑依赖

### 9.1 关键路径

```
S1-T2 (CryptoProvider) ──► S1-T6 (OKV 格式) ──► S2-T5 (EntryService)
                                                    │
                                                    ▼
                                          S3-T1 (ProfileService) ──► S4-T4 (SyncService)
                                                                        │
                                                                        ▼
                                                              S5-T3 (CLI entry) ──► S8-T3 (rotate)
```

- **CryptoProvider 是最高优先级**:S1-T2 延期将连锁影响 S1-T6 / S2-T5 / 后续所有。
- **OKV 格式是第二优先级**:S1-T6 延期将影响 v0.1 Demo 与 v0.2 同步。

### 9.2 跨里程碑共享组件

| 组件 | 首次出现 | 后续使用 |
|---|---|---|
| `ICryptoProvider` | v0.1 S1-T2 | 全部后续 |
| `.okv` 格式读写器 | v0.1 S1-T6 | v0.2(seed) / v0.3(附件) |
| `LockService` | v0.1 S1-T8 | 全部后续 |
| `EntryService` | v0.1 S2-T5 | 全部后续 |
| `ProfileService` | v0.2 S3-T1 | 全部后续 |
| `SyncService` | v0.2 S4-T4 | v0.3+(CLI sync status) |
| `SearchService` | v0.3 S6-T1 | v0.4+(rotate 搜索) |

---

## 10. 风险与缓解

### 10.1 技术风险

| 风险 | 等级 | 影响 | 缓解 |
|---|---|---|---|
| libsodium 在 .NET 8 兼容性 | 中 | v0.1 延期 | Sprint 1 第 1 天 PoC;备选 BouncyCastle |
| Argon2id 256MiB 在低端设备 OOM | 中 | v0.1 用户体验 | 动态调整 + 最低 128MiB 兜底 |
| Avalonia 控件复杂度 | 中 | v0.1 UI 延期 | 优先核心控件,复杂控件延后 |
| 同步冲突合并算法 bug | 高 | v0.2 数据丢失 | 充分单测 + 2 设备 E2E + 备份保留 |
| KDBX 格式版本差异 | 低 | v0.3 导入失败 | 支持主流版本 + 向后兼容 |
| 平台 API 轮换集成 | 中 | v0.4 功能缺失 | 首批仅 OpenAI / GitHub,其他延后 |

### 10.2 安全风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 密码学实现 bug(nonce 重用等) | 严重 | Roslyn 分析器 + 单测覆盖 + 外部审计 |
| 内存未清零 | 高 | INV-03 单测 + 崩溃转储检查 |
| 同步路径泄露明文 | 严重 | INV-09 网络抓包验证 |
| 供应链攻击(NuGet 包篡改) | 高 | 包哈希锁定 + 构建哈希记录 |

### 10.3 进度风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 团队成员流失 | 中 | 知识共享 + 文档完备 |
| 外部审计延期 | 中 | v1.0 预留 4 周缓冲 |
| Microsoft Store 审核延期 | 低 | 同时提供单文件 + Portable |

---

## 11. 团队配置建议

### 11.1 MVP 阶段(v0.1)

| 角色 | 工作量 | 关键技能 |
|---|---|---|
| Tech Lead(架构) | 100% | .NET / 密码学 / Avalonia |
| 后端工程师 | 100% | C# / libsodium / 文件格式 |
| 全栈工程师 | 100% | C# / Avalonia / CLI |
| UI 工程师 | 50% | Avalonia XAML / 交互设计 |
| QA 工程师 | 50% | xUnit / 集成测试 / 安全测试 |

### 11.2 v0.2 - v0.4 阶段

| 角色 | 工作量 |
|---|---|
| Tech Lead | 100% |
| 后端工程师 | 100% |
| 全栈工程师 | 100% |
| UI 工程师 | 100%(v0.2 起全投入) |
| QA 工程师 | 100% |

### 11.3 v1.0 阶段

新增:
- DevOps / 发布工程师(50%,负责签名 / MSIX / Store)
- 技术作家(50%,负责用户手册 / 白皮书)
- 外部审计公司(独立)

---

## 12. 开发环境搭建

### 12.1 必备工具

| 工具 | 版本 | 用途 |
|---|---|---|
| .NET 8 SDK | 8.0.x | 构建 / 运行 |
| Avalonia templates | 11.x | `dotnet new install Avalonia.Templates` |
| libsodium native | 1.0.19+ | 通过 Sodium.Core NuGet 自动拉取 |
| Git | 2.x | 版本控制 |
| Visual Studio 2022 / Rider | 最新 | IDE(可选,可用 VSCode) |

### 12.2 初始化命令

```bash
# 克隆仓库
git clone <repo-url> OmniKeyVault
cd OmniKeyVault

# 安装 Avalonia 模板
dotnet new install Avalonia.Templates

# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行 GUI(主程序,无参数)
dotnet run --project src/OmniKeyVault.Cli

# 运行内部 CLI(集成测试 / CI 入口)
dotnet run --project src/OmniKeyVault.Cli -- vault create --vault ./test.okv

# 运行测试
dotnet test

# 运行性能基准
dotnet run --project tools/OmniKeyVault.Benchmark
```

### 12.3 项目结构(对应 [ARCHITECTURE.md §7.2](./ARCHITECTURE.md#72-解决方案结构))

```
OmniKeyVault.sln
├── src/
│   ├── OmniKeyVault.Domain/
│   ├── OmniKeyVault.Contracts/
│   ├── OmniKeyVault.Application/
│   ├── OmniKeyVault.Infrastructure/
│   ├── OmniKeyVault.UI/
│   └── OmniKeyVault.Cli/
├── tests/
│   ├── OmniKeyVault.Domain.Tests/
│   ├── OmniKeyVault.Application.Tests/
│   ├── OmniKeyVault.Infrastructure.Tests/
│   ├── OmniKeyVault.UI.Tests/
│   └── OmniKeyVault.E2E.Tests/
├── tools/
│   └── OmniKeyVault.Benchmark/
├── templates/           # 平台模板 JSON
└── docs/
```

---

## 13. 编码规范与贡献指南

### 13.1 C# 代码风格

- **命名**:`PascalCase` 类 / 方法 / 属性;`camelCase` 局部变量;`_camelCase` 私有字段。
- **缩进**:4 空格,不混 Tab。
- **花括号**:Allman 风格(开括号新行)。
- **using**:file-scoped(`using ...;` 在文件顶部)。
- **null**:启用 nullable 引用类型(`<Nullable>enable</Nullable>`)。
- **async**:`async` / `await` 全链路,禁用 `.Result` / `.Wait()`。

### 13.2 安全编码实践

- **禁用 `string` 承载敏感数据**(Roslyn 分析器 OKV0001)。
- **`SecureKey` 必须 `using`**(OKV0002)。
- **密钥比较用 `FixedTimeEquals`**(OKV0003)。
- **Service 方法以 `EnsureUnlocked()` 开头**(OKV0004)。
- **禁止 `as any` / `@ts-ignore` 等价物**:C# 中禁用 `unsafe` cast,必要时用模式匹配。

### 13.3 PR 流程

1. **分支命名**:`feature/v0.1/S1-T2-crypto-provider` / `fix/v0.1/argon2-oom`。
2. **Commit 规范**:Conventional Commits(`feat: ...` / `fix: ...` / `docs: ...` / `test: ...` / `refactor: ...` / `chore: ...`)。
3. **PR 描述**:链接任务 ID + 变更摘要 + 测试方式 + 截图(UI 改动)。
4. **Review**:至少 1 人 review,Tech Lead 对安全 / 架构改动必须 review。
5. **CI 检查**:构建 + 单测 + 集成测试 + Roslyn 分析器 + lint 全通过。
6. **合并**:Squash merge,保持主线历史清晰。

### 13.4 测试要求

- **单元测试**:每个公开方法至少 1 个 happy path + 2 个 edge case。
- **集成测试**:每个 Service 至少 1 个端到端用例。
- **安全测试**:每个 INV-* 不变量有对应测试。
- **覆盖率**:核心层(Domain / Application / Infrastructure)≥80%;UI 层 ≥60%。

---

## 14. 附录

### 14.1 Sprint 任务 ID 规则

`S<Sprint 编号>-T<任务编号>`,如 `S1-T2` = Sprint 1 第 2 个任务。

### 14.2 验收标准 ID 规则

- `INV-XX`:密码学不变量(见 [SECURITY.md §10](./SECURITY.md#10-密码学不变量))。
- `FMT-XX-XX`:格式测试(见 [OKV_FORMAT.md §15](./OKV_FORMAT.md#15-测试用例要求))。
- `UI-XX-XX`:UI 测试(见 [MANUAL.md §15.8 + TEST_REPORT.md](./MANUAL.md#158-不变测试))。
- `CLI-XX-XX`:内部 CLI 测试(见 [INTERNAL.md §9](./INTERNAL.md#9-测试用例要求))。
- `SEC-XX-XX`:安全威胁测试(见 [SECURITY.md §2](./SECURITY.md#21-我们防御的威胁))。

### 14.3 修订记录

| 版本 | 日期 | 修订 |
|---|---|---|
| v0.1 | 2026-06-18 | 初稿,覆盖 v0.1 → v1.0 全部 Sprint 任务分解 |
| 0.2 | 2026-06-24 | v0.1 / v0.2 标记为已交付(S3-T2 / S4-T5 / S4-T8 等 GUI 任务在 v0.2 落地);v0.3 Sprint 5 任务重组:CLI 任务(S5-T1 ~ T5)取消(v0.2 提前交付),改为 KDBX + GUI 端到端测试 + watcher 完整实现;§12 命令更新(GUI 为默认启动);cross-ref 全面更新(PRD.md → MANUAL.md,UI_UX_SPEC.md → MANUAL.md,CLI_SPEC.md → INTERNAL.md) |
| **1.0** | **2026-06-25** | **§5 v0.3 + §6 v0.4 全部标记为已交付(73 + 21 新增测试;S6-T1-S6-T7 全部完成,S7-T1-S7-T8 + S8-T1-S8-T7 全部完成;新增 `entry search/rotate/history` + `sync pause/resume` + `config get/set/list` + `import --format kdbx-xml` 等 6 个新内部 CLI 命令 + 9 个新 GUI demo 入口 + 1万条目性能压测工具 `tools/OmniKeyVault.Benchmark`);§7 v1.0 标为"当前进行中"(451/451 tests + 1万条目性能压测就绪;S9-T1/S9-T2 等待外部审计公司对接;S9-T3/S9-T4/S9-T5/S9-T6 文档定稿中);1.2 时间盒表更新 v0.3 + v0.4 实际交付日期;cross-ref 更新 v0.4 → v1.0 RC** |
| **1.1** | **2026-07-07** | **v1.1 优化进行中:** §1.2 时间盒表新增 v1.1 行;§7 新增 v1.1 优化前置说明;当前 Phase 1-2 已完成,Phase 3 部分完成(OKV0001 + OKV0003 分析器);467/467 测试通过(457 + 10 分析器);v1.1 优化计划详见 [plan-v1.1-optimization.md](./plan-v1.1-optimization.md) |
