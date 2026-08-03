// OmniKey Vault Browser Extension — Popup Script
// v2.4.0: Auto-fill, icon status indicator, recent entries quick access
// Communicates with the HTTP API server at 127.0.0.1:14725 (configurable).

const DEFAULT_PORT = 14725;

let apiBase = `http://127.0.0.1:${DEFAULT_PORT}`;
let authToken = '';
let debounceTimer = null;
let currentProfile = 'prod';
let availableProfiles = [];
let selectedAutofillEntry = null;

// Load auth token and port from storage
chrome.storage.local.get(['okv_auth_token', 'okv_api_port', 'okv_profile'], (result) => {
  authToken = result.okv_auth_token || '';
  const port = result.okv_api_port || DEFAULT_PORT;
  apiBase = `http://127.0.0.1:${port}`;
  currentProfile = result.okv_profile || 'prod';
  checkStatus();
  renderRecentEntries();
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
  const autofillBar = document.getElementById('autofillBar');
  try {
    const data = await api('/api/status');
    if (data.locked) {
      status.textContent = '🔒 保险箱已锁定 — 请在桌面应用中解锁';
      status.style.color = '#e74c3c';
      results.innerHTML = '<div class="empty">请先解锁保险箱</div>';
      profileSelector.style.display = 'none';
      autofillBar.style.display = 'none';
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

      // Show auto-fill bar (will be activated when an entry is selected)
      autofillBar.style.display = 'block';

      // Auto-search on load
      doSearch('');
    }
  } catch (err) {
    status.textContent = '❌ 无法连接 — 请确认桌面应用正在运行';
    status.style.color = '#e74c3c';
    results.innerHTML = '<div class="empty">无法连接到 OmniKey Vault<br><br>请确保桌面应用已启动且浏览器扩展 API 已启用</div>';
    profileSelector.style.display = 'none';
    autofillBar.style.display = 'none';
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
      container.querySelectorAll('.profile-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      doSearch(document.getElementById('search').value.trim());
    });
    container.appendChild(btn);
  });
}

async function doSearch(query) {
  const results = document.getElementById('results');
  const recentSection = document.getElementById('recentSection');
  try {
    const params = { profile: currentProfile };
    if (query) params.q = query;
    const data = await api('/api/search', params);
    if (data.count === 0) {
      results.innerHTML = '<div class="empty">未找到匹配的条目</div>';
      recentSection.style.display = 'none';
      return;
    }

    // Show recent section only when not searching
    recentSection.style.display = query ? 'none' : 'block';

    results.innerHTML = data.results.map(entry => `
      <div class="entry" data-entry-id="${entry.id}">
        <div class="name">${escapeHtml(entry.name)}</div>
        <div class="meta">${escapeHtml(entry.platformId || '')} · ${entry.type}</div>
        <div class="fields">
          ${entry.fields.map(f => `
            <div class="field-row">
              <span class="key">${escapeHtml(f.key)}</span>
              <span class="value">${escapeHtml(f.masked)}</span>
              <div class="btn-group">
                <button class="copy-btn" data-entry-id="${entry.id}" data-field="${escapeHtml(f.key)}">复制</button>
              </div>
            </div>
          `).join('')}
        </div>
        <div style="margin-top: 6px;">
          <button class="fill-btn" data-entry-id="${entry.id}" data-entry-name="${escapeHtml(entry.name)}" style="padding: 4px 10px; border: 1px solid #533483; border-radius: 4px; background: transparent; color: #533483; cursor: pointer; font-size: 11px;">⚡ 自动填充当前页面</button>
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
          // Track as recent
          trackRecentEntry(entryId, btn.closest('.entry'));
        } catch (err) {
          btn.textContent = '✗';
          setTimeout(() => btn.textContent = '复制', 2000);
        }
      });
    });

    // Attach auto-fill handlers
    document.querySelectorAll('.fill-btn').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation();
        const entryId = btn.dataset.entryId;
        btn.textContent = '...';
        try {
          // Fetch actual field values for auto-fill
          const data = await api('/api/autofill', { entryId, profile: currentProfile });
          if (data.success && data.fields) {
            // Send to content script via background
            chrome.runtime.sendMessage({
              type: 'AUTOFILL_ENTRY',
              entry: { id: entryId, name: btn.dataset.entryName },
              fields: data.fields,
            }, (response) => {
              if (response && response.success) {
                btn.textContent = `✓ 已填充 ${response.filled}/${response.total}`;
                setTimeout(() => btn.textContent = '⚡ 自动填充当前页面', 3000);
                trackRecentEntry(entryId, btn.closest('.entry'));
              } else {
                btn.textContent = '✗ 无可填充字段';
                setTimeout(() => btn.textContent = '⚡ 自动填充当前页面', 2000);
              }
            });
          } else {
            btn.textContent = '✗ 获取数据失败';
            setTimeout(() => btn.textContent = '⚡ 自动填充当前页面', 2000);
          }
        } catch (err) {
          btn.textContent = '✗ ' + err.message;
          setTimeout(() => btn.textContent = '⚡ 自动填充当前页面', 2000);
        }
      });
    });
  } catch (err) {
    results.innerHTML = `<div class="error">搜索失败: ${escapeHtml(err.message)}</div>`;
  }
}

function trackRecentEntry(entryId, entryElement) {
  // Get the entry name from the DOM
  const nameEl = entryElement?.querySelector('.name');
  const metaEl = entryElement?.querySelector('.meta');
  const entry = {
    id: entryId,
    name: nameEl ? nameEl.textContent : '',
    meta: metaEl ? metaEl.textContent : '',
    time: Date.now(),
  };
  chrome.runtime.sendMessage({ type: 'TRACK_RECENT', entry: entry });
}

async function renderRecentEntries() {
  chrome.storage.local.get(['okv_recent_entries'], (result) => {
    const recent = result.okv_recent_entries || [];
    const recentList = document.getElementById('recentList');
    const recentSection = document.getElementById('recentSection');

    if (recent.length === 0) {
      recentSection.style.display = 'none';
      return;
    }

    recentSection.style.display = 'block';
    recentList.innerHTML = recent.map(entry => `
      <div class="recent-entry" data-entry-id="${entry.id}">
        <span class="icon">🔑</span>
        <div class="info">
          <div class="name">${escapeHtml(entry.name)}</div>
          <div class="time">${formatTime(entry.time)}</div>
        </div>
        <div class="actions">
          <button class="recent-copy" data-entry-id="${entry.id}">复制</button>
          <button class="recent-fill" data-entry-id="${entry.id}" data-entry-name="${escapeHtml(entry.name)}">填充</button>
        </div>
      </div>
    `).join('');

    // Attach handlers for recent entries
    document.querySelectorAll('.recent-copy').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation();
        const entryId = btn.dataset.entryId;
        btn.textContent = '...';
        try {
          // Search for the entry to get its fields
          const data = await api('/api/search', { q: '', profile: currentProfile });
          const entry = data.results.find(r => r.id === entryId);
          if (entry && entry.fields.length > 0) {
            await api('/api/copy', { entryId, field: entry.fields[0].key, profile: currentProfile });
            btn.textContent = '✓';
            setTimeout(() => btn.textContent = '复制', 2000);
          } else {
            btn.textContent = '✗';
            setTimeout(() => btn.textContent = '复制', 2000);
          }
        } catch (err) {
          btn.textContent = '✗';
          setTimeout(() => btn.textContent = '复制', 2000);
        }
      });
    });

    document.querySelectorAll('.recent-fill').forEach(btn => {
      btn.addEventListener('click', async (e) => {
        e.stopPropagation();
        const entryId = btn.dataset.entryId;
        btn.textContent = '...';
        try {
          const data = await api('/api/autofill', { entryId, profile: currentProfile });
          if (data.success && data.fields) {
            chrome.runtime.sendMessage({
              type: 'AUTOFILL_ENTRY',
              entry: { id: entryId, name: btn.dataset.entryName },
              fields: data.fields,
            }, (response) => {
              if (response && response.success) {
                btn.textContent = `✓ ${response.filled}`;
                setTimeout(() => btn.textContent = '填充', 2000);
              } else {
                btn.textContent = '✗';
                setTimeout(() => btn.textContent = '填充', 2000);
              }
            });
          } else {
            btn.textContent = '✗';
            setTimeout(() => btn.textContent = '填充', 2000);
          }
        } catch (err) {
          btn.textContent = '✗';
          setTimeout(() => btn.textContent = '填充', 2000);
        }
      });
    });
  });
}

function formatTime(timestamp) {
  const diff = Date.now() - timestamp;
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return '刚刚';
  if (mins < 60) return `${mins} 分钟前`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours} 小时前`;
  const days = Math.floor(hours / 24);
  return `${days} 天前`;
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
