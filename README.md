<div align="center">

# OmniKey Vault

### 本地优先 · 端到端加密 · 开源凭据管理工具

将分散在各 SaaS / 云厂商中的 API Key、Secret、Token、证书等信息收纳进一个加密保险库，通过加密文件级同步在多设备间无缝流转。

**即使源代码和金库文件同时泄露，没有主密码也无法解密。**

[![.NET CI](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-791%2F791-brightgreen)](docs/测试报告.md)
[![Version](https://img.shields.io/badge/version-2.3.6-blue)](docs/变更日志.md)
[![License](https://img.shields.io/badge/license-MIT-yellow)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)](docs/编译打包指南.md)

</div>

---

## 概述

**OmniKey Vault** 是一款面向开发者与运维人员的凭据管理工具。它把你的 API Key、Secret、Account ID、Token、证书指纹等信息收纳进一个加密保险库（`.okv` 文件），并通过 WebDAV 或本地文件夹同步在多设备间安全流转。

> **核心安全主张**：就算同步过程或云端备份文件整体泄露，只要主密码不丢，攻击者拿到密文也无法在合理时间内还原明文。而要做到这一点，不仅需要密码学强度足够，还需要**自定义的、不被通用工具（KeePass / hashcat / John）识别的信封格式**。

### 为什么选择 OmniKey Vault？

| 痛点 | 现状 | OmniKey Vault 方案 |
|------|------|---------------------|
| API Key 散落各处 | 记事本、Excel、浏览器收藏夹、聊天记录 | 统一保险库，集中加密存储 |
| 凭据泄露 | 同步盘文件明文、误传到代码仓库 | 客户端零知识，服务端只见密文 |
| 多设备一致性 | 多个 .txt 散落各处，无法确定最新 | 版本化加密文件 + 向量时钟合并 |
| 平台字段各异 | 有的需要 Key+Secret，有的需要 Account+Tenant+Region | 模板 + 自定义字段，每个条目独立加密 |
| 备份难恢复 | 备份文件泄露即等于裸奔 | 双重 Argon2id KDF + 自有信封格式，泄露也无法离线爆破 |

---

## 核心特性

### 安全

- **双重 Argon2id 密钥拉伸（v1.6）**：双轮 KDF 链（Round 1: 256 MiB + Round 2: 64 MiB = 总计 320 MiB 内存成本），离线爆破成本翻倍
- **XChaCha20-Poly1305 AEAD** 认证加密
- **Ed25519** 容器签名，防篡改
- **自定义 `.okv` 二进制格式**，不被 hashcat / KeePass / John 等工具识别
- **域分隔符**（`okv-kek-v2`）确保 v1/v2 密钥不可互换
- **内存安全**：敏感数据用完即清零（`CryptographicOperations.ZeroMemory`）
- **日志脱敏**：自动过滤 API Key、密码、邮箱等敏感信息
- **Roslyn 分析器**：编译期拦截安全违规代码

### 易用

- **GUI 桌面应用**（Avalonia 11），无参数启动即进入图形界面
- **WebDAV 云同步**：支持坚果云 / Nextcloud / Synology / Box / 自建
- **本地文件夹同步**：OneDrive / Google Drive / Dropbox / USB
- **多 Profile**：生产 / 开发 / 测试环境隔离，带视觉标识
- **TOTP 验证码**：内置 RFC 6238 标准的 OTP 生成器
- **平台模板**：内置 66 个模板，覆盖国内外主流平台（GitHub / OpenAI / AWS / 腾讯云 / 华为云 / 百度千帆 / 通义千问 / DeepSeek / 支付宝 / 微信支付 / 钉钉 / 飞书等）
- **全文搜索**：字段级搜索语法，1 万条目搜索 ≤ 2ms
- **历史快照**：每次编辑自动保存版本，可随时回退
- **一键轮换**：支持 OpenAI / GitHub API 密钥自动轮换
- **导入**：Bitwarden JSON / KeePass 2.x XML / KeePass KDBX / 1Password .1pux / EnPass JSON / CSV（LastPass/Chrome/Edge/Firefox）/ .env 文件
- **国际化**：中文 + 英文
- **自动锁定**：空闲超时 / 系统锁屏 / 系统休眠
- **凭据泄露检测**：HaveIBeenPwned k-anonymity 模式，主动检测密码是否已在已知泄露库中
- **紧急联系人**：Shamir 分片恢复机制，主密码拆分为 N 份分片，任意 K 份可还原
- **加密容器导出**：导出为独立加密文件（.okvx），输入独立密码，方便安全分享
- **密码生成器**：自定义长度/字符集/排除易混字符 + 密码短语模式，Ctrl+G 快捷唤起
- **X.509 证书管理**：解析 PEM/PFX 证书，显示到期时间、指纹、颁发者
- **SSH Agent 集成**：一键将 SSH 私钥加载到 Windows ssh-agent
- **S3 兼容存储同步**：AWS S3 / MinIO / Cloudflare R2 等对象存储作为同步后端
- **文件夹/标签**：文件夹 CRUD + 标签系统（侧边栏标签面板，点击筛选）
- **批量操作**：批量编辑/删除/导出，Ctrl+点击多选
- **密码强度全库扫描**：一键扫描所有条目的密码强度
- **系统通知**：条目过期等事件推送 Windows 系统通知
- **窗口位置记忆**：记住上次窗口位置和大小

---

## 快速开始

### 安装

1. 前往 [Releases](https://github.com/mawenshui/omnikey-vault/releases) 下载最新版 `OmniKeyVault-Setup-2.3.6.exe`（安装包）或 `OmniKeyVault-2.3.6-win-x64.zip`（便携版）
2. 安装包：双击运行，按向导完成安装
3. 便携版：解压后直接运行 `okv.exe`

> **系统要求**：Windows 10 1809 (17763) 或更高版本，.NET 8 运行时（安装包已内置）

### 从源码构建

```bash
# 1. 克隆仓库
git clone https://github.com/mawenshui/omnikey-vault.git
cd omnikey-vault

# 2. 还原 + 构建
dotnet restore OmniKeyVault.sln
dotnet build OmniKeyVault.sln -c Release

# 3. 启动 GUI 桌面应用
dotnet run --project src/OmniKeyVault.Cli

# 4. 运行测试
dotnet test    # → 791/791 通过

# 5. 性能压测（1 万条目）
dotnet run --project tools/OmniKeyVault.Benchmark
```

### 基本使用

1. **创建金库**：启动程序 → 点击「创建新金库」→ 设置主密码 → 保存恢复密钥
2. **添加条目**：点击「+ 新建条目」→ 选择平台模板 → 填入凭据 → 保存
3. **多设备同步**：设置 → 配置 WebDAV → 点击同步
4. **搜索**：在搜索框输入关键词，支持 `name:github secret:token` 字段级语法

---

## 加密架构

### v1.6 金库（Header v2 — 双重 Argon2id）

```
主密码 (不存储)
    │
    ▼
Argon2id Round 1 (t=3, m=256MiB, p=4)   ← 第一轮：全量内存成本
    │
    ▼
MK1 (中间主密钥)
    │
    ▼
Argon2id Round 2 (t=3, m=64MiB, p=1)    ← 第二轮：二次拉伸
    │
    ▼
MK2 (最终主密钥)
    │
    ▼
HKDF-SHA256(MK2, "okv-kek-v2", salt1)    ← 域分隔符升级
    │
    ▼
KEK (密钥加密密钥) → 解包 DEK (数据加密密钥)
    │
    ▼
XChaCha20-Poly1305 AEAD 加密 → Ed25519 签名 → .okv 文件
```

| 加密参数 | Round 1 | Round 2 |
|----------|---------|---------|
| 算法 | Argon2id | Argon2id |
| 内存成本 | 256 MiB | 64 MiB |
| 时间成本 | 3 | 3 |
| 并行度 | 4 | 1 |
| **总内存成本** | **320 MiB** | |

> v1 金库（Header v1）继续使用单轮 Argon2id（256 MiB），向后兼容。详见 [安全设计文档](docs/安全设计.md)。

---

## 项目结构

```
OmniKeyVault/
├── src/
│   ├── OmniKeyVault.Domain/           # 领域模型（纯值对象，无 I/O）
│   ├── OmniKeyVault.Contracts/        # 接口契约（ICryptoProvider 等）
│   ├── OmniKeyVault.Application/      # 应用服务（VaultService 等）
│   ├── OmniKeyVault.Infrastructure/   # 基础设施（SodiumCryptoProvider 等）
│   └── OmniKeyVault.Cli/             # 唯一入口项目
│       ├── Gui/                       #   Avalonia 11 MVVM 视图（用户面向）
│       └── Cli/                       #   内部 CLI 解析器（CI/测试入口）
├── tests/
│   ├── OmniKeyVault.Tests/            # xUnit 单元/集成测试（771 个）
│   └── OmniKeyVault.Analyzers.Tests/  # Roslyn 分析器测试（20 个）
├── tools/
│   ├── OmniKeyVault.Benchmark/        # 性能压测工具
│   └── OmniKeyVault.Analyzers/        # Roslyn 安全分析器
├── templates/                          # 内置平台模板 JSON（66 个）
├── installer/                          # Inno Setup 安装脚本 + 安装包
├── images/                             # 应用图标
├── docs/                               # 项目文档（中文）
└── installer/                          # Inno Setup 安装脚本 + 安装包
```

---

## 文档

所有文档均使用中文编写，位于 [`docs/`](docs/) 目录：

### 用户面向

| 文档 | 说明 |
|------|------|
| [使用手册](docs/使用手册.md) | **先看这本** — 安装、创建金库、条目管理、同步、安全设置完整指南 |
| [后续开发计划](docs/后续开发计划-面向用户.md) | 近期 / 中期 / 远期功能规划 |

### 技术规范

| 文档 | 说明 |
|------|------|
| [项目手册](docs/项目手册.md) | 产品定位、用户画像、需求、安全概述、UI/UX 规范 |
| [架构设计](docs/架构设计.md) | 分层架构、模块划分、关键接口、架构决策记录 |
| [安全设计](docs/安全设计.md) | 威胁模型、密码学套件、密钥层级、不变量、审计清单 |
| [文件格式规范](docs/文件格式规范.md) | `.okv` 二进制格式字节布局、版本兼容 |
| [平台模板参考](docs/平台模板参考.md) | 模板 schema、73+ 平台完整 JSON 库 |
| [内部接口规范](docs/内部接口规范.md) | 内部 CLI 模式规范（集成测试 / CI 入口） |

### 运维与流程

| 文档 | 说明 |
|------|------|
| [编译打包指南](docs/编译打包指南.md) | 编译、运行、打包、跨 RID 发布、故障排查 |
| [测试报告](docs/测试报告.md) | 测试结果、性能基准、已知偏差 |
| [开发路线图](docs/开发路线图.md) | Sprint 任务分解、依赖、风险 |
| [变更日志](docs/变更日志.md) | v0.1 → v2.3 全部用户可见变更 |
| [文档总览](docs/文档总览.md) | 文档索引、阅读路径、维护规则 |

---

## 性能基准

在 1 万条目规模下测试：

| 操作 | 耗时 |
|------|------|
| 创建金库 | 0.7s |
| 解锁金库 | 0.1s |
| 搜索条目 | 1.5ms |
| 同步合并 | 0.0s |

> 测试环境：.NET 8.0.27, Windows x64, libsodium 1.0.18

---

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET 8.0 |
| UI 框架 | Avalonia 11 (MVVM) |
| 密码学 | libsodium 1.0.18 (Sodium.Core 1.3.2) |
| KDF | Argon2id（双轮，总计 320 MiB 内存成本） |
| 对称加密 | XChaCha20-Poly1305 AEAD |
| 签名 | Ed25519 |
| 密钥派生 | HKDF-SHA256 |
| 测试框架 | xUnit + FluentAssertions |
| 代码分析 | Roslyn Analyzer（OKV0001-OKV0004 安全规则） |
| CI/CD | GitHub Actions |

---

## 贡献

欢迎提交 Issue 和 Pull Request！请阅读 [贡献指南](CONTRIBUTING.md) 了解开发流程。

### 开发环境

```bash
# 先决条件
# - .NET 8 SDK
# - Visual Studio 2022 或 VS Code

git clone https://github.com/mawenshui/omnikey-vault.git
cd omnikey-vault
dotnet restore OmniKeyVault.sln
dotnet build OmniKeyVault.sln -c Release
dotnet test
```

### 代码安全规范

项目内置 4 条 Roslyn 分析器规则，编译期自动拦截安全违规：

| 规则 | 说明 |
|------|------|
| OKV0001 | 禁止使用 `string` 类型传递密码/密钥（必须用 `ReadOnlySpan<byte>`） |
| OKV0003 | 禁止使用 `==` 比较密钥（必须用固定时间比较） |
| OKV0002 | 禁止未 Dispose 的 `SecureKey`（规划中） |
| OKV0004 | 禁止在未解锁状态下调用 Service（规划中） |

---

## 许可证

本项目基于 [MIT 许可证](LICENSE) 开源。

---

## 安全报告

如发现安全漏洞，请**不要**在 GitHub Issues 中公开提交。请发送邮件至安全团队，或在 GitHub 上创建 [Security Advisory](https://github.com/mawenshui/omnikey-vault/security/advisories/new)。

详见 [安全设计文档](docs/安全设计.md)。

---

## 致谢

- [libsodium](https://libsodium.gitbook.io/) — 密码学原语库
- [Avalonia UI](https://avaloniaui.net/) — 跨平台 UI 框架
- [.NET](https://dotnet.microsoft.com/) — 运行时与开发平台

---

<div align="center">

**OmniKey Vault — 本地优先，安全至上。**

[报告问题](https://github.com/mawenshui/omnikey-vault/issues) ·
[查看文档](docs/文档总览.md) ·
[下载最新版](https://github.com/mawenshui/omnikey-vault/releases)

</div>
