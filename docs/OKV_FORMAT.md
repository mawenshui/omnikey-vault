# OmniKey Vault — 数据模型与 .okv 文件格式规范

| 文档版本 | 日期 | 作者 | 状态 |
|---|---|---|---|
| 1.1 | 2026-07-12 | Sisyphus | v1.6:Header v2 Double Argon2id 密钥拉伸,salt 槽后 16B 语义变更 |
| 1.0 | 2026-06-25 | Sisyphus | v1.0 RC:451/451 tests,等待外部审计;与 v0.3 附件 Blob + 加密索引格式同步 |

> 关联文档:[MANUAL.md §4.5-4.6 / §7-§8](./MANUAL.md) · [SECURITY.md](./SECURITY.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
>
> 本文档是 `.okv` 二进制格式的**权威规范**。任何实现(参考客户端、第三方工具、迁移脚本)必须以此为准。

---

## 1. 概述

### 1.1 文档目的

定义 OmniKey Vault 的:

1. 领域数据模型(Vault / Profile / Entry / Field / Folder / Template)。
2. `.okv` 二进制文件物理布局(头部、Profile Section、Entry 序列化)。
3. 同步目录的辅助文件(`manifest.json`、`.okv.lock`、snapshot)。
4. 种子文件 `seed.okv.dev`(`OKVD` magic)格式。
5. 版本兼容性策略与前向 / 后向兼容规则。

### 1.2 设计目标

| 目标 | 说明 |
|---|---|
| 私有信封格式 | 通用工具(hashcat / John)不识别,见 [SECURITY.md §5](./SECURITY.md#5-自定义信封格式设计哲学) |
| 流式加载 | 大 Vault 可按需解密单个 Entry,不需全量加载 |
| 原子写入 | 临时文件 + rename,崩溃可恢复 |
| 版本化 | 主版本号(`OKV1` → `OKV2`)显式,次版本号向后兼容 |
| 跨平台 | 字节序固定为小端;无平台特化字段 |

### 1.3 字节序与对齐

- **字节序**:全文件小端(Little-Endian)。
- **对齐**:无对齐要求,字段紧密排列。
- **字符串**:UTF-8 编码,length-prefixed(uint32 前置长度,小端)。
- **时间戳**:UTC,Unix 毫秒(int64,小端)。
- **UUID**:UUIDv7,16 字节,小端布局(与 RFC 9562 一致)。

---

## 2. 物理目录结构

### 2.1 Vault 目录布局

```
<vault-dir>/                          # 用户指定的同步目录
├── vault.okv                          # 主保险库文件(加密)
├── vault.okv.tmp                      # 写入临时文件(原子写入,正常状态下不存在)
├── manifest.json                      # 明文元数据(用于同步感知)
├── .okv.lock                          # 文件锁(JSON: {pid, hostname, ts})
├── .okv.snapshots/                    # 历史快照
│   └── <profile>/
│       └── <entry-id>/
│           └── <version>.entry.enc
└── .okv.devices/                      # 设备公钥目录(可选,用于多设备信任)
    └── <device-id>.pub
```

### 2.2 用户应用数据目录

```
%APPDATA%\OmniKeyVault\
├── settings.json                      # 用户设置(明文,无敏感)
└── vaults\<vault-uuid>\               # Vault 本地元数据
    └── device_key.pem                 # 本设备 Ed25519 私钥(KEK 包装)

%USERPROFILE%\OmniKeyVault\            # 默认 Vault 目录(可改)
└── (见 §2.1)
```

### 2.3 文件命名约定

| 文件 | 命名规则 | 说明 |
|---|---|---|
| `vault.okv` | 固定名 | 主文件 |
| `vault.okv.tmp` | 固定名 | 临时文件,正常状态不存在 |
| `manifest.json` | 固定名 | 明文元数据 |
| `.okv.lock` | 固定名 | 隐藏文件 |
| `.okv.snapshots/<profile>/<entry-id>/<version>.entry.enc` | 路径模板 | 历史快照 |
| `seed.okv.dev` | 用户命名 | 种子文件 |

---

## 3. 领域数据模型

### 3.1 整体结构

```
Vault
├── Metadata { uuid, name, created_at, vector_clock, schema_version }
├── Profiles (dict<profile_name, Profile>)
│   ├── Profile { id, name, dek_wrapped, color, settings }
│   ├── Entries (B-tree or skip-list keyed by id)
│   │   └── Entry { id, type, name, fields[], tags[], folder, ... }
│   ├── Folders (id, name, parent_id)
│   ├── Tags (string pool, dedup)
│   └── Templates (id, fields[], platform_id)
└── Indexes (encrypted inverted index)
```

### 3.2 Vault

```csharp
public sealed class Vault {
    public Guid Uuid { get; }                  // UUIDv7
    public string Name { get; }                // 用户可读名
    public DateTimeOffset CreatedAt { get; }   // UTC
    public VectorClock VectorClock { get; }    // 设备 → 计数器
    public ushort SchemaVersion { get; }       // 次版本号
    public IReadOnlyDictionary<string, Profile> Profiles { get; }
}
```

### 3.3 Profile

```csharp
public sealed class Profile {
    public Guid Id { get; }                    // UUIDv7
    public string Name { get; }                // "prod" / "dev" / "test" / 自定义
    public ProfileColor Color { get; }         // UI 标识色(prod=绿 / dev=黄 / test=蓝)
    public ProfileSettings Settings { get; }   // 同步参与、自动锁定等
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<Folder> Folders { get; }
    public IReadOnlyList<Template> Templates { get; }
    // 注意:DEK 不在领域模型中,由 CryptoProvider 管理包装形态
}

public sealed record ProfileSettings(
    bool ParticipateInSync,    // dev/test 默认 false
    bool AutoLockOnSwitch,     // 切换 Profile 时是否锁定
    int IdleLockMinutes        // 空闲锁定分钟数
);
```

### 3.4 Entry

```csharp
public sealed record Entry(
    Guid Id,                   // UUIDv7,排序友好
    EntryType Type,            // api_key / oauth / certificate / ssh_key / note / custom
    string Name,               // 人类可读名称
    string? PlatformId,        // github / aws / openai …
    IReadOnlyList<string> Tags,
    Guid? Folder,              // 所属文件夹
    IReadOnlyList<Field> Fields,
    string? Notes,             // 纯文本(v0.1)/ 富文本(v0.3+)
    DateTimeOffset CreatedAt,  // UTC
    DateTimeOffset UpdatedAt,  // UTC
    DateTimeOffset? ExpiresAt, // 用于证书 / Token 过期提醒
    uint Version               // 单条目版本号,用于乐观锁
);

public enum EntryType : byte {
    ApiKey = 1,
    OAuth = 2,
    Certificate = 3,
    SshKey = 4,
    Note = 5,
    Custom = 255
}
```

### 3.5 Field

```csharp
public sealed record Field(
    string Key,                // "secret_key" / "api_key" / ...
    string Value,              // 实际值
    FieldKind Kind,            // text / secret / url / number / date / totp_uri / file_ref
    bool Sensitive,            // UI 是否默认掩码
    string? Mask,              // 自定义掩码模板("********" 或 "sk-••••")
    FieldValidation? Validation
);

public enum FieldKind : byte {
    Text = 1,
    Secret = 2,
    Url = 3,
    Number = 4,
    Date = 5,
    TotpUri = 6,
    FileRef = 7
}

public sealed record FieldValidation(
    string? Regex,             // "^sk-.*"
    string? Hint               // "应以 sk- 开头"
);
```

### 3.6 Folder 与 Template

> **模板文件格式**:模板的完整 JSON 文件格式(含 UI 元数据:`name` / `category` / `icon` / `official_docs_url` / `auth_header` / `default_base_url` / `mvp_included` / `introduced_in` / `notes` / `expires_at_supported` / `rotation_supported` / `rotation_provider`,以及字段级 `label` / `placeholder` / `description` / `examples` / `group` / `rotatable` 等)见 [PLATFORM_TEMPLATES.md](./PLATFORM_TEMPLATES.md)。本节的 `Template` / `TemplateField` record 描述的是 **`.okv` 二进制文件内的最小存储模型**(运行时 UI 元数据从 `templates/*.json` 加载,不重复存入 `.okv`)。

```csharp
public sealed record Folder(
    Guid Id,
    string Name,
    Guid? ParentId             // null 表示根
);

public sealed record Template(
    string Id,                 // "github" / "openai" / "aws_iam_long_term" / ...
    string PlatformId,
    IReadOnlyList<TemplateField> Fields
);

public sealed record TemplateField(
    string Key,
    FieldKind Kind,
    bool Sensitive,
    bool Required,
    string? DefaultMask,
    FieldValidation? Validation
);
```

> **扩展映射**:JSON 文件格式字段 → 二进制存储字段的对应关系见 [PLATFORM_TEMPLATES.md §9.1](./PLATFORM_TEMPLATES.md#91-与-okv_formatmd-领域模型的对齐)。二进制存储仅保留运行时必需字段(Key / Kind / Sensitive / Required / DefaultMask / Validation),UI 展示字段(Label / Placeholder / Description / Group 等)从模板文件实时加载。

### 3.7 VectorClock

```csharp
public sealed class VectorClock {
    public IReadOnlyDictionary<string, long> Counters { get; }  // device_id → counter

    public VectorClock Increment(string deviceId);
    public VectorClock Merge(VectorClock other);                // max per component
    public int Compare(VectorClock other);                       // -1 / 0 / 1 / null(并发)
}
```

---

## 4. .okv 二进制头部布局

### 4.1 头部结构(Magic Header)

```
偏移  长度  字段                      说明
─────────────────────────────────────────────────────────────
0     4     Magic                     "OKV1" (0x4F 0x4B 0x56 0x01)
4     2     Header Version            0x01 0x00 (v1) 或 0x02 0x00 (v2, Double Argon2id)
6     8     App Build Hash            前 8 字节 SHA-256(构建标识)
14    16    Vault UUID                UUIDv7, 小端
30    1     Argon2id m (uint32 高位)   实际为 4B uint32,见下
30    4     Argon2id m                内存成本(字节),小端(默认 0x10000000 = 256 MiB)
34    4     Argon2id t                迭代次数,小端(默认 3)
38    1     Argon2id p                并行度(默认 4)
39    32    KDF Salt                  v1: 前 16B = KDF salt, 后 16B = 保留
                                      v2: 前 16B = Round 1 KDF salt, 后 16B = Round 2 KDF salt
71    32    MK Verify Tag             加密全零验证主密码正确(见 §4.2)
103   32    Device Ed25519 Public Key 当前签名设备公钥
135   var   Encrypted Profiles        见 §5
─────────────────────────────────────────────────────────────
      var   Vault VectorClock         见 §6
      64    Ed25519 Signature         签名前面所有字节(除签名字段本身)
```

**图示**:

```
┌────────────────────────────────────────────────────┐
│ Magic (4B)        │ "OKV1" (0x4F 4B 56 01)        │ ← 私有不通用,hashcat 不识别
├────────────────────────────────────────────────────┤
│ Header Version    │ 0x01 0x00                     │
├────────────────────────────────────────────────────┤
│ App Build Hash    │ 8B (前 8 字节 SHA-256)        │ ← 标识构建来源,反供应链
├────────────────────────────────────────────────────┤
│ Vault UUID        │ 16B                           │
├────────────────────────────────────────────────────┤
│ Argon2id Params   │ m:uint32 / t:uint32 / p:uint8 │ ← 自定义,默认 256MB
├────────────────────────────────────────────────────┤
│ KDF Salt          │ 32B                           │
├────────────────────────────────────────────────────┤
│ MK Verify Tag     │ 32B (加密全零验证主密码正确)  │
├────────────────────────────────────────────────────┤
│ Device Ed25519 PK │ 32B                           │
├────────────────────────────────────────────────────┤
│ Encrypted Profiles│ var (见 §5)                   │
├────────────────────────────────────────────────────┤
│ Vault VectorClock │ var (见 §6)                   │
├────────────────────────────────────────────────────┤
│ Ed25519 Signature │ 64B (签名前面所有字节)        │
└────────────────────────────────────────────────────┘
```

### 4.2 MK Verify Tag 机制

- **生成**:创建 Vault 时,用 KEK 加密 32 字节全零:`verifyTag = XChaCha20-Poly1305(KEK, 0x00*32, nonce=random_24B)`。
- **存储**:`verifyTag` 包含 nonce(24B) + ciphertext(32B) + tag(16B) = 72B(实际存储为 32B 摘要 + 24B nonce,见下)。
- **简化布局**:为保持头部固定长度,实际存储为:
  - `nonce` (24B) 放入 KDF Salt 之后单独字段(但 v0.1 简化为随机派生,见实现注记)。
  - `verifyTag` (32B) = MAC of (KEK, "okv-verify-v1")。
- **验证**:用户输入主密码 → 派生 MK → 派生 KEK → 重新计算 MAC → `FixedTimeEquals` 比较。

**实现注记**:v0.1 实现可选择 AEAD 加密全零方案或 MAC 方案,只要保证:
1. 验证恒定时间。
2. 验证失败不泄露 MK 状态。
3. 头部字段长度固定(便于解析)。

### 4.3 App Build Hash

- **来源**:构建时计算 `(git_commit_hash + dotnet_version + libsodium_version)` 的 SHA-256,取前 8 字节。
- **用途**:客户端启动时校验,若与当前构建不符提示"文件由不同版本创建"。
- **供应链**:用于审计追溯,某次供应链攻击的文件可定位到构建来源。

### 4.4 Argon2id 参数字段

- **存储**:非默认参数(256 MiB)写入头部,客户端按头部参数执行 KDF。
- **动态调整**:首次解锁后若耗时 <500ms,自动调高 `m` 并重写头部(用 KEK 加密新 verifyTag)。
- **攻击成本**:攻击者需读取头部参数才能构造爆破调用,无法用 hashcat 预设模式。

---

## 5. Profile Section 序列化

### 5.1 Profile Section 总体结构

```
Profile Section(整个 section 一起 AEAD 加密):
┌──────────────────────────────────────────────────┐
│ ProfileID (16B UUIDv7)                          │
│ ProfileName (length-prefixed UTF-8)             │
│ DEK Wrapped (96B: 24B nonce + 40B ct + 16B tag)│ ← KEK 包装的 DEK
│ ProfileColor (1B enum)                          │
│ ProfileSettings (var, 见 §5.2)                  │
│ Folders List (var, 见 §5.3)                     │
│ Tags Pool (var, 见 §5.4)                        │
│ Templates List (var, 见 §5.5)                   │
│ Entries B-tree (var, 见 §5.6)                   │
└──────────────────────────────────────────────────┘
```

### 5.2 ProfileSettings 序列化

```
字段                      长度    说明
─────────────────────────────────────────────────
ParticipateInSync         1B      0/1
AutoLockOnSwitch          1B      0/1
IdleLockMinutes           4B      int32 LE
```

### 5.3 Folders List 序列化

```
字段                      长度    说明
─────────────────────────────────────────────────
FolderCount               4B      uint32 LE
Folders[]                 var     每个文件夹:
  FolderId                16B     UUIDv7
  Name                    var     length-prefixed UTF-8
  ParentId                16B     UUIDv7 或全零(根)
```

### 5.4 Tags Pool 序列化

```
字段                      长度    说明
─────────────────────────────────────────────────
TagCount                  4B      uint32 LE
Tags[]                    var     每个标签:
  Tag                     var     length-prefixed UTF-8(去重)
```

### 5.5 Templates List 序列化

```
字段                      长度    说明
─────────────────────────────────────────────────
TemplateCount             4B      uint32 LE
Templates[]               var     每个模板:
  TemplateId              var     length-prefixed UTF-8
  PlatformId              var     length-prefixed UTF-8
  FieldCount              4B      uint32 LE
  Fields[]                var     见 §5.7
```

### 5.6 Entries B-tree 序列化

```
Entry Header(明文,用于索引):
  EntryId                 16B     UUIDv7
  Version                 4B      uint32 LE
  Type                    1B      EntryType enum
  UpdatedAt               8B      int64 ms UTC

Entry Payload(独立 AEAD 加密,XChaCha20-Poly1305 + 24B nonce):
  Name                    var     length-prefixed UTF-8
  PlatformId              var     length-prefixed UTF-8 (或 0 长度)
  TagsCount               4B
  Tags[]                  var     索引到 Tags Pool(uint32)
  FolderId                16B     或全零
  Notes                   var     length-prefixed UTF-8
  CreatedAt               8B      int64 ms UTC
  ExpiresAt               8B      int64 ms UTC 或 -1(无)
  FieldCount              4B
  Fields[]                var     见 §5.7
```

**为什么条目级独立加密**:
- 即使攻击者拿到整个 `.okv` 文件 + Profile DEK(假设 DEK 泄露),也只能解密该 Profile 的条目。
- 流式加载:只需解密当前查看的 Entry,不需全量解密。
- 单条目修改:只需重加密该条目 + 重写 B-tree 索引,无需重写整个 Profile。

### 5.7 Field 序列化

```
Field 结构:
  Key                     var     length-prefixed UTF-8
  Kind                    1B      FieldKind enum
  Sensitive               1B      0/1
  Value                   var     length-prefixed UTF-8
  Mask                    var     length-prefixed UTF-8 (或 0 长度)
  Validation (optional):
    HasValidation         1B      0/1
    Regex                 var     length-prefixed UTF-8
    Hint                  var     length-prefixed UTF-8
```

---

## 6. VectorClock 序列化

```
字段                      长度    说明
─────────────────────────────────────────────────
CounterCount              4B      uint32 LE
Counters[]                var     每个计数器:
  DeviceId                var     length-prefixed UTF-8
  Counter                 8B      int64 LE
```

---

## 7. 附件 Blob 存储

### 7.1 存储布局

附件不内嵌于 Entry,而是独立存于附件池:

```
<vault-dir>\.okv.attachments\
└── <profile>\
    └── <attachment-id>\
        └── chunks\
            ├── 00001.chunk.enc
            ├── 00002.chunk.enc
            └── meta.json.enc
```

### 7.2 分块加密

- **块大小**:默认 1 MiB。
- **每块独立加密**:`XChaCha20-Poly1305(DEK, chunk, nonce=random_24B)`。
- **元数据**:`meta.json` 加密后存储,含原文件名、MIME、大小、SHA-256。

### 7.3 Field 引用

```csharp
// Field with Kind=FileRef:
{
  "key": "private_key_pem",
  "kind": "file_ref",
  "value": "<attachment-id>",  // 引用附件池
  "sensitive": true
}
```

---

## 8. manifest.json

### 8.1 用途

明文元数据,用于同步感知(无需解密即可判断是否需要同步)。

### 8.2 Schema

```json
{
  "vault_uuid": "01H7XGK1...（UUIDv7）",
  "device_id": "laptop-abc",
  "last_modified": "2026-06-17T08:00:00.123Z",
  "last_modified_by": "laptop-abc",
  "profiles": ["prod", "dev", "test"],
  "vector_clock": {
    "laptop-abc": 17,
    "workstation-def": 23
  },
  "schema_version": 1,
  "okv_format_version": "1.0",
  "device_public_keys": {
    "laptop-abc": "base64-ed25519-pubkey",
    "workstation-def": "base64-ed25519-pubkey"
  }
}
```

### 8.3 字段说明

| 字段 | 类型 | 说明 |
|---|---|---|
| `vault_uuid` | string(UUIDv7) | Vault 唯一标识 |
| `device_id` | string | 当前写入设备标识 |
| `last_modified` | ISO 8601 | 最后修改时间(UTC) |
| `last_modified_by` | string | 最后修改设备 |
| `profiles` | string[] | Profile 名称列表(明文,便于同步感知) |
| `vector_clock` | object | 设备 → 计数器 |
| `schema_version` | int | 领域 schema 次版本号 |
| `okv_format_version` | string | `.okv` 格式版本("1.0", "1.1", "2.0") |
| `device_public_keys` | object | 设备 → Ed25519 公钥(base64) |

### 8.4 安全约束

- `manifest.json` **不含**主密码、MK、KEK、DEK、Entry 内容。
- `profiles` 列表明文,但仅暴露 Profile 名称(如 "prod"、"dev"),不暴露条目数。
- `device_public_keys` 明文,用于多设备信任建立。
- 修改 `manifest.json` 同样需要原子写入 + 签名(签名存于 `vault.okv` 头部)。

---

## 9. .okv.lock 文件

### 9.1 用途

跨进程文件锁,防止多进程同时写入。

### 9.2 格式

```json
{
  "pid": 12345,
  "hostname": "laptop-abc",
  "device_id": "laptop-abc",
  "ts": "2026-06-17T08:00:00.123Z",
  "app_version": "0.1.0"
}
```

### 9.3 锁机制

- **获取**:原子创建 `.okv.lock`(Windows: `CREATE_NEW`;POSIX: `O_CREAT|O_EXCL`)。
- **检测**:其他进程发现 `.okv.lock` 存在,读取内容,检查 PID 是否存活。
- **释放**:进程退出时删除(`IDisposable` + `SafeHandle`)。
- **崩溃恢复**:启动时若发现 `.okv.lock` 但 PID 已死,清理并获取。

---

## 10. snapshot 文件格式

### 10.1 路径

`.okv.snapshots/<profile>/<entry-id>/<version>.entry.enc`

### 10.2 格式

```
Snapshot Header:
  Magic (4B)        "OKVS" (0x4F 4B 56 53)
  Snapshot Version  0x01 0x00
  EntryId (16B)     UUIDv7
  Version (4B)      uint32 LE
  CreatedAt (8B)    int64 ms UTC

Snapshot Payload (AEAD 加密, DEK):
  (同 Entry Payload,见 §5.6)

Signature (64B)     Ed25519 设备签名
```

---

## 11. seed.okv.dev 格式(OKVD)

### 11.1 设计差异

| 维度 | `vault.okv` (OKV1) | `seed.okv.dev` (OKVD) |
|---|---|---|
| Magic | `OKV1` (0x4F 4B 56 01) | `OKVD` (0x4F 4B 56 44) |
| 主密码保护 | ✅ | ❌(自带 dev-master-key) |
| 可含生产凭据 | ✅ | ❌(强制只导入 dev/test) |
| 用途 | 正常生产数据 | Dev / Test 种子 |

### 11.2 头部布局

```
偏移  长度  字段                      说明
─────────────────────────────────────────────────────────────
0     4     Magic                     "OKVD" (0x4F 4B 56 44)
4     2     Header Version            0x01 0x00
6     8     App Build Hash            前 8 字节 SHA-256
14    16    Seed UUID                 UUIDv7
30    32    Dev Master Key (明文)     32B 随机(用于派生 DEK,不要求主密码)
62    32    Dev Salt                  KDF 盐(用于派生 dev DEK,可选)
94    1     Strip Mode                0=full / 1=strip-secrets
95    var   Profiles (encrypted)      见 §5,但 DEK 由 Dev Master Key 派生
      64    Ed25519 Signature         签名前面所有字节
```

### 11.3 Strip Secrets 模式

- **`--strip-secrets`**:导出时,所有 `sensitive=true` 的 Field 的 `Value` 替换为 `"REDACTED-***"`。
- **用途**:团队间共享 dev 数据结构(字段、Tag、模板)而不泄露 secret。
- **检测**:头部 `Strip Mode = 1` 时,客户端导入后提示"sensitive 字段已剥离"。

### 11.4 安全声明

- `seed.okv.dev` **等同于明文分发**:任何拿到文件的人都能解密(因为有 Dev Master Key)。
- **强制隔离**:导入 `seed.okv.dev` 时,UI 弹出模态确认框,目标 Profile 仅限 `dev` / `test`。
- **视觉区分**:dev Profile 顶部红色 banner + `DEV` 水印,直到切换 Profile。

---

## 12. 版本兼容性策略

### 12.1 版本号体系

```
Magic (主版本): OKV1 / OKV2 / ...
Header Version (次版本): 0x01 0x00 / 0x01 0x01 / ...
Schema Version (领域次版本): 1 / 2 / ...
```

### 12.2 兼容规则

| 情形 | 行为 |
|---|---|
| 读侧遇到未知字段 | 忽略(前向兼容) |
| 写侧遇到当前 schema 不支持的旧字段 | 保留并标记 `legacy=true`(后向兼容) |
| 主版本号变化(`OKV1` → `OKV2`) | 客户端拒绝读写,提示升级 |
| 次版本号变化(`OKV1.0` → `OKV1.1`) | 旧版本只读,新版本可写 |

### 12.3 升级路径示例

```
v0.1 (OKV1, Header 1.0, Schema 1)
    │
    ▼ v0.2 新增 TOTP 字段
v0.2 (OKV1, Header 1.0, Schema 2)
    │ 旧客户端读 v0.2 文件 → 忽略 TOTP 字段
    │ 新客户端读 v0.1 文件 → TOTP 字段默认 null
    ▼
v0.3 新增附件 Blob(Header 1.1)
v0.3 (OKV1, Header 1.1, Schema 3)
    │ 旧客户端读 v0.3 文件 → 只读模式(Header 1.1 > 1.0)
    │ 新客户端读 v0.1 文件 → 自动升级到 1.1(写时)
    ▼
v2.0 引入后量子加密
v2.0 (OKV2, Header 2.0, Schema 4)
    │ v1 客户端拒绝读写 OKV2,提示升级
    │ v2 客户端可读 OKV1(自动升级到 OKV2,写时)
```

### 12.4 升级触发

- **读时**:客户端检测头部版本,若高于当前支持,提示"文件由更新版本创建,请升级"。
- **写时**:客户端按当前支持的最高版本写入(自动升级)。
- **手动升级**:设置中提供"升级 Vault 格式"按钮,显式升级到最新版本。

---

## 13. 校验与签名

### 13.1 签名范围

Ed25519 签名覆盖文件头**除签名字段本身**外的所有字节:

```
签名输入 = Magic + Header Version + App Build Hash + Vault UUID +
           Argon2id Params + KDF Salt + MK Verify Tag +
           Device Ed25519 PK + Encrypted Profiles + Vault VectorClock
签名输出 = 64B Ed25519 签名
```

### 13.2 验证流程

```
1. 读取 .okv 文件
2. 解析头部,提取签名外的所有字段
3. 提取签名 + Device Ed25519 PK
4. 在 manifest.json 的 device_public_keys 中查找该 PK
5. 若找到 → Ed25519 验证;若未找到 → 提示"未知设备,是否信任?"
6. 验证通过 → 继续 KDF + 解密
7. 验证失败 → 拒绝加载,提示"文件可能被篡改"
```

### 13.3 AEAD 完整性

- 每个 Profile Section、每个 Entry Payload、每个附件 chunk 独立 AEAD 加密。
- 任何字节篡改 → Poly1305 MAC 验证失败 → 抛 `CryptoException`。
- AEAD 失败**不返回部分明文**(libsodium 保证)。

---

## 14. 序列化与反序列化约束

### 14.1 实现要求

- **二进制读写**:使用 `System.IO.BinaryReader` / `BinaryWriter` 或自定义 `Span<byte>` 读写器。
- **UTF-8**:所有字符串 UTF-8 编码,`Encoding.UTF8.GetBytes`。
- **UUIDv7**:使用 `Guid` + 自定义 UUIDv7 生成器(时间戳 + 随机)。
- **流式**:Profile Section 与 Entries 支持流式读写,避免 OOM。

### 14.2 解析容错

- **未知字段**:Header Version 内的未知字段按 §12.2 忽略。
- **长度异常**:若 length-prefixed 字段长度超过合理上限(如 string > 1 MiB),抛 `InvalidDataException`。
- **EOF**:解析中遇到意外 EOF,抛 `InvalidDataException`,不返回部分结果。

### 14.3 性能目标

| 操作 | 目标(1 万条目) |
|---|---|
| 全量序列化 | ≤500ms |
| 全量反序列化 | ≤800ms(含解密) |
| 单条目读取 | ≤10ms |
| 单条目写入 | ≤50ms(含 B-tree 更新) |

---

## 15. 测试用例要求

### 15.1 格式一致性测试

| 用例 | 验证 |
|---|---|
| `FMT-RW-01` | 写入后读取,数据一致 |
| `FMT-RW-02` | 1 万条目读写性能达标 |
| `FMT-RW-03` | 含特殊字符(Unicode、换行、引号)的字符串读写 |
| `FMT-RW-04` | 空 Vault(无 Profile)读写 |
| `FMT-RW-05` | 空 Profile(无 Entry)读写 |

### 15.2 兼容性测试

| 用例 | 验证 |
|---|---|
| `FMT-COMPAT-01` | v0.1 客户端读取 v0.2 文件 → 忽略未知字段 |
| `FMT-COMPAT-02` | v0.2 客户端读取 v0.1 文件 → 默认值填充 |
| `FMT-COMPAT-03` | v1 客户端读取 OKV2 → 拒绝并提示升级 |

### 15.3 完整性测试

| 用例 | 验证 |
|---|---|
| `FMT-INTEG-01` | 篡改 1 字节头部 → 签名验证失败 |
| `FMT-INTEG-02` | 篡改 1 字节 Profile Section → AEAD 失败 |
| `FMT-INTEG-03` | 篡改 1 字节 Entry Payload → AEAD 失败 |
| `FMT-INTEG-04` | 未知设备签名 → 提示信任 |

### 15.4 崩溃恢复测试

| 用例 | 验证 |
|---|---|
| `FMT-RECOV-01` | 写入中崩溃(留 .okv.tmp) → 重启清理 |
| `FMT-RECOV-02` | rename 前崩溃 → 旧 .okv 完好 |
| `FMT-RECOV-03` | .okv.lock 残留 + PID 已死 → 自动清理 |

---

## 16. 附录

### 16.1 字段长度速查

| 字段 | 长度 |
|---|---|
| Magic | 4B |
| Header Version | 2B |
| App Build Hash | 8B |
| UUID | 16B |
| Argon2id m | 4B |
| Argon2id t | 4B |
| Argon2id p | 1B |
| KDF Salt | 32B |
| MK Verify Tag | 32B |
| Ed25519 Public Key | 32B |
| Ed25519 Signature | 64B |
| XChaCha20 nonce | 24B |
| Poly1305 tag | 16B |
| DEK Wrapped | 96B(24 + 40 + 16) |

### 16.2 魔数速查

| Magic | 十六进制 | 用途 |
|---|---|---|
| `OKV1` | 0x4F 4B 56 01 | 生产 Vault 文件 |
| `OKVD` | 0x4F 4B 56 44 | Dev 种子文件 |
| `OKVS` | 0x4F 4B 56 53 | Snapshot 文件 |
| `OKV2` | 0x4F 4B 56 02 | (预留)后量子加密版本 |

### 16.3 修订记录

| 版本 | 日期 | 修订 |
|---|---|---|
| v0.1 | 2026-06-18 | 初稿,覆盖 OKV1 + OKVD + OKVS 格式 |
| **0.2** | **2026-06-24** | **顶部 cross-ref 更新(PRD.md → MANUAL.md);二进制格式与字段定义不变** |
