# 贡献指南

感谢你对 OmniKey Vault 项目的关注！本文档介绍了参与项目开发的方式和规范。

---

## 如何贡献

### 报告问题

如果你发现了 Bug 或有功能建议，请通过 [GitHub Issues](https://github.com/mawenshui/omnikey-vault/issues) 提交。提交时请：

1. 先搜索已有 Issue，避免重复
2. 使用清晰的标题描述问题
3. 提供复现步骤（如果是 Bug）
4. 说明你的环境（操作系统、.NET 版本、OmniKey Vault 版本）

### 提交代码

1. Fork 本仓库
2. 创建特性分支：`git checkout -b feature/your-feature`
3. 编写代码并确保通过所有测试：`dotnet test`
4. 提交代码：`git commit -m 'feat: 添加 XXX 功能'`
5. 推送分支：`git push origin feature/your-feature`
6. 创建 Pull Request

### 提交信息规范

使用 [Conventional Commits](https://www.conventionalcommits.org/) 格式：

| 前缀 | 用途 | 示例 |
|------|------|------|
| `feat` | 新功能 | `feat: 添加 TOTP 自动复制功能` |
| `fix` | Bug 修复 | `fix: 修复 WebDAV 同步空指针异常` |
| `docs` | 文档更新 | `docs: 更新使用手册至 v1.6` |
| `refactor` | 代码重构 | `refactor: 提取 SettingsStore 到独立类` |
| `test` | 测试相关 | `test: 新增 Double Argon2id KDF 测试` |
| `chore` | 构建/工具 | `chore: 升级 NuGet 包版本` |
| `security` | 安全相关 | `security: 升级 Argon2id 双轮 KDF` |

---

## 开发环境

### 先决条件

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) 或 [VS Code](https://code.visualstudio.com/)
- [Git](https://git-scm.com/)

### 构建与测试

```bash
# 还原依赖
dotnet restore OmniKeyVault.sln

# 构建（0 warnings, 0 errors）
dotnet build OmniKeyVault.sln -c Release

# 运行全部测试（578/578 通过）
dotnet test

# 性能压测
dotnet run --project tools/OmniKeyVault.Benchmark
```

### 项目架构

```
src/
├── OmniKeyVault.Domain/           # 领域模型 — 纯值对象，无 I/O
├── OmniKeyVault.Contracts/        # 接口契约 — ICryptoProvider 等
├── OmniKeyVault.Application/      # 应用服务 — VaultService 等
├── OmniKeyVault.Infrastructure/   # 基础设施 — SodiumCryptoProvider 等
└── OmniKeyVault.Cli/             # 唯一入口项目（GUI + 内部 CLI）
```

详细的架构设计请阅读 [架构设计文档](docs/架构设计.md)。

---

## 代码规范

### 安全红线

项目内置 Roslyn 分析器，编译期自动拦截以下违规：

| 规则 | 说明 |
|------|------|
| OKV0001 | 禁止使用 `string` 类型传递密码/密钥，必须使用 `ReadOnlySpan<byte>` |
| OKV0003 | 禁止使用 `==` 比较密钥，必须使用固定时间比较 |

### 编码规范

- `TreatWarningsAsErrors = true`：所有警告视为错误
- 启用 `Nullable`：所有引用类型必须显式标注可空性
- 启用 `ImplicitUsings`：使用全局 using
- 敏感数据使用 `ReadOnlySpan<byte>` / `Memory<byte>`，禁止字符串形态在内存中残留
- 所有密码学操作通过 `ICryptoProvider` 接口，禁止业务层直接调用底层库
- 所有持久化操作通过临时文件 + rename，拒绝部分写入状态

### 文档规范

- 所有文档使用中文编写
- 文档文件名使用中文
- 每份文档末尾保留修订记录表
- 修改代码时同步更新相关文档

---

## 添加平台模板

1. 在 `templates/` 目录下创建新的 JSON 文件
2. 参照 [平台模板参考文档](docs/平台模板参考.md) 中的格式规范
3. 添加测试用例
4. 提交 Pull Request

---

## 安全漏洞报告

如果你发现了安全漏洞，请**不要**在 GitHub Issues 中公开提交。请：

1. 发送邮件至安全团队
2. 或在 GitHub 上创建 [Security Advisory](https://github.com/mawenshui/omnikey-vault/security/advisories/new)

详见 [安全设计文档](docs/安全设计.md)。

---

## 许可证

提交的代码将基于 [MIT 许可证](LICENSE) 开源。
