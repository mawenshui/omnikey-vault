# OmniKey Vault — 编译、运行与打包指南 (Build, Run & Package Guide)

| 文档版本 | 日期 | 作者 | 状态 |
|---|---|---|---|
| 1.3 | 2026-07-10 | Sisyphus | v1.5 发布:创建向导 WebDAV 拉取 + 404 修复,561/561 tests |
| 1.2 | 2026-07-10 | Sisyphus | v1.2 发布:WebDAV 云同步 + 跨设备同步修复,557/557 tests |
| 1.1 | 2026-07-07 | Sisyphus | v1.1 优化进行中:分析器项目落地,467/467 tests |
| 1.0 | 2026-06-25 | Sisyphus | v1.0 RC:GUI 主形态 + Benchmark 工具(1万条目) |

> 本文档面向开发者、CI 维护者和发布工程师,覆盖 OmniKey Vault(`okv.exe` / `okv`)从源码到分发的完整生命周期。**用户面向入口是 GUI 桌面应用**(`okv` 无参数启动);**内部 / CI 自动化入口是 CLI**(`okv <subcommand>`,详见 [INTERNAL.md](./INTERNAL.md))。所有命令在 Windows / Linux / macOS 上等价(以 PowerShell 与 Bash 双向给出)。

---

## 1. 仓库结构 (Repository Layout)

```
OmniKeyVault.sln                     # 解决方案入口
Directory.Build.props                # 全局 MSBuild 属性(    <Version>1.4.0</Version>、TreatWarningsAsErrors=true、Roslyn 分析器注入、SourceLink)

src/
├── OmniKeyVault.Domain/             # 领域模型 — 纯值对象,无 I/O
├── OmniKeyVault.Contracts/          # 接口契约 — ICryptoProvider、IStorageProvider、IVaultFormat、ISeedFormat
├── OmniKeyVault.Application/        # 应用服务 — VaultService、ProfileService、EntryService、SyncService、SeedExporter 等
├── OmniKeyVault.Infrastructure/     # 实现 — SodiumCryptoProvider、VaultFormat、SeedFormat、FileSystemStorageProvider
└── OmniKeyVault.Cli/                # 唯一入口项目 — 输出名 `okv`
                                     #   ├─ Gui/  — Avalonia 11 MVVM 视图(用户面向主程序)
                                     #   └─ Cli/  — 内部 CLI 解析器(集成测试 / CI 入口)

tests/
└── OmniKeyVault.Tests/              # xUnit + FluentAssertions,457 个测试(详见 [TEST_REPORT.md](TEST_REPORT.md))

    OmniKeyVault.Analyzers.Tests/     # Roslyn 分析器测试,10 个测试(OKV0001 + OKV0003)

tools/
├── OmniKeyVault.Benchmark/          # 1万条目性能压测(v0.4 S8-T5;输出 create/unlock/search/sync 4 场景)
└── OmniKeyVault.Analyzers/          # Roslyn 分析器(OKV0001 禁止 string 密码参数 + OKV0003 禁止 == 比较密钥)

templates/                            # 内置平台模板 JSON(11 个:5 MVP + 6 v0.2)
docs/                                 # MANUAL / ROADMAP / ARCHITECTURE / OKV_FORMAT / INTERNAL / SECURITY / PLATFORM_TEMPLATES / TEST_REPORT / CHANGELOG / 本文档
└── UI/                              # HTML 静态视觉参考(原型)
```

> **没有 .gitignore / build 脚本 / CI 配置**:这是 v0.2 的有意简化(v0.2.1 引入 GitHub Actions + Directory.Packages.props + .gitignore)。见 §9 偏差登记。

---

## 2. 先决条件 (Prerequisites)

### 2.1 运行时依赖

| 依赖 | 最低版本 | 用途 |
|---|---|---|
| **.NET SDK** | `8.0.100`(LTS) | 构建、测试、发布 |
| **.NET Runtime** | `8.0.0` | 框架依赖部署的运行要求;自包含部署无需 |
| **OS** | Windows 10+ / Linux glibc 2.31+ / macOS 12+ | 跨平台 libsodium 已被 Sodium.Core 包覆盖 |

### 2.2 验证安装

```bash
# .NET SDK
dotnet --list-sdks
# 期望输出包含 8.0.x 一行(也允许更高 8.0 补丁版本共存)

# .NET 运行时(框架依赖部署所需)
dotnet --list-runtimes
# 期望 Microsoft.NETCore.App 8.0.x 至少 1 行
```

> 兼容矩阵:虽未在 .NET 9/10 上声明支持,但 net8.0 TFM 在更高 SDK 上**应**可继续构建。**生产发布请固定 8.0.x SDK**。

---

## 3. 还原、构建与测试 (Restore, Build, Test)

### 3.1 一次性还原

```bash
# Bash / PowerShell Core / pwsh
dotnet restore OmniKeyVault.sln
```

.NET 8 SDK 的 MSBuild 在后续 `build` / `test` / `publish` 命令中会按需自动还原,通常可跳过此步。

### 3.2 编译

```bash
# Debug(默认)
dotnet build OmniKeyVault.sln -c Debug

# Release
dotnet build OmniKeyVault.sln -c Release
```

**预期输出**:

```
已成功生成。
    0 个警告
    0 个错误
```

> 关键约束:[Directory.Build.props](Directory.Build.props) 设置 `TreatWarningsAsErrors=true`,**任何 warning 都导致构建失败**。如果看到 `0 warnings, 0 errors` 之外的数量,先定位并修复,不要禁用此标志。

### 3.3 运行测试

```bash
# 全部 467 个测试(457 + 10 分析器)
dotnet test

# 仅运行 v0.2 + v0.3 + v0.4 新增测试组(281 个)
dotnet test tests/OmniKeyVault.Tests/OmniKeyVault.Tests.csproj -c Debug --filter "
  FullyQualifiedName~TotpServiceTests|
  FullyQualifiedName~ProfileServiceTests|
  FullyQualifiedName~BackupServiceTests|
  FullyQualifiedName~SeedFormatTests|
  FullyQualifiedName~SeedImportExportTests|
  FullyQualifiedName~SyncServiceTests|
  FullyQualifiedName~ManifestServiceTests|
  FullyQualifiedName~V2CommandTests|
  FullyQualifiedName~V1CommandTests|
  FullyQualifiedName~V02GuiFlowTests|
  FullyQualifiedName~V03GuiFlowTests|
  FullyQualifiedName~V04GuiFlowTests|
  FullyQualifiedName~SearchServiceTests|
  FullyQualifiedName~AttachmentServiceTests|
  FullyQualifiedName~KeePassXmlImporterTests|
  FullyQualifiedName~LocaleTests|
  FullyQualifiedName~IdleTimerTests|
  FullyQualifiedName~PlatformRotatorTests|
  FullyQualifiedName~WatcherProviderTests
"

# 详细输出
dotnet test tests/OmniKeyVault.Tests/OmniKeyVault.Tests.csproj -c Debug --logger "console;verbosity=normal"
```

**预期输出**:

```
通过! - 失败: 0, 通过: 457, 已跳过: 0, 总计: 457  # OmniKeyVault.Tests
通过! - 失败: 0, 通过: 10, 已跳过: 0, 总计: 10    # OmniKeyVault.Analyzers.Tests
```

### 3.4 跑 1万条目性能压测(v0.4 S8-T5)

```bash
# 全部 4 场景压测(默认 10000 条目)
dotnet run --project tools/OmniKeyVault.Benchmark -c Release

# 调整条目数(更快迭代)
dotnet run --project tools/OmniKeyVault.Benchmark -c Release -- 5000
```

**预期输出**:

```
=== Summary ===
Scenario                Actual        Target      Unit    Status
create_vault            0.7           60.0        s       ✓ OK
unlock                  0.1           1.5         s       ✓ OK
search                  1.5           200.0       ms      ✓ OK
sync (NoChange)         0.0           5.0         s       ✓ OK

✓ ALL OK — performance budget met
```

耗时约 17-19 秒(测试模式 Argon2id `t=3, m=32 MiB`)。详细覆盖见 [TEST_REPORT.md](TEST_REPORT.md)。

### 3.5 测试环境变量

测试套件使用 `OKV_TEST_MODE=1` 切换到弱化 Argon2id 参数(从 256 MiB 降到 32 MiB),大幅缩短执行时间。**生产代码路径不会读取此变量**。

```bash
# Linux / macOS
OKV_TEST_MODE=1 OKV_MASTER_PASSWORD=my-pw dotnet test ...

# PowerShell
$env:OKV_TEST_MODE = "1"; $env:OKV_MASTER_PASSWORD = "my-pw"; dotnet test ...
```

---

## 4. 本地运行 (Local Run)

> `okv.exe` 是单一可执行文件;无参数 = GUI 主程序,有参数 = 内部 CLI(详见 [INTERNAL.md](./INTERNAL.md))。

### 4.1 GUI 模式(主程序,无参数)

```bash
# 启动 Avalonia 桌面应用(默认主程序)
dotnet run --project src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj

# 等价于编译后直接运行产物
./src/OmniKeyVault.Cli/bin/Debug/net8.0/okv          # Linux / macOS
.\src\OmniKeyVault.Cli\bin\Debug\net8.0\okv.exe      # Windows PowerShell
```

### 4.2 内部 CLI 模式(集成测试 / CI / 调试)

```bash
# 传 CLI 参数
dotnet run --project src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj -- vault --help
dotnet run --project src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj -- version
```

`--` 是必需的:`dotnet run` 用它把后续参数转发给程序(而非 dotnet 自身)。CLI 命令参考见 [INTERNAL.md §5](./INTERNAL.md#5-关键命令速查)。

`--` 是必需的:`dotnet run` 用它把后续参数转发给程序(而非 dotnet 自身)。

### 4.2 直接调用编译产物

构建后,`okv` 二进制位于 `src/OmniKeyVault.Cli/bin/<Config>/net8.0/`。

```bash
# Linux / macOS
./src/OmniKeyVault.Cli/bin/Debug/net8.0/okv version

# Windows PowerShell
.\src\OmniKeyVault.Cli\bin\Debug\net8.0\okv.exe version
```

**Windows-only 注意**:`okv.exe` 是 Windows 平台 apphost;Linux/macOS 上同一路径是 ELF/Mach-O 可执行,文件名仍叫 `okv`(无扩展名)。Sodium.Core NuGet 包会自动选择匹配的 `runtimes/<rid>/native/libsodium.*`,无需手动配置。

### 4.3 端到端快速烟测 (Smoke Test)

```bash
# 准备一个临时 vault 并跑一个完整流程
$env:OKV_MASTER_PASSWORD = "smoke-test-123"  # PowerShell
# export OKV_MASTER_PASSWORD="smoke-test-123"  # Bash

okv vault create --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD
okv profile create --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD --name dev --color yellow
okv entry set --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD --profile dev --name "smoke-entry" --template openai
okv entry list --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD --profile dev
okv export --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD --output $HOME/seed.okv.dev --format okv-dev --source-profile dev
okv import --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD --input $HOME/seed.okv.dev --format okv-dev --profile dev
okv sync status --vault $HOME/okv-smoke.okv --password-env OKV_MASTER_PASSWORD

# 清理
rm $HOME/okv-smoke.okv $HOME/seed.okv.dev
```

预期每个命令以退出码 0 返回并输出对应结果。

---

## 5. 打包 / 发布 (Publishing & Packaging)

CLI 项目的可执行入口是 `OmniKeyVault.Cli`(`<AssemblyName>okv</AssemblyName>`,`<OutputType>Exe</OutputType>`)。本节描述 4 种发布形态,按推荐顺序排列。

### 5.1 框架依赖 ZIP(默认,推荐 CI 分发)

> 目标机器需要预装 .NET 8 Desktop / Core Runtime。**最小体积**(~280 KB)。

```bash
# Windows x64
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r win-x64 --self-contained false \
  -o ./publish/win-x64-fd

# Linux x64
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r linux-x64 --self-contained false \
  -o ./publish/linux-x64-fd

# macOS x64 (Intel)
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r osx-x64 --self-contained false \
  -o ./publish/osx-x64-fd

# macOS Apple Silicon
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r osx-arm64 --self-contained false \
  -o ./publish/osx-arm64-fd
```

**产物**(`./publish/win-x64-fd/`):

```
okv.exe                          # 151 KB — Windows 启动器(实际主体在 okv.dll)
okv.dll                          # 84 KB  — 程序集
okv.runtimeconfig.json           # 运行时配置(需要 .NET 8)
okv.deps.json                    # 依赖图
libsodium.dll                    # 312 KB — 本地 libsodium(Sodium.Core native)
OmniKeyVault.{Domain,Contracts,Application,Infrastructure}.dll
Sodium.Core.dll                  # 49 KB
templates/                       # 11 个 JSON 模板(github / openai / aws_iam_long_term / aws_sts_temporary /
                                 #  aliyun_ram_user / anthropic / aws_sts_temporary / azure_service_principal /
                                 #  gcp_service_account / slack / stripe / supabase)
```

**校验**:
```bash
# Windows
./publish/win-x64-fd/okv.exe version
# 期望:OmniKey Vault v0.2.0 / Build: xxxxxxxx / Runtime: .NET 8.0.x / libsodium: 1.0.18
```

### 5.2 自包含单文件(零依赖,U 盘友好)

> 目标机器**不需要**任何 .NET 运行时。**最大体积**(~68 MB)但最便携。

```bash
# Windows x64, 单文件
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o ./publish/win-x64-sc

# Linux x64, 单文件
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o ./publish/linux-x64-sc
```

**产物**:`okv.exe`(或 `okv`)一个文件,68 MB,内含 .NET 运行时、libsodium native、所有应用 DLL 和 11 个模板。**目标机器零依赖运行**。

**校验**:
```bash
./publish/win-x64-sc/okv.exe version
# 自包含运行时显示:.NET 8.0.x
```

> 关于 `<DebugType>`:Release 自包含默认不包含 .pdb;调试时加 `-p:DebugType=embedded`。

### 5.3 自包含目录(框架与 5.2 同,但作为多文件目录)

适合需要附带 README / LICENSE / 默认配置 / 平台特定 dll 的发布。

```bash
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=true \
  -o ./publish/win-x64-r2r
```

`PublishReadyToRun=true` 预编译热点路径为 R2R 格式,启动更快(~10% 体积代价,冷启动 ≤ 2s 目标更易达成)。

### 5.4 NuGet 包(DLL 库)

`OmniKeyVault.Application` 与 `OmniKeyVault.Infrastructure` 可作为类库被第三方 .NET 项目消费。

CLI 项目本身因 `<OutputType>Exe</OutputType>` 不打包。需添加 `<IsPackable>true</IsPackable>` 到对应 `.csproj` 后:

```bash
# 仅打包 Application 层
dotnet pack src/OmniKeyVault.Application/OmniKeyVault.Application.csproj \
  -c Release -o ./nupkg

# 全解决方案打包
dotnet pack OmniKeyVault.sln -c Release -o ./nupkg
```

> v0.2 现状:`OmniKeyVault.{Domain,Contracts,Application,Infrastructure}` 都缺 `<IsPackable>true</IsPackable>`,且 `Directory.Build.props` 设置 `GenerateDocumentationFile=false`(`<PackageId>` / `<Description>` 暂未声明)。v0.2.1 添加 `Directory.Packages.props` + `PackageMetadata` 后 NuGet 发布流程落地。

### 5.5 RID 与平台支持矩阵

| RID | 平台 | Sodium.Core native |
|---|---|---|
| `win-x64` / `win-x86` | Windows 10/11 | `runtimes\win-*\native\libsodium.dll` |
| `linux-x64` / `linux-arm` / `linux-arm64` | Linux | `runtimes/linux-*/native/libsodium.so` |
| `linux-musl-x64` / `linux-musl-arm` / `linux-musl-arm64` | Alpine / musl-based | `runtimes/linux-musl-*/native/libsodium.so` |
| `osx-x64` / `osx-arm64` | macOS 10.15+ / Apple Silicon | `runtimes/osx-*/native/libsodium.dylib` |

Sodium.Core 1.3.2 自动选择匹配 RID,无需手动配置。CLI **不直接使用 P/Invoke 平台 API**(FileSystemWatcher 仅可选,Roadmap S4-T1 延期到 v0.2.1),所以无需 Windows-only 编译。

### 5.6 版本号与产物命名

版本号在 [Directory.Build.props](Directory.Build.props) 中集中管理:

```xml
<Version>1.1.0</Version>
<AssemblyVersion>1.1.0.0</AssemblyVersion>
<FileVersion>1.1.0.0</FileVersion>
```

发布产物建议命名:

```
okv-v1.0.0-win-x64-fd.zip
okv-v1.0.0-linux-x64-sc.tar.gz
okv-v1.0.0-osx-arm64-fd.zip
okv-v1.0.0-source.tar.gz          # git archive
okv-v1.0.0-checksums.txt
```

发布脚本(v0.2.1 引入)可自动 `dotnet publish -r <RID>` × 6 RID + `tar` / `zip` 打包。

### 5.7 完整多平台发布脚本 (Bash)

```bash
#!/usr/bin/env bash
set -euo pipefail
VERSION="1.0.0"
OUT="./publish"

declare -A RIDS=(
  [linux-x64]=linux-x64
  [linux-arm64]=linux-arm64
  [osx-x64]=osx-x64
  [osx-arm64]=osx-arm64
  [win-x64]=win-x64
)

for name in "${!RIDS[@]}"; do
  rid="${RIDS[$name]}"
  ext="fd"  # framework-dependent by default
  echo "==> Publishing $name ($rid, $ext)"
  dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
    -c Release -r "$rid" --self-contained false \
    -o "$OUT/$name-$ext"
  ( cd "$OUT/$name-$ext" && zip -r "../okv-v$VERSION-$name-$ext.zip" . )
done

# SHA256SUMS
( cd "$OUT" && sha256sum *.zip > "okv-v$VERSION-checksums.txt" )
echo "Done. Artifacts in $OUT/"
```

---

## 6. 容器化(可选)

v0.2 没有 `Dockerfile` / `docker-compose.yml`(v0.2.1 添加)。最小化示例:

```dockerfile
# Dockerfile 示例(v0.2.1 引入)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
COPY --from=build /publish/linux-x64-fd /app
WORKDIR /app
ENTRYPOINT ["/app/okv"]
```

> 容器内 `okv vault` 不会持久化任何数据(默认 `%APPDATA%` / `~/.config` 在容器内是临时);挂载 volume 是必须的:`-v ~/.okv-data:/root/.config/OmniKeyVault`。

---

## 7. 安装位置(用户态)

CLI 不写注册表 / 系统目录。所有数据在用户态:

| OS | 配置 / 设备密钥 | 默认 Vault |
|---|---|---|
| Windows | `%APPDATA%\OmniKeyVault\device-keys\<vault-uuid>.key` | `%USERPROFILE%\OmniKeyVault\vault.okv` |
| Linux | `~/.config/OmniKeyVault/device-keys/<vault-uuid>.key` | `~/OmniKeyVault/vault.okv` |
| macOS | `~/Library/Application Support/OmniKeyVault/device-keys/<vault-uuid>.key` | `~/OmniKeyVault/vault.okv` |

模板查找顺序(后者覆盖前者):

1. `%APPDATA%/OmniKeyVault/templates/*.json`(用户覆盖)
2. `okv.exe` 同目录的 `templates/*.json`(内置)

卸载即删除上述目录,**无注册表 / 系统文件残留**。

---

## 8. 故障排查 (Troubleshooting)

### 8.1 `okv.exe` 启动报 "It was not possible to find any compatible framework version"

- **原因**:目标机器无 .NET 8 运行时。
- **修复**:安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0),或改用 5.2 的自包含单文件包。

### 8.2 `okv.exe` 启动报 "Unable to load shared library 'libsodium'"

- **原因**:`libsodium.dll / .so / .dylib` 与 `okv` 二进制不在同目录(框架依赖发布场景)。
- **修复**:确保发布的 `runtimes/<rid>/native/libsodium.*` 完整复制,或重做 `dotnet publish --self-contained true`。

### 8.3 `Argon2id` 首次解锁耗时 > 1.5 s

- **正常**:首次 KDF 派生需要 256 MiB 内存 + ~3 轮迭代,典型 0.5-1.2 s。
- **异常**:若 > 2 s,检查:
  - 设备是否有足够空闲 RAM(> 512 MB 可用)
  - 是否处于虚拟化 / WSL / 容器中(沙箱可能限制内存)
  - Argon2 参数是否被错误调低(检查 `vault info` 输出的 `t= / m= / p=`)

### 8.4 模板加载不到(`TemplateService_LoadFromDirectory_LoadsAllTemplates` 失败)

- 检查 `templates/` 目录是否随 `okv` 一起发布。`dotnet publish` 默认会复制 `<None Include="..\..\templates\*.json">` 链项,无需手工复制。
- 自包含单文件发布时,模板会被嵌入到 `okv.exe` 内部,运行时会自动提取到临时目录,**无需手工操作**。

### 8.5 PowerShell 下中文乱码

`Write-Host` 默认使用 GB2312 编码。CLI 内部用 UTF-8。**推荐**:用 `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` 或在 PowerShell Core / pwsh 下运行。在 CI(UTF-8 locale)中无影响。

### 8.6 测试在 Linux / macOS 失败而 Windows 通过

- 检查文件路径大小写敏感(Linux/macOS):`TempVaultDir` 路径使用 `Path.GetTempPath()`,跨平台 OK。
- 检查 line endings:仓库是 LF 风格(`.gitattributes` 缺失 → v0.2.1 补),但 .csproj 不会因 CRLF / LF 差异而失败。
- `Sodium.Core` 平台特定 native 部署依赖 RID 自动选择,无需手工 `LD_LIBRARY_PATH`。

### 8.7 同步状态 `Devices` 列表快速膨胀

CLI 默认 `deviceId = "<machine>-<pid>"`,**每次 CLI 进程都是新设备**。同步多进程时 `manifest.json` 的 `device_public_keys` 字典会增长。

- **临时缓解**:`sync force` 后手动删除 `manifest.json` 重置。
- **正式修复**:v0.2.1 引入持久化 `device.id`(写一次,后续读取复用);v1.0 提供 `sync device revoke` 子命令。

---

## 9. 偏差与延期 (Deviations)

v0.2 范围内**有意不做**的事项,与 v2.0 / v1.0 计划对齐:

| 项 | 状态 | 替代方案 / 路径 |
|---|---|---|
| `.gitignore` | 缺失(v0.2) | v0.2.1 添加(忽略 `bin/` `obj/` `.vs/` 等) |
| `Directory.Packages.props`(中央包版本管理) | 缺失 | v0.2.1 引入,迁移 Sodium.Core / xunit / FluentAssertions |
| `Dockerfile` / `docker-compose.yml` | 缺失 | v0.2.1 添加,基于 5.2 自包含镜像 |
| CI 配置(GitHub Actions / Gitea Actions) | 缺失 | v0.2.1 添加 `ci.yml`,跑 `build + test + publish` 矩阵 |
| Release 脚本(`scripts/release.sh` / `.ps1`) | 缺失 | v0.2.1 实现 §5.7 的全 RID 发布 |
| NuGet 发布 | 未启用 | v0.2.1 添加 `IsPackable=true` + 包元数据 |
| ~~GUI 启动器~~ | ~~不存在(AvAvalonia 推迟到 v0.2.1)~~ | **v0.2 已落地**:`src/OmniKeyVault.Cli/Gui/Views/` 下 24 个 Avalonia 视图(详见 [TEST_REPORT.md §2.1 S3-T2/S4-T5/S4-T8 状态更新](./TEST_REPORT.md));`okv`(无参数)直接启动 GUI |
| `.editorconfig` / `.gitattributes` | 缺失 | v0.2.1 添加,统一代码风格与 EOL |

---

## 10. 复现清单 (Reproduction Checklist)

```bash
# 1. 准备
dotnet --version          # 应 ≥ 8.0.100
git clone <repo>
cd OmniKeyVault

# 2. 构建 + 测试
dotnet build OmniKeyVault.sln -c Release     # → 0 warnings, 0 errors
dotnet test -c Release                        # → 467 passed (457 + 10)

# 3. 1万条目性能压测(v0.4 S8-T5)
dotnet run --project tools/OmniKeyVault.Benchmark -c Release    # → ALL OK

# 4. 框架依赖发布(Windows x64)
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r win-x64 --self-contained false \
  -o ./publish/win-x64-fd
./publish/win-x64-fd/okv.exe version          # → OmniKey Vault v1.0.0

# 5. 自包含单文件(Windows x64)
dotnet publish src/OmniKeyVault.Cli/OmniKeyVault.Cli.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish/win-x64-sc
./publish/win-x64-sc/okv.exe version         # → 同上,无需 .NET 运行时
```

**期望**:所有命令 0 失败,所有测试通过,产物可独立运行。

---

## 11. 后续 (v0.2.1+)

| 项 | 说明 |
|---|---|
| GitHub Actions | `ci.yml`:`ubuntu-latest` + `windows-latest` + `macos-latest` × `Debug` + `Release`,自动发布 artifacts |
| `Directory.Packages.props` | 中央版本管理,所有项目 `PackageReference` 无版本号 |
| `.gitignore` | 跨平台标准忽略规则 |
| `.editorconfig` | 强制 LF EOL + 4 空格缩进 + Roslyn 格式化规则 |
| Release 脚本 | `scripts/release.sh` + `scripts/release.ps1`,跑 §5.7 全 RID 矩阵 |
| ~~GUI 发布~~ | **v0.2 已落地**:`okv`(无参数)直接启动 Avalonia 11 桌面应用;`publish/win-x64-fd/` 产物为框架依赖版。详见 [MANUAL.md §17.2](./MANUAL.md#172-v02已交付--多-profile--dev-备份--同步)。 |
| Homebrew / scoop / winget 配方 | 官方包管理器分发(社区贡献优先) |

---

## 12. 参考 (References)

- [MANUAL.md](MANUAL.md) — 用户面向项目手册(产品 / UI / UX / 路线图)
- [ROADMAP.md](ROADMAP.md) — Sprint 任务分解与里程碑
- [ARCHITECTURE.md](ARCHITECTURE.md) — 分层与依赖方向
- [INTERNAL.md](INTERNAL.md) — 内部 CLI 模式(集成测试 / CI 自动化入口)规范
- [OKV_FORMAT.md](OKV_FORMAT.md) — `.okv` / `.okv.dev` 二进制规范
- [SECURITY.md](SECURITY.md) — 威胁模型与密码学不变量
- [TEST_REPORT.md](TEST_REPORT.md) — v0.2 测试覆盖与偏差
- [PLATFORM_TEMPLATES.md](PLATFORM_TEMPLATES.md) — 11 个模板字段定义
- [docs/UI/](UI/) — GUI HTML 静态视觉参考(原型,非最终实现)
- .NET 8 RID 索引:https://learn.microsoft.com/dotnet/core/rid-catalog
- Sodium.Core NuGet:https://www.nuget.org/packages/Sodium.Core/

---

**文档结束**。如有疑问,优先查阅 [TEST_REPORT.md](TEST_REPORT.md) §9(已知偏差)与 [ARCHITECTURE.md](ARCHITECTURE.md) §10(ADR)。v0.2.1 引入 CI 后,本文档将由自动化生成的 release notes 补充。

### 修订记录

| 版本 | 日期 | 修订 |
|---|---|---|
| 0.1 | 2026-06-22 | v0.1 初稿(以 CLI 为唯一入口) |
| 0.2 | 2026-06-23 | v0.2 增量:多 Profile + Dev 备份 + 同步 + TOTP;357 个测试;`okv.exe` 框架依赖发布 |
| 0.3 | 2026-06-24 | §1 / §4 / §9 / §11 / §12 全部更新:GUI 作为主程序(无参数启动);内部 CLI 仅作集成测试 / CI 入口(详见 [INTERNAL.md](./INTERNAL.md));§9 偏差表中"GUI 启动器"项划掉(已落地,24 个 Avalonia 视图) |
| **1.1** | **2026-07-07** | **v1.1 优化进行中:** 新增 `tools/OmniKeyVault.Analyzers/` Roslyn 分析器项目(OKV0001 + OKV0003);`Directory.Build.props` 版本 1.0.0 → 1.1.0 + 分析器注入;§1 仓库结构加入分析器项目;§3.3 测试数 451 → 467(含 10 分析器测试);§5.6 版本号更新为 1.1.0 |
| **1.0** | **2026-06-25** | **v1.0 RC:** GUI 全部主流程(14 XAML 视图 + 12 个 demo 入口) + 1万条目性能压测就绪;`tools/OmniKeyVault.Benchmark` 落地并加入 sln;§3.3 测试数 357 → 451;§3.4 新增 benchmark 用法;§5.6 `okv-v1.0.0-*` 产物命名 |
