# OmniKey Vault Browser Extension (只读桥接)

v1.9 浏览器扩展，提供只读访问 OmniKey Vault 保险箱的能力。

## 功能

- 搜索保险箱中的条目
- 查看条目字段（敏感字段仅显示掩码）
- 一键复制字段值到剪贴板（通过本地 API，不直接传输明文）

## 安装

### Chrome / Edge
1. 打开 `chrome://extensions/`
2. 开启"开发者模式"
3. 点击"加载已解压的扩展程序"
4. 选择 `browser-extension/` 目录

### Firefox
1. 打开 `about:debugging#/runtime/this-firefox`
2. 点击"临时载入附加组件"
3. 选择 `browser-extension/manifest.json`

## 配对

1. 在 OmniKey Vault 桌面应用中，打开 设置 → 浏览器扩展
2. 启用浏览器扩展 API
3. 复制配对令牌
4. 点击浏览器扩展弹窗底部的"⚙ 配对设置"
5. 粘贴令牌并确认

## 安全模型

- API 服务器仅监听 `127.0.0.1`（本地回环），不可从网络访问
- CORS 限制为 `chrome-extension://` 和 `moz-extension://` 来源
- 所有响应均为只读，无修改操作
- 敏感字段值**永不通过 HTTP 传输**，仅支持复制到剪贴板
- 需要Bearer令牌认证（每次会话生成）
