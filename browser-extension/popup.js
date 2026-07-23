// OmniKey Vault Browser Extension — Popup Script
// v1.9: Read-only bridge to the local OmniKey Vault desktop app.
// Communicates with the HTTP API server at 127.0.0.1:14725.
// v2.3.7: Fix custom port not working + add profile selector.

const DEFAULT_PORT = 14725;

let apiBase = `http://127.0.0.1:${DEFAULT_PORT}`;
let authToken = '';
let debounceTimer = null;
let currentProfile = 'prod';
let availableProfiles = [];

// Load auth token and port from storage
chrome.storage.local.get(['okv_auth_token', 'okv_api_port', 'okv_profile'], (result) => {
  authToken = result.okv_auth_token || '';
  const port = result.okv_api_port || DEFAULT_PORT;
  apiBase = `http://127.0.0.1:${port}`;
  currentProfile = result.okv_profile || 'prod';
  checkStatus();
});

async function api(path, params = {}) {
  const url = new URL(apiBase + path);
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
  const profileSelector = document.getElementById('profileSelector');
  try {
    const data = await api('/api/status');
    if (data.locked) {
      status.textContent = '🔒 保险箱已锁定 — 请在桌面应用中解锁';
      status.style.color = '#e74c3c';
      results.innerHTML = '<div class="empty">请先解锁保险箱</div>';
      profileSelector.style.display = 'none';
    } else {
      status.textContent = `✅ 已连接 · ${data.profiles.length} 个 Profile`;
      status.style.color = '#2ecc71';

      // v2.3.7: Build profile selector from API response
      availableProfiles = data.profiles || ['prod'];
      if (availableProfiles.length > 0 && !availableProfiles.includes(currentProfile)) {
        currentProfile = availableProfiles[0];
      }
      buildProfileSelector();
      profileSelector.style.display = availableProfiles.length > 1 ? 'flex' : 'none';

      // Auto-search on load
      doSearch('');
    }
  } catch (err) {
    status.textContent = '❌ 无法连接 — 请确认桌面应用正在运行';
    status.style.color = '#e74c3c';
    results.innerHTML = '<div class="empty">无法连接到 OmniKey Vault<br><br>请确保桌面应用已启动且浏览器扩展 API 已启用</div>';
    profileSelector.style.display = 'none';
  }
}

function buildProfileSelector() {
  const container = document.getElementById('profileSelector');
  container.innerHTML = '';
  availableProfiles.forEach(p => {
    const btn = document.createElement('button');
    btn.className = 'profile-btn' + (p === currentProfile ? ' active' : '');
    btn.textContent = p;
    btn.addEventListener('click', () => {
      currentProfile = p;
      chrome.storage.local.set({ okv_profile: p });
      // Update active styles
      container.querySelectorAll('.profile-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      doSearch(document.getElementById('search').value.trim());
    });
    container.appendChild(btn);
  });
}

async function doSearch(query) {
  const results = document.getElementById('results');
  // v2.3.6: Empty query should fetch all entries, not show a placeholder.
  // v2.3.7: Use currentProfile instead of hardcoded 'prod'.
  try {
    const params = { profile: currentProfile };
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
          await api('/api/copy', { entryId, field, profile: currentProfile });
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

// Settings link — prompt for auth token and port
document.getElementById('settingsLink').addEventListener('click', (e) => {
  e.preventDefault();
  const currentPort = apiBase.match(/:(\d+)$/)?.[1] || DEFAULT_PORT;
  const token = prompt('请输入 OmniKey Vault 配对令牌\n（在桌面应用 → 设置 → 浏览器扩展 中查看）:', authToken);
  if (token !== null) {
    authToken = token.trim();
    const portStr = prompt('请输入 API 端口（默认 14725）:', currentPort);
    if (portStr !== null) {
      const port = parseInt(portStr.trim()) || DEFAULT_PORT;
      apiBase = `http://127.0.0.1:${port}`;
      chrome.storage.local.set({ okv_auth_token: authToken, okv_api_port: port });
    } else {
      chrome.storage.local.set({ okv_auth_token: authToken });
    }
    checkStatus();
  }
});
