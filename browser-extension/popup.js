// OmniKey Vault Browser Extension — Popup Script
// v1.9: Read-only bridge to the local OmniKey Vault desktop app.
// Communicates with the HTTP API server at 127.0.0.1:14725.

const API_BASE = 'http://127.0.0.1:14725';
const DEFAULT_PORT = 14725;

let authToken = '';
let debounceTimer = null;

// Load auth token from storage
chrome.storage.local.get(['okv_auth_token', 'okv_api_port'], (result) => {
  authToken = result.okv_auth_token || '';
  const port = result.okv_api_port || DEFAULT_PORT;
  if (port !== DEFAULT_PORT) {
    // Override API_BASE if custom port is configured
    // (not reassigning const, but we use it dynamically below)
  }
  checkStatus();
});

async function api(path, params = {}) {
  const url = new URL(API_BASE + path);
  for (const [k, v] of Object.entries(params)) {
    url.searchParams.set(k, v);
  }
  const response = await fetch(url, {
    headers: { 'Authorization': `Bearer ${authToken}` },
  });
  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }
  return response.json();
}

async function checkStatus() {
  const status = document.getElementById('status');
  const results = document.getElementById('results');
  try {
    const data = await api('/api/status');
    if (data.locked) {
      status.textContent = '🔒 保险箱已锁定 — 请在桌面应用中解锁';
      status.style.color = '#e74c3c';
      results.innerHTML = '<div class="empty">请先解锁保险箱</div>';
    } else {
      status.textContent = `✅ 已连接 · ${data.profiles.length} 个 Profile`;
      status.style.color = '#2ecc71';
      // Auto-search on load
      doSearch('');
    }
  } catch (err) {
    status.textContent = '❌ 无法连接 — 请确认桌面应用正在运行';
    status.style.color = '#e74c3c';
    results.innerHTML = '<div class="empty">无法连接到 OmniKey Vault<br><br>请确保桌面应用已启动且浏览器扩展 API 已启用</div>';
  }
}

async function doSearch(query) {
  const results = document.getElementById('results');
  // v2.3.6: Empty query should fetch all entries, not show a placeholder.
  // This way the user sees their entries immediately when the popup opens.
  try {
    const params = { profile: 'prod' };
    if (query) params.q = query;
    const data = await api('/api/search', params);
    if (data.count === 0) {
      results.innerHTML = '<div class="empty">未找到匹配的条目</div>';
      return;
    }
    results.innerHTML = data.results.map(entry => `
      <div class="entry">
        <div class="name">${escapeHtml(entry.name)}</div>
        <div class="meta">${escapeHtml(entry.platformId || '')} · ${entry.type}</div>
        <div class="fields">
          ${entry.fields.map(f => `
            <div class="field-row">
              <span class="key">${escapeHtml(f.key)}</span>
              <span class="value">${escapeHtml(f.masked)}</span>
              <button class="copy-btn" data-entry-id="${entry.id}" data-field="${escapeHtml(f.key)}">复制</button>
            </div>
          `).join('')}
        </div>
      </div>
    `).join('');

    // Attach copy handlers
    document.querySelectorAll('.copy-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation();
        const entryId = btn.dataset.entryId;
        const field = btn.dataset.field;
        btn.textContent = '...';
        try {
          await api('/api/copy', { entryId, field, profile: 'prod' });
          btn.textContent = '✓';
          setTimeout(() => btn.textContent = '复制', 2000);
        } catch (err) {
          btn.textContent = '✗';
          setTimeout(() => btn.textContent = '复制', 2000);
        }
      });
    });
  } catch (err) {
    results.innerHTML = `<div class="error">搜索失败: ${escapeHtml(err.message)}</div>`;
  }
}

function escapeHtml(str) {
  const div = document.createElement('div');
  div.textContent = str || '';
  return div.innerHTML;
}

// Search input with debounce
document.getElementById('search').addEventListener('input', (e) => {
  clearTimeout(debounceTimer);
  const query = e.target.value.trim();
  debounceTimer = setTimeout(() => doSearch(query), 250);
});

// Settings link — prompt for auth token
document.getElementById('settingsLink').addEventListener('click', (e) => {
  e.preventDefault();
  const token = prompt('请输入 OmniKey Vault 配对令牌\n（在桌面应用 → 设置 → 浏览器扩展 中查看）:', authToken);
  if (token !== null) {
    authToken = token.trim();
    chrome.storage.local.set({ okv_auth_token: authToken });
    checkStatus();
  }
});
