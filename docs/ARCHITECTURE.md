# OmniKey Vault — 架构设计文档

| 文档版本 | 日期 | 作者 | 状态 |
|---|---|---|---|
| 1.1 | 2026-07-07 | Sisyphus | v1.1 优化进行中:Roslyn 分析器项目落地,467/467 tests |
| 1.0 | 2026-06-25 | Sisyphus | v1.0 RC:GUI + 内部 CLI + Benchmark,14 个 XAML 视图,451/451 tests |

> 关联文档:[MANUAL.md](./MANUAL.md) · [SECURITY.md](./SECURITY.md) · [OKV_FORMAT.md](./OKV_FORMAT.md) · [INTERNAL.md](./INTERNAL.md)

---

## 1. 概述

### 1.1 文档目的

本文档描述 OmniKey Vault 的系统架构,服务于以下读者:

- **核心开发者**:理解模块边界、依赖关系、关键抽象,快速进入实现。
- **代码审查者**:判断变更是否符合架构约束(如 CryptoProvider 不接受字符串)。
- **外部审计人员**:验证架构是否支撑 [MANUAL.md §7 / SECURITY.md §2](./MANUAL.md#7-安全模型) 中的安全不变量。
- **未来贡献者**:在不破坏既有设计的前提下扩展功能(macOS / Linux 客户端、新平台模板等)。

### 1.2 架构目标

| 目标 | 衡量标准 |
|---|---|
| 本地优先 | 核心 CRUD 在无网状态下完成,所有密码学操作在进程内完成 |
| 安全可审计 | 所有密码学操作集中在 CryptoProvider,可被外部审查 |
| 可测试 | 领域模型与基础设施解耦,可在无文件系统 / 无 OS API 环境下测试 |
| 跨平台预备 | UI 层与业务层不耦合 Windows API,为 v1.x macOS / Linux 铺路 |
| 依赖最小化 | 无外部 native deps(libsodium 通过 .NET 包装);单文件可执行可选 |
| **GUI 优先** | **用户面向主程序 = Avalonia 桌面应用(无参数启动)。CLI 模式仅作内部 / CI / 自动化入口。** |

### 1.3 架构原则

1. **密码学集中**:所有加解密通过 `ICryptoProvider` 接口,禁止业务层直接调用 libsodium。
2. **锁定优先**:所有 Service 在锁定状态下第一行抛 `VaultLockedException`,不依赖 UI 层判断。
3. **Span 优先**:敏感数据使用 `ReadOnlySpan<byte>` / `Memory<byte>`,禁止字符串形态在内存中长期残留。
4. **原子写入**:所有持久化操作通过临时文件 + rename,拒绝部分写入状态。
5. **显式依赖**:依赖注入贯穿全栈,禁止 Service 内 `new` 创建依赖。

---

## 2. 系统架构

### 2.1 分层架构图

```
┌──────────────────────────────────────────────────────┐
│  UI 层 (Avalonia 11 MVVM)            ← 用户面向主程序 │
│  ┌────────────────────────────────────────────────┐  │
│  │ Views (XAML)        │ ViewModels               │  │
│  │ MainWindow          │ MainViewModel            │  │
│  │ UnlockWindow        │ EntryEditorViewModel     │  │
│  │ CreateVaultWizard   │ ProfileSwitcherViewModel │  │
│  │ EditorWindow        │ SettingsViewModel        │  │
│  │ SeedImportWindow    │ SyncConflictResolverVm   │  │
│  │ SeedExportWindow    │ RecoveryKeyViewModel     │  │
│  │ HistoryWindow       │ HistoryViewModel         │  │
│  │ SettingsWindow      │ ImportExportViewModel    │  │
│  └────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────┤
│  入口层 (Host)                                       │
│  ┌────────────────────────────────────────────────┐  │
│  │ Program.cs   启动入口:无参数 → GUI;有参数 → CLI│  │
│  │ AvaloniaApp  Avalonia 应用构建器               │  │
│  │ GuiShell     窗口生命周期 + DI 容器管理        │  │
│  │ CliContainer 内部 CLI 模式的 DI 容器           │  │
│  │ CliParser    子命令解析                        │  │
│  └────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────┤
│  应用服务层 (Application Services)                   │
│  ┌────────────────────────────────────────────────┐  │
│  │ VaultService    │ EntryService                 │  │
│  │ ProfileService  │ SyncService                  │  │
│  │ BackupService   │ ClipboardService             │  │
│  │ TotpService     │ LockService                  │  │
│  │ SearchService   │ ImportExportService          │  │
│  │ BitwardenImporter │ SeedExporter              │  │
│  │ SeedImporter    │ ManifestService              │  │
│  │ ProfilePayloadCodec (序列化)                   │  │
│  └────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────┤
│  领域模型层 (Domain)                                 │
│  ┌────────────────────────────────────────────────┐  │
│  │ Vault   │ Profile   │ Entry   │ Field          │  │
│  │ Folder  │ Template  │ VectorClock              │  │
│  │ Argon2Params │ SecureKey │ Crypto/  Sync/     │  │
│  │ Profiles/ Entries/ Fields/ Folders/ Templates/ │  │
│  │ Vaults/ Errors/                                 │  │
│  └────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────┤
│  基础设施层 (Infrastructure)                         │
│  ┌────────────────────────────────────────────────┐  │
│  │ ICryptoProvider   │ IStorageProvider           │  │
│  │ IWatcherProvider  │ ILockProvider              │  │
│  │ IClipboardProvider│ ITotpProvider              │  │
│  │ ISystemEventProvider (SessionSwitch / Idle)    │  │
│  │ IVaultFormat      │ ISeedFormat                │  │
│  │ SodiumCryptoProvider │ VaultFormat             │  │
│  │ SeedFormat │ FileSystemStorageProvider         │  │
│  │ OSWatcherProvider │ OSLockProvider             │  │
│  │ OSClipboardProvider │ OSSystemEventProvider    │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

**入口与分层的关系**:`Program.cs` 是单一入口(`okv.exe`);运行时根据是否有 CLI 参数决定走 GUI(默认)还是 CLI(内部接口)。两条路径共享 `Application` + `Domain` + `Infrastructure` 层;UI 层仅 GUI 使用。详见 §5 进程架构。

### 2.2 各层职责

#### 2.2.1 UI 层(Avalonia 11)
- **职责**:渲染界面、绑定用户输入到 ViewModel、触发 Service 调用、托管窗口生命周期。
- **关键类型**:
  - `MainWindow` — 条目列表 + 详情面板主窗口(v0.1 落地)
  - `UnlockWindow` — 主密码 / Recovery Key 解锁页(v0.1 落地)
  - `CreateVaultWizard` — 6 步创建向导(v0.1 落地)
  - `EditorWindow` — 条目编辑页(v0.1 落地)
  - `ProfileSwitcherWindow` — Profile 切换器(带 banner + 水印,v0.2 落地)
  - `SettingsWindow` — 设置页(v0.2 落地)
  - `SeedImportWindow` / `SeedExportWindow` — 种子导入导出向导(v0.2 落地)
  - `SyncConflictResolver` — 同步冲突解决向导(v0.2 落地)
  - `RecoveryKeyWindow` — Recovery Key 展示(v0.1 落地)
  - `HistoryWindow` — 历史快照查看(v0.4 计划)
  - `DeviceTrustDialog` — 未知设备签名信任确认(v0.2 落地)
- **禁止**:直接访问基础设施;ViewModel 中业务逻辑超过 3 行必须下沉到 Service。
- **测试策略**:ViewModel 可在无 Avalonia 时测试(使用 Mock Service);GUI 端到端通过 Avalonia headless 渲染 + AvaloniaTest 框架(v0.4 引入)。

#### 2.2.2 入口层(Host)
- **职责**:程序入口 + 模式分发(GUI / CLI)+ DI 容器装配 + 窗口生命周期管理。
- **关键类型**:
  - `Program.cs` — 唯一 `Main()`,根据 `args.Length == 0` 决定走 Avalonia 还是 CLI 解析。
  - `AvaloniaApp` — Avalonia 应用构建器(`AppBuilder.Configure<App>().UsePlatformDetect()...`)。
  - `App.axaml(.cs)` — Avalonia Application 类(主题、字体、全局资源)。
  - `GuiShell` — 持有 `CliContainer`(同一组服务)、协调 `UnlockWindow` ↔ `MainWindow` 切换。
- **特征**:单一进程、单一 DI 容器实例;`CliContainer` 共享给 GUI 与 CLI。

#### 2.2.3 应用服务层
- **职责**:编排领域对象、事务边界、锁定状态校验、跨模块协调。
- **特征**:无状态(除 `LockService` 管理解锁窗口);所有写操作方法在锁定状态下抛 `VaultLockedException`。
- **依赖**:只依赖领域模型与基础设施接口,不依赖具体实现。

#### 2.2.4 领域模型层
- **职责**:纯领域逻辑,无 I/O、无副作用。
- **特征**:不可变值对象(`Entry`、`Field`)+ 聚合根(`Vault`、`Profile`)。
- **测试**:纯单元测试,无需 Mock。

#### 2.2.5 基础设施层
- **职责**:封装所有外部交互(文件、密码学、OS API、剪贴板)。
- **特征**:接口在应用层定义,实现在基础设施层(依赖反转)。
- **测试**:集成测试,使用临时目录 + 真实 libsodium。

### 2.3 依赖方向

```
UI ──► Application Services ──► Domain
                │
                ▼
        Infrastructure Interfaces
                ▲
                │
        Infrastructure Implementations
```

- **编译期**:UI 依赖 Services,Services 依赖 Domain 与接口。
- **运行期**:通过 DI 容器注入 Infrastructure 实现。
- **禁止反向依赖**:Domain 不依赖任何上层;Infrastructure 不依赖 Services。

---

## 3. 技术栈选型

### 3.1 推荐技术栈

| 层 | 推荐 | 备选 | 决策 |
|---|---|---|---|
| 运行时 | **.NET 8** | .NET 9 / NativeAOT | LTS + 跨平台 + 成熟 |
| UI 框架 | **Avalonia 11** | WPF / WinUI 3 / Tauri | 跨平台预备 + MVVM 成熟 |
| MVVM 框架 | **ReactiveUI** | CommunityToolkit.Mvvm | 响应式编程契合同步事件流 |
| 密码学 | **Sodium.Core** (libsodium 包装) | BouncyCastle | libsodium API 简洁、抗误用 |
| 持久化 | **.okv 文件 + 嵌入式 SQLite**(索引缓存) | LiteDB | 索引可重建,SQLite 工具链成熟 |
| 同步传输 | **FileSystemWatcher + 任意云盘** | 自托管 sync server | 零运维,符合本地优先理念 |
| DI 容器 | **Microsoft.Extensions.DependencyInjection** | Autofac | 标准化 + 轻量 |
| 日志 | **Serilog** | Microsoft.Extensions.Logging | 结构化日志 + 文件 sink |
| 测试 | **xUnit + FluentAssertions** | NUnit | .NET 主流 + 断言可读 |
| 打包 | **MSIX + 单文件可执行** | Inno Setup | 商店分发 + 自包含双路径 |

### 3.2 关键选型 Rationale

#### 3.2.1 为什么选 Avalonia 而非 WPF

| 维度 | Avalonia 11 | WPF |
|---|---|---|
| 跨平台 | ✅ Windows / macOS / Linux | ❌ 仅 Windows |
| 渲染 | Skia(自绘,一致) | DirectX(平台依赖) |
| XAML 兼容 | 接近 WPF 语法 | — |
| 未来路径 | v1.x 零成本扩展 macOS | 需重写 |
| 生态 | 略小但增长快 | 极成熟 |

**决策**:为了 [MANUAL.md §18 远期规划](./MANUAL.md#18-远期v1x需-v1-成功验证后再决定) 的 macOS / Linux 扩展路径,选择 Avalonia。短期成本:设计师资源略少;长期收益:代码复用。

#### 3.2.2 为什么选 Sodium.Core 而非 BouncyCastle

| 维度 | Sodium.Core | BouncyCastle |
|---|---|---|
| API 设计 | 高层 API,抗误用 | 低层 API,需手动拼装 |
| XChaCha20-Poly1305 | ✅ 原生支持 | ✅ 需手动组合 |
| Argon2id | ✅ | ✅(独立包) |
| 性能 | 原生 libsodium,快 | 托管代码,慢 20-40% |
| 误用风险 | 低(默认安全参数) | 高(需理解参数) |

**决策**:Sodium.Core 的抗误用 API 更契合 [MANUAL.md §7 / SECURITY.md §3](./MANUAL.md#7-安全模型) 的"密码学可审计"目标。BouncyCastle 留作备选,仅在合规场景需要 FIPS 认证时切换。

#### 3.2.3 为什么选 .NET 8 而非 NativeAOT

- .NET 8 LTS 支持至 2026 年 11 月,覆盖 v1.0 发布周期。
- NativeAOT 在 v1.0 后评估:可进一步缩减二进制体积与启动时间,但反射限制可能与 Avalonia 冲突,需 PoC 验证。

---

## 4. 模块划分

### 4.1 UI 层模块(`src/OmniKeyVault.Cli/Gui/`)

> **项目位置说明**:`GUI` 模块物理上位于 `OmniKeyVault.Cli` 项目内(详见 §7 解决方案结构)。这是历史命名;`OmniKeyVault.Cli` 是**唯一入口**项目,内部 `Gui/` 子目录承载 Avalonia 代码,根目录的 `Cli/` 子目录承载内部 CLI 解析器(见 §4.2)。模块依赖关系如下,与项目名称无关。

| 模块 | 职责 | 关键类型 |
|---|---|---|
| `OmniKeyVault.Cli.Gui.Views` | 所有 XAML 视图 + code-behind | `MainWindow`、`UnlockWindow`、`CreateVaultWizard`、`EditorWindow`、`ProfileSwitcherWindow`、`SettingsWindow`、`SeedImportWindow`、`SeedExportWindow`、`SyncConflictResolver`、`RecoveryKeyWindow`、`HistoryWindow`、`DeviceTrustDialog` |
| `OmniKeyVault.Cli.Gui` | GUI 协调 + Avalonia 资源 | `GuiShell`(窗口生命周期 + DI 共享)、`App.axaml(.cs)`、`AvaloniaApp`、`RecoveryKeyRenderer`、`ToastService`、`Res`(资源常量) |
| `OmniKeyVault.Cli` | 入口 + 内部 CLI 解析 | `Program.cs`(分发入口)、`CliParser`(子命令解析)、`CommandHandlers`(CLI 派发)、`CliContainer`(DI 容器) |

> **v0.2 落地状态**:Views/ 下 24 个 XAML 视图 + code-behind 已实现(v0.1:7 个 + v0.2:17 个新增)。详见 [MANUAL.md §17.2](./MANUAL.md#172-v02已交付--多-profile--dev-备份--同步)。

### 4.2 内部 CLI 模块(非用户面向)

> 该模块仅为集成测试 / CI 自动化 / 调试提供入口;**不是产品用户接口**(产品用户接口 = GUI,见 §1.2 架构目标)。详见 [INTERNAL.md](./INTERNAL.md)。

| 模块 | 职责 | 关键类型 |
|---|---|---|
| `OmniKeyVault.Cli.CliParser` | 子命令 + 全局选项解析 | `CliParser`、`CliParseResult` |
| `OmniKeyVault.Cli.CommandHandlers` | CLI 子命令派发 | `CommandHandlers` |
| `OmniKeyVault.Cli.CliContainer` | CLI 模式的 DI 容器(与 GUI 共享 Service 实例) | `CliContainer` |

### 4.3 应用服务层模块(`src/OmniKeyVault.Application/`)

| 服务 | 职责 | 关键方法 | 锁定状态行为 |
|---|---|---|---|
| `VaultService` | Vault 生命周期管理 | `CreateAsync`、`UnlockAsync`、`LockAsync`、`ChangeMasterPasswordAsync` | `UnlockAsync` 不抛,其他抛 |
| `ProfileService` | Profile 切换与管理 | `CreateProfile`、`SwitchProfile`、`DeleteProfile` | 全部抛 `VaultLockedException` |
| `EntryService` | 条目 CRUD | `CreateEntry`、`UpdateEntry`、`DeleteEntry`、`GetEntry`、`ListEntries` | 全部抛 |
| `SyncService` | 同步监测与合并 | `StartWatch`、`StopWatch`、`MergeAsync` | `MergeAsync` 抛,`StartWatch` 不抛 |
| `BackupService` | 快照与种子 | `CreateSnapshot`、`RestoreSnapshot`、`ExportSeed`、`ImportSeed` | 全部抛 |
| `ClipboardService` | 剪贴板安全复制 | `CopySensitiveAsync`(8 秒自动清空) | 全部抛 |
| `TotpService` | TOTP 计算 | `GenerateCode`、`GetRemainingSeconds` | 全部抛 |
| `LockService` | 解锁窗口与自动锁定 | `EnsureUnlocked`、`RegisterActivity`、`StartIdleTimer` | 自身管理锁定状态 |
| `SearchService` | 全文与字段级搜索 | `SearchAsync`、`RebuildIndex` | 全部抛 |
| `ImportExportService` | 跨格式迁移 | `ImportBitwardenAsync`、`ImportKdbxAsync`、`ExportAsync` | 全部抛 |
| `SeedExporter` / `SeedImporter` | Dev 种子格式导出 / 导入 | `ExportAsync`、`ImportAsync` | 全部抛 |
| `BitwardenImporter` | Bitwarden JSON 导入 | `ImportAsync` | 全部抛 |
| `ManifestService` | `manifest.json` 读写 | `ReadAsync`、`WriteAtomicAsync` | 全部抛 |
| `TemplateService` | 平台模板加载 | `LoadFromDirectory`、`ListAll`、`Get` | 无锁定约束 |

### 4.4 领域模型层(`src/OmniKeyVault.Domain/`)

```csharp
// 聚合根
public sealed class Vault {
    public VaultId Id { get; }
    public VaultMetadata Metadata { get; }
    public IReadOnlyDictionary<ProfileName, Profile> Profiles { get; }
    public VectorClock VectorClock { get; }
}

public sealed class Profile {
    public ProfileId Id { get; }
    public ProfileName Name { get; }
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<Folder> Folders { get; }
    // DEK 不在领域模型中,由 CryptoProvider 管理包装形态
}

// 值对象(不可变)
public sealed record Entry(
    EntryId Id,
    EntryType Type,
    EntryName Name,
    PlatformId? PlatformId,
    IReadOnlyList<Tag> Tags,
    FolderId? Folder,
    IReadOnlyList<Field> Fields,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    uint Version
);

public sealed record Field(
    FieldKey Key,
    FieldValue Value,
    FieldKind Kind,
    bool Sensitive,
    FieldMask? Mask,
    FieldValidation? Validation
);
```

### 4.5 基础设施层(`src/OmniKeyVault.Infrastructure/`)

| 接口 | 实现 | 平台特化 |
|---|---|---|
| `ICryptoProvider` | `SodiumCryptoProvider` | 无(libsodium 跨平台) |
| `IStorageProvider` | `FileSystemStorageProvider` | Windows: `MoveFileEx`;macOS/Linux: `rename(2)` |
| `IWatcherProvider` | `OSWatcherProvider` | Windows: `FileSystemWatcher`;macOS: `FSEvents`;Linux: `inotify` |
| `ILockProvider` | `OSLockProvider` | Windows: `Mutex` + lock file;macOS/Linux: `flock(2)` |
| `IClipboardProvider` | `OSClipboardProvider` | Windows: `OpenClipboard` + `SetClipboardData`;macOS: `NSPasteboard`;Linux: `xclip` |
| `ISystemEventProvider` | `OSSystemEventProvider` | Windows: `Microsoft.Win32.SystemEvents.SessionSwitch`;macOS: `NSDistributedNotificationCenter`;Linux: `DBus` |
| `IVaultFormat` | `VaultFormat` | 字节级读写,无平台特化 |
| `ISeedFormat` | `SeedFormat` | 字节级读写,无平台特化 |

---

## 5. 进程架构

### 5.1 单进程模型

v1 为单进程桌面应用,无后台服务、无 IPC 通道。理由:

- **攻击面最小化**:无 IPC = 无跨进程权限提升路径。
- **同步简单**:Vault 状态在进程内,无需跨进程同步。
- **资源节约**:无后台守护进程占用内存。

### 5.2 模式分发(GUI 主 + CLI 内部)

`okv.exe` 是单一可执行文件,根据命令行参数决定模式:

```
okv                              # 启动 GUI(无参数)— 用户面向主程序
okv <subcommand> [...]           # 内部 CLI 模式 — 仅供集成测试 / CI / 调试
okv --help                       # 显示根 help
```

**GUI 模式**(默认,无参数):
- 启动 Avalonia 主循环(`AppBuilder.Configure<App>().UsePlatformDetect()`)。
- 构造 `GuiShell` → 持有 `CliContainer`(与 CLI 共享同一组服务)。
- 启动后显示 `UnlockWindow`(或 `CreateVaultWizard`,若 Vault 不存在)。
- 解锁后切换至 `MainWindow`;用户可手动 `Ctrl+L` 重新锁定 → 回到 `UnlockWindow`。
- 进程退出时由 `LockService` 触发清零,`SyncService` 停止 watcher,Serilog flush。

**内部 CLI 模式**(有参数):
- 不启动 Avalonia 主循环(节省内存 + 避免 GUI 依赖)。
- 复用同一 `CliContainer` 内的服务实例。
- 输出格式:`--format json | text | raw | env | csv`。
- 退出后 30 秒内主动清零 stdout 内存副本。
- **不**承诺长期向后兼容(接口可能随内部重构变化)。详见 [INTERNAL.md](./INTERNAL.md)。

### 5.3 并发模型

- **UI 线程**:Avalonia UI 线程,所有 UI 更新通过 `Dispatcher.UIThread.Post`。
- **Service 调用**:异步 `async/await`,默认在线程池执行,不阻塞 UI。
- **SyncService 后台监听**:`FileSystemWatcher` 事件在 IO 线程触发,经 `Channel<T>` 序列化后由后台任务处理。
- **锁定状态**:`LockService` 内部使用 `SemaphoreSlim` 保护解锁窗口状态,确保并发请求一致。

---

## 6. 关键抽象与接口

### 6.1 ICryptoProvider

```csharp
public interface ICryptoProvider {
    // KDF
    MasterKey DeriveMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args);
    bool VerifyMasterKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, Argon2Params args, ReadOnlySpan<byte> verifyTag);

    // 密钥派生
    KeyEncryptionKey DeriveKek(MasterKey mk, ReadOnlySpan<byte> info);

    // 包装
    WrappedKey WrapKey(KeyEncryptionKey kek, DataEncryptionKey dek);
    DataEncryptionKey UnwrapKey(KeyEncryptionKey kek, WrappedKey wrapped);

    // AEAD 加密
    EncryptedPayload Encrypt(DataEncryptionKey dek, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad);
    byte[] Decrypt(DataEncryptionKey dek, in EncryptedPayload payload, ReadOnlySpan<byte> aad);

    // 签名
    DeviceKeyPair GenerateDeviceKeyPair();
    byte[] Sign(DevicePrivateKey key, ReadOnlySpan<byte> data);
    bool Verify(DevicePublicKey key, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);

    // 内存安全
    void Zero(Span<byte> buffer);
}
```

**约束**:
- 所有方法接受 `ReadOnlySpan<byte>` / `Span<byte>`,不接受 `string`。
- 返回的密钥类型(`MasterKey`、`KeyEncryptionKey`、`DataEncryptionKey`)实现 `IDisposable`,`Dispose` 调用 `CryptographicOperations.ZeroMemory`。
- 实现内部使用 `Sodium.Core`,但接口本身不暴露 libsodium 类型。

### 6.2 IStorageProvider

```csharp
public interface IStorageProvider {
    Task<Stream> OpenReadAsync(VaultPath path, CancellationToken ct);
    Task WriteAtomicAsync(VaultPath path, Func<Stream, Task> writer, CancellationToken ct);
    Task<bool> ExistsAsync(VaultPath path, CancellationToken ct);
    Task DeleteAsync(VaultPath path, CancellationToken ct);
    IAsyncEnumerable<VaultPath> EnumerateAsync(VaultPath dir, CancellationToken ct);
}
```

**约束**:
- `WriteAtomicAsync` 内部:写临时文件 → fsync → rename(MoveFileEx / rename)。
- 路径类型 `VaultPath` 为强类型,禁止裸 `string` 路径拼接(防路径注入)。

### 6.3 IWatcherProvider

```csharp
public interface IWatcherProvider : IDisposable {
    IDisposable Watch(VaultPath dir, WatchFilter filter, Action<WatchEvent> handler);
}

public sealed record WatchEvent(
    WatchEventKind Kind,  // Created / Changed / Deleted / Renamed
    VaultPath Path,
    DateTimeOffset Timestamp
);
```

**约束**:
- 事件去抖:200ms 内多次事件合并为一次。
- 事件经 `Channel<T>` 序列化,避免回调中直接调用 Service。

### 6.4 ILockProvider

```csharp
public interface ILockProvider {
    IDisposable AcquireFileLock(VaultPath lockFile);
    bool IsLockedByOtherProcess(VaultPath lockFile);
}
```

**约束**:
- 文件锁跨进程(Windows: `Mutex` + lock file;macOS/Linux: `flock`)。
- 进程退出时自动释放(`IDisposable` + `SafeHandle`)。

---

## 7. 依赖关系

### 7.1 编译期依赖图

```
OmniKeyVault.Cli  (主入口,唯一可执行项目)
  ├── OmniKeyVault.Application
  │   ├── OmniKeyVault.Domain
  │   └── OmniKeyVault.Contracts (interfaces)
  ├── OmniKeyVault.Infrastructure
  │   ├── OmniKeyVault.Contracts
  │   └── Sodium.Core (NuGet)
  ├── Avalonia 11 (NuGet) — GUI
  └── 内部包含:
      ├── Gui/  — Avalonia MVVM 视图与协调(用户面向)
      └── Cli/  — 内部 CLI 解析器(集成测试 / CI / 调试入口)
```

> **历史命名说明**:`OmniKeyVault.Cli` 是 v0.1 时期的名字(当时只有 CLI);v0.2 把 GUI 合并进来后保留了项目名以避免破坏 CI / 发布脚本。模块物理位置(详见 §4.1)区分:`Cli/Gui/` = 用户面向,`Cli/Cli*` = 内部 CLI。

### 7.2 解决方案结构(v0.2 实际)

```
OmniKeyVault.sln
├── src/
│   ├── OmniKeyVault.Domain/                 # 领域模型 — 纯值对象,无 I/O
│   ├── OmniKeyVault.Contracts/              # 接口契约 — ICryptoProvider、IStorageProvider、IVaultFormat、ISeedFormat
│   ├── OmniKeyVault.Application/            # 应用服务 — VaultService / EntryService / ProfileService / SyncService / SeedExporter 等
│   ├── OmniKeyVault.Infrastructure/         # 实现 — SodiumCryptoProvider / VaultFormat / SeedFormat / FileSystemStorageProvider
│   └── OmniKeyVault.Cli/                    # 入口 — 输出名 `okv`,同时承载 GUI (Gui/) 和内部 CLI (Cli/)
├── tests/
│   ├── OmniKeyVault.Tests/                  # xUnit + FluentAssertions,457 个测试(详见 [TEST_REPORT.md](./TEST_REPORT.md))
│   └── OmniKeyVault.Analyzers.Tests/        # Roslyn 分析器测试,10 个测试(OKV0001 + OKV0003)
├── templates/                                # 内置平台模板 JSON(11 个:5 MVP + 6 v0.2)
├── docs/                                    # MANUAL / ARCHITECTURE / OKV_FORMAT / INTERNAL / SECURITY / PLATFORM_TEMPLATES / TEST_REPORT / BUILD / ROADMAP
├── tools/
│   ├── OmniKeyVault.Benchmark/              # 1万条目性能压测
│   └── OmniKeyVault.Analyzers/              # Roslyn 分析器(OKV0001 + OKV0003,v1.1 Phase 3)
└── publish/                                  # 编译产物(详见 [BUILD.md](./BUILD.md))
    └── win-x64-fd/                          # v0.2 实际框架依赖发布
```

---

## 8. 横切关注点

### 8.1 配置

- **来源**:`%APPDATA%\OmniKeyVault\settings.json`(用户级)+ 命令行参数(覆盖)。
- **加载**:`ISettingsProvider` 接口,启动时一次性加载,变更触发事件。
- **敏感字段**:同步目录路径等非敏感信息存明文;主密码 / Recovery Key 永不落盘。

### 8.2 日志

- **框架**:Serilog,结构化日志。
- **级别**:`Verbose` / `Debug` / `Information` / `Warning` / `Error`。
- **输出**:文件 `%LOCALAPPDATA%\OmniKeyVault\logs\okv-{date}.log`,滚动 7 天。
- **脱敏**:日志上下文禁止包含明文凭据、密钥、主密码;`LogContext` 推送 `VaultId` / `DeviceId` 即可。
- **崩溃**:未处理异常过滤器在写日志后主动清零敏感内存并吞掉转储(见 [SECURITY.md §6](./SECURITY.md#6-内存安全))。

### 8.3 错误处理

| 错误类型 | 基类 | 处理策略 |
|---|---|---|
| 锁定状态调用 | `VaultLockedException` | UI 提示解锁;CLI 退出码 3 |
| 密码学失败 | `CryptoException` | 不暴露内部差异,统一提示"解密失败";退出码 4 |
| 同步冲突 | `SyncConflictException` | UI 弹冲突解决向导;CLI 退出码 5 |
| 文件 I/O | `StorageException` | 重试 3 次后告警;退出码 6 |
| 参数校验 | `ValidationException` | UI 内联错误;CLI 退出码 2 |
| 其他 | `UnexpectedException` | 日志 + 用户提示"内部错误";退出码 1 |

### 8.4 国际化

- **框架**:.NET 资源文件(`.resx`)+ `IStringLocalizer`。
- **v1 范围**:`zh-CN` / `en-US`。
- **不变量**:模板 ID、字段 key、CLI 子命令始终英文;仅 UI 文案与错误消息本地化。

### 8.5 生命周期

```
应用启动
  ├── 加载 settings.json
  ├── 初始化 DI 容器
  ├── 注册全局异常过滤器
  ├── (GUI) 启动 Avalonia 主循环
  └── (CLI) 解析子命令 → 执行 → 退出

应用退出
  ├── LockService 触发锁定(清零 MK/KEK/DEK)
  ├── SyncService 停止 watcher
  ├── Serilog flush
  └── 进程退出
```

---

## 9. 部署架构

### 9.1 安装包形态(v1 计划)

> **v0.2 实际状态**:`okv` 二进制以**框架依赖(FDD)**形态发布,目标 RID = `win-x64`(详见 [BUILD.md §5](./BUILD.md#5-打包--发布-publishing--packaging))。v1.0 落地以下三种分发形态。

| 形态 | 用途 | 大小估计 |
|---|---|---|
| MSIX | Microsoft Store 分发 / 企业 SCCM | ~45 MB |
| 单文件可执行(self-contained) | 官网下载,无需 .NET Runtime | ~75 MB |
| Portable ZIP | U 盘携带,无需安装 | ~75 MB |

### 9.2 文件布局(已安装)

```
%LOCALAPPDATA%\Programs\OmniKeyVault\
├── okv.exe                       # 主可执行文件(Windows;Linux/macOS 为 okv)
├── native\libsodium.dll          # 或对应平台 so/dylib
├── Avalonia\.{Themes,Fonts}\     # 主题与字体
├── assets\                       # 图标、字体
└── templates\                    # 内置平台模板(11 个 JSON)

%APPDATA%\OmniKeyVault\
├── settings.json                 # 用户设置(明文,无敏感)
├── last-vault.txt                # 上次打开的 Vault 路径标记
├── vaults\<vault-uuid>\
│   ├── device_key                # 本设备 Ed25519 私钥(v0.2 明文,KEK 包装 v1.0 落地)
│   └── ...                       # 未来 per-vault 元数据
└── logs\

%USERPROFILE%\OmniKeyVault\       # 默认同步目录(可改)
├── vault.okv
├── manifest.json
└── .okv.lock
```

### 9.3 自包含 vs 框架依赖

- **默认**:自包含部署(SCD),内嵌 .NET Runtime + Avalonia 资源,用户无需安装。
- **企业**:可提供框架依赖版本(FDD),由 IT 统一部署 .NET 8 Desktop Runtime。
- **NativeAOT**:v1.0 后评估,目标二进制 < 30 MB;需 Avalonia headless / 预编译兼容性验证。

### 9.4 GUI vs CLI 部署差异

- **GUI 模式**(`okv`,无参数):需要 Avalonia 11 + 主题 + 字体 + Windows 视觉子系统支持(Win32 / X11 / Wayland / macOS Cocoa)。**不**支持 Server Core / 无头 Windows Server。
- **内部 CLI 模式**(`okv <subcommand>`):可运行于任意有 .NET 8 Runtime 的环境,包括无头 Linux / Windows Server Core / Docker(详见 [BUILD.md §6 容器化](./BUILD.md#6-容器化可选))。

---

## 10. 架构决策记录 (ADR)

### ADR-001:选择 Avalonia 11 作为 UI 框架

- **状态**:Accepted
- **日期**:2026-06-17
- **背景**:v1 仅 Windows,但 v1.x 计划 macOS / Linux。WPF 仅 Windows;WinUI 3 限制更多;Tauri 生态小且 Rust 学习曲线高。
- **决策**:选择 Avalonia 11。
- **代价**:设计师资源略少;某些控件需自定义。
- **后续**:v1.x 扩展时验证 macOS / Linux 渲染一致性。

### ADR-002:使用 Sodium.Core(libsodium 包装)

- **状态**:Accepted
- **日期**:2026-06-17
- **背景**:需要 XChaCha20-Poly1305 + Argon2id + Ed25519。BouncyCastle 全托管但 API 低层、易误用;Sodium.Core 包装 libsodium,高层 API 抗误用。
- **决策**:使用 Sodium.Core。
- **代价**:引入 native 依赖 libsodium;但通过 .NET 包装,部署简单。
- **后续**:若未来需 FIPS 认证,评估 BouncyCastle FIPS 分支。

### ADR-003:自定义 .okv 信封格式而非复用 KDBX

- **状态**:Accepted
- **日期**:2026-06-17
- **背景**:[MANUAL.md §1.4](./MANUAL.md#14-核心价值主张) 要求"通用工具不识别"。KDBX4 被 hashcat mode 13400 专门支持;Bitwarden 格式公开。
- **决策**:自定义 `OKV1` magic + 自定义字段布局 + 非默认 Argon2id 参数。
- **代价**:无现成工具支持;但这是设计目标。
- **后续**:每年小幅迭代字段布局,提高逆向成本。

### ADR-004:文件系统同步而非自建协议

- **状态**:Accepted
- **日期**:2026-06-17
- **背景**:[MANUAL.md §4.4](./MANUAL.md#44-多设备同步) 要求零运维。自建同步协议需服务端,违反"本地优先"。
- **决策**:复用任意文件同步工具(OneDrive / Syncthing / rsync / git-annex)。
- **代价**:失去实时双向;但开发者用户更重视零运维。
- **后续**:v1.x 评估是否提供官方托管 E2EE 同步作为可选项。

### ADR-005:领域模型不持有密钥对象

- **状态**:Accepted
- **日期**:2026-06-18
- **背景**:Entry / Profile 等领域对象需要可序列化、可测试。若持有 DEK,则领域模型依赖 `ICryptoProvider`,且密钥生命周期与对象生命周期耦合,易泄漏。
- **决策**:领域模型只持有明文数据与元数据;DEK / KEK / MK 由 `LockService` + `CryptoProvider` 管理,仅在加解密边界短暂使用。
- **代价**:Service 层需显式传递 DEK;但这让密钥使用点可审计。

### ADR-006:单一可执行文件分发,GUI 与内部 CLI 共享二进制

- **状态**:Accepted
- **日期**:2026-06-18(2026-06-24 重新定位:CLI 改为内部接口)
- **背景**:v0.1 阶段只有 CLI;v0.2 引入 GUI 后,为避免双二进制分发(MSIX 包 + 版本管理 + 签名复杂度),选择共享同一 `okv.exe`。无参数 = GUI(主程序),有参数 = 内部 CLI(仅供集成测试 / CI / 调试,**不**作为产品用户接口)。
- **决策**:同一 `okv.exe` 通过 `args.Length == 0` 分发 GUI / CLI 模式。GUI 模式下不启动 Avalonia 主循环不命中,CLI 模式跳过 Avalonia 启动。详见 §5.2。
- **代价**:
  - 二进制包含 Avalonia 11(~30MB 依赖),即使是 CLI 用户也要下载完整 GUI;
  - **接受**:CLI 不是用户面向,集成测试 / CI 流水线下载完整包是合理的。
- **后续**:
  - 若 v1.x 出现"仅 CLI 需求"的边缘场景(嵌入式 / Docker 镜像最小化),评估构建 `OmniKeyVault.Cli.Core` 子集(剥离 Avalonia + ViewModel)。**当前不计划。**
  - v0.2 已落地(`okv.exe` 同时包含 `Cli/Gui/` + `Cli/Cli*` 命名空间,DI 容器 `CliContainer` 共享)。

---

## 11. 跨平台预备

虽然 v1 仅 Windows,以下设计为 v1.x 铺路:

| 维度 | Windows 特化 | macOS / Linux 适配点 |
|---|---|---|
| 文件锁 | `Mutex` + lock file | `flock` |
| 原子写入 | `MoveFileEx(REPLACE_EXISTING)` | `rename(2)` |
| 文件监听 | `FileSystemWatcher` | `FSEvents` / `inotify` |
| 剪贴板 | `OpenClipboard` + `SetClipboardData` | `NSPasteboard` / `xclip` |
| 系统事件 | `Microsoft.Win32.SystemEvents` | `NSDistributedNotificationCenter` / `DBus` |
| CSPRNG | `BCryptGenRandom` | `getrandom(2)` |
| 路径布局 | `%APPDATA%` / `%LOCALAPPDATA%` | `~/.config` / `~/.local/share` |
| 代码签名 | Authenticode | notarization / gpg |

**约束**:所有平台差异封装在 `I*Provider` 实现内,业务层无 `#if WINDOWS`。

---

## 12. 可测试性设计

### 12.1 测试金字塔

```
       ┌──────────────┐
       │   E2E (5%)   │  真实文件系统 + 真实 libsodium + 真实 Avalonia
       └──────────────┘
     ┌──────────────────┐
     │ Integration (25%)│  真实文件系统 / 真实 libsodium,无 UI
     └──────────────────┘
   ┌──────────────────────┐
   │     Unit (70%)        │  纯领域逻辑 + Mock Service
   └──────────────────────┘
```

### 12.2 测试夹具

- **`VaultFactory`**:构造测试用 Vault 内存对象,各种边界(空、满、含特殊字符)。
- **`CryptoProviderStub`**:确定性密钥派生(固定 seed),用于可重现的加密测试。
- **`TempVaultDir`**:xUnit fixture,每个测试用例独立临时目录,用完清理。
- **`SeedData`**:5 个 MVP 平台模板的种子 Entry 集,用于 UI 截图测试与 E2E。

### 12.3 关键测试场景

| 场景 | 层级 | 验证点 |
|---|---|---|
| 主密码解锁 | Integration | Argon2id 耗时 ≥500ms;验证 tag 正确 |
| 锁定后 Service 调用 | Unit | 全部抛 `VaultLockedException` |
| 原子写入崩溃恢复 | Integration | 模拟 rename 前崩溃,重启后 `.okv.tmp` 清理 |
| 同步冲突合并 | Integration | 两设备并发写,向量时钟正确合并 |
| 剪贴板 8 秒清空 | Integration | 复制后 8s 剪贴板为空 |
| 内存清零 | Unit | `MasterKey.Dispose` 后内存全 0 |

详见 [ROADMAP.md](./ROADMAP.md) 各里程碑的验收标准。

---

## 13. 性能与容量

### 13.1 性能目标(对应 [MANUAL.md §5 非功能需求](./MANUAL.md#5-非功能需求))

| 操作 | 目标 | 测量方法 |
|---|---|---|
| 冷启动 | ≤2s | 1000 条目 Vault,SATA SSD |
| 解锁 | ≤1.5s | Argon2id 256MB |
| 条目保存 | ≤100ms | 含加密 + 原子写入 |
| 全文搜索 | ≤200ms | 1000 条目 |
| 同步检测 → UI 更新 | ≤5s | 假设云盘无延迟 |

### 13.2 容量规划

| 指标 | v1 目标 | 设计上限 |
|---|---|---|
| 条目数 | 10,000 | 100,000(流式加载) |
| Profile 数 | 8 | 64 |
| 单条目字段数 | 20 | 256 |
| 附件单文件 | 10 MB | 100 MB(分块) |
| Vault 文件大小 | ~5 MB(1 万条目) | ~50 MB |

### 13.3 内存预算

- 空闲:≤150 MB(不含 .NET Runtime)
- 1 万条目全加载:≤500 MB(流式按需解密)
- MK / KEK / DEK 常驻:~128 字节,可忽略

---

## 14. 附录

### 14.1 术语表

参见 [MANUAL.md 附录 A](./MANUAL.md#附录-a--术语表)。

### 14.2 参考资料

- Avalonia 11 文档:https://docs.avaloniaui.net/
- libsodium 文档:https://libsodium.gitbook.io/doc/
- Sodium.Core:https://github.com/tabrath/libsodium-core
- ReactiveUI:https://www.reactiveui.net/
- Microsoft.Extensions.DependencyInjection:https://learn.microsoft.com/dotnet/core/extensions/dependency-injection

### 14.3 修订记录

| 版本 | 日期 | 修订 |
|---|---|---|
| v0.1 | 2026-06-18 | 初稿,覆盖 MVP 架构决策 |
| 0.2 | 2026-06-24 | 适配 v0.2 实际状态:GUI 已落地(`src/OmniKeyVault.Cli/Gui/Views/` 24 个 XAML);CLI 重新定位为内部接口(详见 [INTERNAL.md](./INTERNAL.md));二进制名 `OmniKeyVault.exe` → `okv.exe`;cross-ref 全面更新(PRD.md → MANUAL.md);新增 §2.2.2 入口层 / §4.1 UI 模块清单 / §4.2 内部 CLI 模块;ADR-006 重新表述 |
| 1.0 | 2026-06-25 | v1.0 RC: 14 个 XAML 视图(原 24 个计数有误,实际为 14 个文件 / 14 个 code-behind);§2.2.2 入口层新增 9 个 `OKV_GUI_DEMO_*` demo 入口说明;§2.3 服务层新增 v0.3 + v0.4 服务(SearchService / AttachmentService / KeePassXmlImporter / IdleTimer / OpenAiRotator / GitHubPatRotator);§4.1 UI 模块清单更新(14 视图 + 9 demo 入口);§7.2 解决方案结构加入 `tools/OmniKeyVault.Benchmark/`;cross-ref 更新指向 v1.0 各文档 |
| **1.1** | **2026-07-07** | **v1.1 优化进行中:** §7.2 解决方案结构加入 `tools/OmniKeyVault.Analyzers/`(Roslyn 分析器)+ `tests/OmniKeyVault.Analyzers.Tests/`;`Directory.Build.props` 版本升至 1.1.0 + 分析器注入所有 src/ 项目;测试数 357 → 467(457 单元/集成 + 10 分析器);v1.1 优化计划详见 [plan-v1.1-optimization.md](./plan-v1.1-optimization.md) |
