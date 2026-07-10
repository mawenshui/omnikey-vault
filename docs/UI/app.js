/* ============================================================
   OmniKey Vault — Prototype Interactions
   Implements: unlock flow, search, copy with countdown,
   profile switch with banner, secret reveal, TOTP, sync.
   ============================================================ */

(() => {
  'use strict';

  // ---------- Sample data ----------
  const PROFILES = {
    prod: {
      color: 'prod', bannerText: '', watermark: '',
      entries: [
        {
          id: '01H7XGK1ABC', name: 'OpenAI 生产环境',
          platform: 'openai', platformMark: 'O',
          type: 'api_key', folder: 'AI 服务',
          tags: ['ai', 'work', 'prod'],
          updated: '3 天前',
          fields: [
            { key: 'api_key', value: 'sk-proj-abc123XYZdef456GHI789JKLmno012PQRstu345UVW', kind: '密文', sensitive: true },
            { key: 'organization_id', value: 'org-aBcDeFgHiJkLmNoPqRsT', kind: '文本', sensitive: false },
            { key: 'project_id', value: 'proj_xYzAbCdEfGhIjKlMnOp', kind: '文本', sensitive: false },
          ],
          notes: '生产环境 OpenAI Key,用于主 API 服务。上次轮换:2026-05-12。',
          created: '2026-05-01', updated_at: '2026-06-15', version: 3,
        },
        {
          id: '01H7XGK2DEF', name: 'AWS 生产环境',
          platform: 'aws', platformMark: 'A',
          type: 'api_key', folder: '云服务',
          tags: ['cloud', 'prod'],
          updated: '8 天前',
          fields: [
            { key: 'access_key_id', value: 'AKIAIOSFODNN7EXAMPLE', kind: '文本', sensitive: false },
            { key: 'secret_access_key', value: 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY', kind: '密文', sensitive: true },
            { key: 'region', value: 'us-east-1', kind: '文本', sensitive: false },
            { key: 'account_id', value: '123456789012', kind: '文本', sensitive: false },
          ],
          notes: '主 AWS 生产账户。日常作业请使用带 MFA 的 assumable role。',
          created: '2026-04-22', updated_at: '2026-06-10', version: 2,
        },
        {
          id: '01H7XGK3GHI', name: 'GitHub 个人 PAT',
          platform: 'github', platformMark: 'G',
          type: 'api_key', folder: 'AI 服务',
          tags: ['dev', 'personal'],
          updated: '14 天前',
          fields: [
            { key: 'personal_access_token', value: 'ghp_abcdefghijklmnopqrstuvwxyz0123456789AB', kind: '密文', sensitive: true },
            { key: 'username', value: 'sisyphus-dev', kind: '文本', sensitive: false },
          ],
          notes: '个人 GitHub PAT。权限范围:repo、read:org、workflow。',
          created: '2026-03-15', updated_at: '2026-06-04', version: 4,
        },
        {
          id: '01H7XGK4JKL', name: 'Stripe 正式环境',
          platform: 'stripe', platformMark: 'S',
          type: 'api_key', folder: '支付',
          tags: ['pay', 'prod'],
          updated: '20 天前',
          fields: [
            { key: 'secret_key', value: 'sk_live_abcDEF123ghiJKL456mnoPQR789stuUVW012xyz', kind: '密文', sensitive: true },
            { key: 'publishable_key', value: 'pk_live_abcDEF123ghiJKL456mnoPQR', kind: '文本', sensitive: false },
            { key: 'webhook_secret', value: 'whsec_abc123def456ghi789jkl012mno345', kind: '密文', sensitive: true },
          ],
          notes: '生产环境 Stripe 密钥。Webhook secret 已配置到主端点。',
          created: '2026-02-10', updated_at: '2026-05-29', version: 5,
        },
        {
          id: '01H7XGK5MNO', name: 'Supabase 主项目',
          platform: 'supabase', platformMark: 'U',
          type: 'api_key', folder: '数据库',
          tags: ['db', 'work'],
          updated: '1 个月前',
          fields: [
            { key: 'anon_key', value: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZmVyZW5jZSI6ImFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6In0.anon_key_example', kind: '文本', sensitive: false },
            { key: 'service_role_key', value: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJvbGUiOiJzZXJ2aWNlX3JvbGUifQ.service_role_example_secret', kind: '密文', sensitive: true },
            { key: 'project_url', value: 'https://abcdefghijklmnop.supabase.co', kind: '链接', sensitive: false },
          ],
          notes: '主 Supabase 项目。service_role_key 严禁暴露到客户端。',
          created: '2026-01-20', updated_at: '2026-05-18', version: 2,
        },
      ],
    },
    dev: {
      color: 'dev', bannerText: 'DEV — 非生产数据',
      watermark: 'dev',
      entries: [
        {
          id: '01H7DEV1ABC', name: 'OpenAI 测试',
          platform: 'openai', platformMark: 'O',
          type: 'api_key', folder: 'AI 服务',
          tags: ['ai', 'test'],
          updated: '1 天前',
          fields: [
            { key: 'api_key', value: 'sk-test-abc123def456ghi789jkl012mno345pqr678', kind: '密文', sensitive: true },
            { key: 'organization_id', value: 'org-test-abcdefghij', kind: '文本', sensitive: false },
          ],
          notes: '测试用 OpenAI Key,已限速。用于 CI 测试夹具。',
          created: '2026-06-10', updated_at: '2026-06-17', version: 1,
        },
        {
          id: '01H7DEV2DEF', name: 'AWS 沙箱',
          platform: 'aws', platformMark: 'A',
          type: 'api_key', folder: '云服务',
          tags: ['cloud', 'test'],
          updated: '2 天前',
          fields: [
            { key: 'access_key_id', value: 'AKIA TEST SANDBOX 0123', kind: '文本', sensitive: false },
            { key: 'secret_access_key', value: 'sandboxTestSecretKeyForDevUseOnly123', kind: '密文', sensitive: true },
            { key: 'region', value: 'us-west-2', kind: '文本', sensitive: false },
          ],
          notes: '沙箱 AWS 账户。资源每晚自动销毁。',
          created: '2026-06-01', updated_at: '2026-06-16', version: 1,
        },
      ],
    },
    test: {
      color: 'test', bannerText: 'TEST — 测试数据',
      watermark: 'test',
      entries: [],
    },
  };

  // ---------- State ----------
  const state = {
    screen: 'unlock',
    profile: 'prod',
    selectedEntryId: null,
    searchQuery: '',
    activeTag: null,
    lockMinutesLeft: 15,
    syncStatus: 'synced', // synced | syncing | error | paused
    clipboardTimer: null,
    totpTimer: null,
    lockTimer: null,
  };

  // ---------- DOM helpers ----------
  const $ = (id) => document.getElementById(id);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
  const el = (tag, props = {}, ...children) => {
    const node = document.createElement(tag);
    Object.entries(props).forEach(([k, v]) => {
      if (k === 'class') node.className = v;
      else if (k === 'html') node.innerHTML = v;
      else if (k.startsWith('on')) node.addEventListener(k.slice(2).toLowerCase(), v);
      else if (k === 'dataset') Object.entries(v).forEach(([dk, dv]) => (node.dataset[dk] = dv));
      else node.setAttribute(k, v);
    });
    children.flat().forEach((c) => {
      if (c == null) return;
      node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    });
    return node;
  };

  // ---------- Screen switching ----------
  function showScreen(name) {
    state.screen = name;
    $$('.screen').forEach((s) => s.classList.remove('active'));
    const target = $(`screen-${name}`);
    if (target) target.classList.add('active');
  }

  // ---------- Unlock flow ----------
  function initUnlock() {
    const form = $('unlock-form');
    const pwInput = $('master-pw');
    const revealBtn = $('reveal-pw');
    const unlockBtn = $('unlock-btn');
    const kdfOverlay = $('kdf-overlay');
    const recoveryBtn = $('recovery-btn');

    // Reveal toggle (hold to show)
    let pwRevealed = false;
    revealBtn.addEventListener('mousedown', () => {
      pwInput.type = 'text';
      pwRevealed = true;
    });
    ['mouseup', 'mouseleave'].forEach((evt) =>
      revealBtn.addEventListener(evt, () => {
        if (pwRevealed) {
          pwInput.type = 'password';
          pwRevealed = false;
        }
      })
    );

    form.addEventListener('submit', (e) => {
      e.preventDefault();
      if (!pwInput.value) {
        pwInput.focus();
        return;
      }
      // Simulate KDF
      unlockBtn.classList.add('loading');
      kdfOverlay.classList.add('visible');
      // Restart progress ring animation
      const ring = kdfOverlay.querySelector('.kdf-progress-ring');
      ring.style.animation = 'none';
      void ring.offsetWidth;
      ring.style.animation = '';

      setTimeout(() => {
        unlockBtn.classList.remove('loading');
        kdfOverlay.classList.remove('visible');
        pwInput.value = '';
        showScreen('main');
        startLockCountdown();
        startSyncSimulation();
        renderEntries();
        toast('保险库已解锁 · 已加载 5 个条目', 'success');
      }, 1700);
    });

    recoveryBtn.addEventListener('click', () => {
      toast('恢复密钥流程 — 仅原型演示', 'info');
    });
  }

  // ---------- Entry rendering ----------
  function getCurrentEntries() {
    return PROFILES[state.profile].entries;
  }

  function filterEntries(entries) {
    const q = state.searchQuery.trim().toLowerCase();
    const tag = state.activeTag;
    return entries.filter((e) => {
      if (tag && !e.tags.includes(tag)) return false;
      if (!q) return true;
      if (e.name.toLowerCase().includes(q)) return true;
      if (e.platform.toLowerCase().includes(q)) return true;
      if (e.tags.some((t) => t.toLowerCase().includes(q))) return true;
      if (e.fields.some((f) => f.key.toLowerCase().includes(q) || f.value.toLowerCase().includes(q))) return true;
      return false;
    });
  }

  function maskValue(value, reveal = false) {
    if (reveal) return value;
    if (value.length <= 8) return '••••••••';
    return value.slice(0, 3) + '•••••••••••••••••' + value.slice(-4);
  }

  function renderEntries() {
    const body = $('entry-list-body');
    const empty = $('list-empty');
    const listCount = $('list-count');
    const listTitle = $('list-title');

    const all = getCurrentEntries();
    const filtered = filterEntries(all);

    listTitle.textContent = state.activeTag ? `#${state.activeTag}` : '全部条目';
    listCount.textContent = `${filtered.length} 个条目`;

    body.innerHTML = '';

    if (filtered.length === 0) {
      empty.hidden = false;
      body.hidden = true;
      return;
    }
    empty.hidden = true;
    body.hidden = false;

    filtered.forEach((entry, i) => {
      const row = el('div', {
        class: 'entry-row' + (entry.id === state.selectedEntryId ? ' selected' : ''),
        dataset: { id: entry.id },
        style: `animation-delay: ${Math.min(i * 40, 400)}ms`,
        onclick: () => selectEntry(entry.id),
      });

      // Platform mark
      const platform = el('div', {
        class: `entry-platform platform-${entry.platform}`,
      }, entry.platformMark);

      // Main
      const main = el('div', { class: 'entry-main' });
      const nameRow = el('div', { class: 'entry-name-row' },
        el('span', { class: 'entry-name' }, entry.name)
      );
      const primaryField = entry.fields.find((f) => f.sensitive) || entry.fields[0];
      const fieldDisplay = el('div', { class: 'entry-field-display' },
        el('span', { class: 'entry-field-key' }, primaryField.key + ':'),
        el('span', { class: 'entry-field-value' }, maskValue(primaryField.value))
      );
      const tags = el('div', { class: 'entry-tags' },
        ...entry.tags.map((t) => el('span', {
          class: 'entry-tag' + (t === state.activeTag || t === state.searchQuery.replace('#', '') ? ' match' : ''),
        }, '#' + t))
      );
      main.appendChild(nameRow);
      main.appendChild(fieldDisplay);
      main.appendChild(tags);

      // Meta
      const meta = el('div', { class: 'entry-meta' },
        el('span', { class: 'entry-time' }, entry.updated),
        el('button', {
          class: 'entry-copy-btn',
          title: '复制主字段',
          onclick: (e) => { e.stopPropagation(); copyToClipboard(primaryField.value, e.currentTarget); },
        },
        el('svg', { width: '14', height: '14', viewBox: '0 0 14 14', fill: 'none', html: '<rect x="3" y="3" width="7" height="7" rx="0.5" stroke="currentColor" stroke-width="1" fill="none"/><path d="M2 9 V2 H9" stroke="currentColor" stroke-width="1" fill="none"/>' }),
        el('div', { class: 'copy-ring', html: '<svg viewBox="0 0 32 32"><circle cx="16" cy="16" r="14"/></svg>' })
        )
      );

      row.appendChild(platform);
      row.appendChild(main);
      row.appendChild(meta);
      body.appendChild(row);
    });
  }

  // ---------- Entry selection ----------
  function selectEntry(id) {
    state.selectedEntryId = id;
    $$('.entry-row').forEach((r) => r.classList.toggle('selected', r.dataset.id === id));
    renderDetail();
  }

  function renderDetail() {
    const empty = $('detail-empty');
    const content = $('detail-content');
    if (!state.selectedEntryId) {
      empty.hidden = false;
      content.hidden = true;
      return;
    }
    const entry = getCurrentEntries().find((e) => e.id === state.selectedEntryId);
    if (!entry) {
      empty.hidden = false;
      content.hidden = true;
      return;
    }
    empty.hidden = true;
    content.hidden = false;
    content.innerHTML = '';

    // Header
    const header = el('div', { class: 'detail-header' },
      el('div', { class: `detail-platform platform-${entry.platform}` }, entry.platformMark),
      el('div', { class: 'detail-title-block' },
        el('div', { class: 'detail-title' }, entry.name),
        el('div', { class: 'detail-subtitle' }, `${entry.platform} · ${entry.type} · ${entry.id}`)
      ),
      el('div', { class: 'detail-actions' },
        el('button', {
          class: 'icon-btn',
          title: '编辑',
          onclick: openEditor,
          html: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none"><path d="M9 2 L12 5 L5 12 H2 V9 L9 2 Z" stroke="currentColor" stroke-width="1" fill="none" stroke-linejoin="round"/></svg>',
        })
      )
    );
    content.appendChild(header);

    // Fields
    const fieldsSection = el('div', { class: 'detail-section' },
      el('div', { class: 'detail-section-label' }, '字段')
    );
    entry.fields.forEach((f) => {
      const fieldEl = el('div', { class: 'detail-field' });
      const head = el('div', { class: 'detail-field-head' },
        el('span', { class: 'detail-field-key' }, f.key),
        el('span', { class: `detail-field-kind kind-${f.kind === '密文' ? 'secret' : 'text'}` }, f.kind)
      );
      const valueRow = el('div', { class: 'detail-field-value-row' });
      const valueEl = el('div', {
        class: 'detail-field-value masked',
        html: escapeHtml(maskValue(f.value)),
      });
      const exposedDot = el('span', { class: 'exposed-dot' });

      const actions = el('div', { class: 'detail-field-actions' });

      if (f.sensitive) {
        const revealBtn = el('button', {
          class: 'icon-btn icon-btn-sm',
          title: '按住显示明文',
          html: '<svg width="12" height="12" viewBox="0 0 12 12" fill="none"><path d="M1 6 Q6 1.5 11 6 Q6 10.5 1 6 Z" stroke="currentColor" stroke-width="1" fill="none"/><circle cx="6" cy="6" r="1.5" stroke="currentColor" stroke-width="1" fill="none"/></svg>',
        });
        let revealed = false;
        const reveal = () => {
          revealed = true;
          valueEl.classList.remove('masked');
          valueEl.classList.add('revealed');
          valueEl.textContent = f.value;
          exposedDot.classList.add('visible');
          revealBtn.style.color = 'var(--accent)';
        };
        const mask = () => {
          if (!revealed) return;
          revealed = false;
          valueEl.classList.add('masked');
          valueEl.classList.remove('revealed');
          valueEl.textContent = maskValue(f.value);
          exposedDot.classList.remove('visible');
          revealBtn.style.color = '';
        };
        revealBtn.addEventListener('mousedown', reveal);
        revealBtn.addEventListener('mouseup', mask);
        revealBtn.addEventListener('mouseleave', mask);
        actions.appendChild(revealBtn);
      }

      const copyBtn = el('button', {
        class: 'icon-btn icon-btn-sm',
        title: '复制',
        html: '<svg width="12" height="12" viewBox="0 0 12 12" fill="none"><rect x="3" y="3" width="6" height="6" rx="0.5" stroke="currentColor" stroke-width="1" fill="none"/><path d="M2 8 V2 H8" stroke="currentColor" stroke-width="1" fill="none"/></svg>',
        onclick: (e) => copyToClipboard(f.value, e.currentTarget),
      });
      actions.appendChild(copyBtn);

      valueRow.appendChild(valueEl);
      valueRow.appendChild(exposedDot);
      valueRow.appendChild(actions);
      fieldEl.appendChild(head);
      fieldEl.appendChild(valueRow);
      fieldsSection.appendChild(fieldEl);
    });
    content.appendChild(fieldsSection);

    // TOTP demonstration (if field kind is totp_uri — simulate for OpenAI)
    if (entry.platform === 'openai' && state.profile === 'prod') {
      const totpSection = el('div', { class: 'detail-section' },
        el('div', { class: 'detail-section-label' }, 'TOTP(演示)')
      );
      const totpDisplay = el('div', { class: 'totp-display' },
        el('div', { class: 'totp-code', id: 'totp-code' }, '—— ——'),
        el('div', { class: 'totp-ring' },
          el('svg', { html: '<circle cx="16" cy="16" r="14" stroke-dasharray="88" stroke-dashoffset="0" id="totp-ring-circle"/>' }),
          el('div', { class: 'totp-seconds', id: 'totp-seconds' }, '30')
        )
      );
      totpSection.appendChild(totpDisplay);
      content.appendChild(totpSection);
      startTotpCountdown();
    }

    // Notes
    if (entry.notes) {
      const notesSection = el('div', { class: 'detail-section' },
        el('div', { class: 'detail-section-label' }, '备注'),
        el('div', { class: 'detail-notes' }, entry.notes)
      );
      content.appendChild(notesSection);
    }

    // Meta
    const metaSection = el('div', { class: 'detail-section' },
      el('div', { class: 'detail-section-label' }, '元数据'),
      el('div', { class: 'detail-meta-grid' },
        el('div', { class: 'detail-meta-item' },
          el('div', { class: 'detail-meta-label' }, '创建于'),
          el('div', { class: 'detail-meta-value' }, entry.created)
        ),
        el('div', { class: 'detail-meta-item' },
          el('div', { class: 'detail-meta-label' }, '更新于'),
          el('div', { class: 'detail-meta-value' }, entry.updated_at)
        ),
        el('div', { class: 'detail-meta-item' },
          el('div', { class: 'detail-meta-label' }, '版本'),
          el('div', { class: 'detail-meta-value' }, 'v' + entry.version)
        ),
        el('div', { class: 'detail-meta-item' },
          el('div', { class: 'detail-meta-label' }, '文件夹'),
          el('div', { class: 'detail-meta-value' }, entry.folder)
        )
      )
    );
    content.appendChild(metaSection);
  }

  // ---------- TOTP countdown ----------
  function startTotpCountdown() {
    if (state.totpTimer) clearInterval(state.totpTimer);
    const codeEl = $('totp-code');
    const ringEl = $('totp-ring-circle');
    const secEl = $('totp-seconds');
    if (!codeEl || !ringEl || !secEl) return;

    const update = () => {
      const now = Math.floor(Date.now() / 1000);
      const remaining = 30 - (now % 30);
      const code = generateFakeTotp(now);
      codeEl.textContent = code.slice(0, 3) + ' ' + code.slice(3);
      secEl.textContent = remaining;
      const offset = 88 - (88 * remaining / 30);
      ringEl.setAttribute('stroke-dashoffset', offset);
    };
    update();
    state.totpTimer = setInterval(update, 1000);
  }

  function generateFakeTotp(seed) {
    // Deterministic pseudo-TOTP for demo
    const chars = '0123456789';
    let code = '';
    let x = seed;
    for (let i = 0; i < 6; i++) {
      x = (x * 1103515245 + 12345) & 0x7fffffff;
      code += chars[x % 10];
    }
    return code;
  }

  // ---------- Copy with 8s countdown ----------
  function copyToClipboard(value, btn) {
    if (state.clipboardTimer) {
      clearTimeout(state.clipboardTimer);
      state.clipboardTimer = null;
    }
    // Try clipboard API, fallback to console
    if (navigator.clipboard) {
      navigator.clipboard.writeText(value).catch(() => {});
    }

    // Visual: countdown ring
    btn.classList.add('counting', 'copied');
    const original = btn;
    setTimeout(() => original.classList.remove('copied'), 1500);

    // Simulate clipboard clear after 8 seconds
    state.clipboardTimer = setTimeout(() => {
      if (navigator.clipboard) navigator.clipboard.writeText('').catch(() => {});
      btn.classList.remove('counting');
      toast('剪贴板已清空', 'info');
      state.clipboardTimer = null;
    }, 8000);

    toast('已复制 · 8 秒后自动清空', 'success');
  }

  // ---------- Profile switcher ----------
  function initProfileSwitcher() {
    const chip = $('profile-chip');
    const overlay = $('profile-switcher-overlay');
    const closeBtn = $('switcher-close');
    const items = $$('.switcher-item');

    chip.addEventListener('click', () => overlay.classList.add('visible'));
    closeBtn.addEventListener('click', () => overlay.classList.remove('visible'));
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) overlay.classList.remove('visible');
    });

    items.forEach((item) => {
      item.addEventListener('click', () => {
        const profile = item.dataset.profile;
        switchProfile(profile);
        overlay.classList.remove('visible');
      });
    });

    $('new-profile-btn').addEventListener('click', () => {
      toast('新建配置文件流程 — 仅原型演示', 'info');
    });
  }

  function switchProfile(profile) {
    if (profile === state.profile) return;
    state.profile = profile;
    state.selectedEntryId = null;

    // Update chip
    const chip = $('profile-chip');
    chip.querySelector('.profile-dot').className = `profile-dot profile-dot-${PROFILES[profile].color}`;
    chip.querySelector('.profile-name').textContent = profile;
    const countEl = $(`profile-count-${profile}`);
    const countSpan = chip.querySelector('.profile-count');
    if (countSpan) countSpan.textContent = String(PROFILES[profile].entries.length);

    // Update status bar
    const statusProfile = $('status-profile');
    statusProfile.innerHTML = '';
    statusProfile.appendChild(el('span', { class: `status-dot status-dot-${PROFILES[profile].color}` }));
    statusProfile.appendChild(el('span', { class: 'mono' }, profile));

    // Banner + watermark
    const banner = $('profile-banner');
    const watermark = $('profile-watermark');
    banner.classList.remove('banner-test');
    watermark.classList.remove('watermark-test');

    if (profile === 'dev' || profile === 'test') {
      banner.classList.add('visible');
      watermark.classList.add('visible');
      $('banner-text').textContent = PROFILES[profile].bannerText;
      watermark.textContent = PROFILES[profile].watermark;
      if (profile === 'test') {
        banner.classList.add('banner-test');
        watermark.classList.add('watermark-test');
      }
    } else {
      banner.classList.remove('visible');
      watermark.classList.remove('visible');
    }

    renderEntries();
    renderDetail();
    toast(`已切换到 ${profile} 配置文件`, 'info');
  }

  // ---------- Search ----------
  function initSearch() {
    const input = $('search-input');
    input.addEventListener('input', () => {
      state.searchQuery = input.value;
      state.activeTag = null;
      $$('.tag-chip').forEach((c) => c.classList.remove('active'));
      renderEntries();
    });

    // Keyboard shortcut: / to focus search
    document.addEventListener('keydown', (e) => {
      if (e.key === '/' && document.activeElement !== input && state.screen === 'main') {
        e.preventDefault();
        input.focus();
      }
      if (e.key === 'Escape') {
        if (input.value) {
          input.value = '';
          state.searchQuery = '';
          renderEntries();
        }
      }
    });
  }

  // ---------- Tag filtering ----------
  function initTags() {
    $$('.tag-chip[data-tag]').forEach((chip) => {
      chip.addEventListener('click', () => {
        const tag = chip.dataset.tag;
        if (state.activeTag === tag) {
          state.activeTag = null;
          chip.classList.remove('active');
        } else {
          state.activeTag = tag;
          $$('.tag-chip').forEach((c) => c.classList.remove('active'));
          chip.classList.add('active');
        }
        $('search-input').value = '';
        state.searchQuery = '';
        renderEntries();
      });
    });
  }

  // ---------- Folders (visual only) ----------
  function initFolders() {
    $$('.folder-item').forEach((item) => {
      item.addEventListener('click', () => {
        $$('.folder-item').forEach((i) => i.classList.remove('active'));
        item.classList.add('active');
        const name = item.querySelector('span').textContent;
        $('list-title').textContent = name;
        if (name === '全部条目') {
          renderEntries();
        } else {
          toast(`文件夹筛选"${name}" — 仅原型演示`, 'info');
        }
      });
    });
  }

  // ---------- Editor overlay ----------
  function openEditor() {
    $('editor-overlay').classList.add('visible');
  }
  function initEditor() {
    $('editor-back').addEventListener('click', () => {
      $('editor-overlay').classList.remove('visible');
    });
    $('editor-save').addEventListener('click', () => {
      $('editor-overlay').classList.remove('visible');
      toast('条目已保存(原型)', 'success');
    });

    // Field reveal (hold)
    $$('.field-reveal').forEach((btn) => {
      const row = btn.closest('.field-row');
      const input = row.querySelector('input');
      const dot = row.querySelector('.exposed-dot');
      let revealed = false;
      const reveal = () => {
        if (input.type === 'password') {
          input.type = 'text';
          revealed = true;
          btn.classList.add('revealing');
          if (dot) dot.classList.add('visible');
        }
      };
      const mask = () => {
        if (revealed) {
          input.type = 'password';
          revealed = false;
          btn.classList.remove('revealing');
          if (dot) dot.classList.remove('visible');
        }
      };
      btn.addEventListener('mousedown', reveal);
      btn.addEventListener('mouseup', mask);
      btn.addEventListener('mouseleave', mask);
    });

    // Field copy
    $$('.field-copy').forEach((btn) => {
      btn.addEventListener('click', () => {
        const input = btn.closest('.field-row').querySelector('input');
        copyToClipboard(input.value, btn);
      });
    });

    // Tag removal
    $$('.tag-remove').forEach((x) => {
      x.addEventListener('click', (e) => {
        e.target.closest('.tag-chip-removable').remove();
      });
    });

    $('add-field').addEventListener('click', () => {
      toast('添加字段 — 仅原型演示', 'info');
    });
  }

  // ---------- Lock ----------
  function initLock() {
    $('lock-btn').addEventListener('click', lockVault);
    $('lock-now-btn').addEventListener('click', lockVault);
  }

  function lockVault() {
    if (state.lockTimer) clearInterval(state.lockTimer);
    if (state.totpTimer) clearInterval(state.totpTimer);
    if (state.clipboardTimer) clearTimeout(state.clipboardTimer);
    state.selectedEntryId = null;
    state.profile = 'prod';
    $('profile-banner').classList.remove('visible');
    $('profile-watermark').classList.remove('visible');
    showScreen('unlock');
    toast('保险库已锁定 · 内存已清空', 'info');
  }

  function startLockCountdown() {
    state.lockMinutesLeft = 15;
    const display = $('lock-countdown');
    if (state.lockTimer) clearInterval(state.lockTimer);
    state.lockTimer = setInterval(() => {
      state.lockMinutesLeft -= 1;
      if (state.lockMinutesLeft <= 0) {
        lockVault();
        return;
      }
      display.textContent = `已解锁 · 剩余 ${state.lockMinutesLeft} 分钟`;
    }, 60000); // 1 min per tick (proto uses real minutes)
    display.textContent = `已解锁 · 剩余 ${state.lockMinutesLeft} 分钟`;
  }

  // ---------- Sync simulation ----------
  function startSyncSimulation() {
    const indicator = $('sync-indicator');
    const dot = indicator.querySelector('.sync-dot');
    const text = $('sync-text');

    // Randomly toggle syncing state
    const cycle = () => {
      const r = Math.random();
      if (r < 0.2) {
        dot.className = 'sync-dot sync-dot-syncing';
        text.textContent = '同步中…(1/1 个文件)';
        setTimeout(() => {
          dot.className = 'sync-dot sync-dot-synced';
          text.textContent = `${randomAgo()}同步 · 来自 ${randomDevice()}`;
        }, 2200);
      }
      setTimeout(cycle, 8000 + Math.random() * 12000);
    };
    cycle();

    $('sync-btn').addEventListener('click', () => {
      dot.className = 'sync-dot sync-dot-syncing';
      text.textContent = '同步中…(手动触发)';
      setTimeout(() => {
        dot.className = 'sync-dot sync-dot-synced';
        text.textContent = `刚刚同步 · 来自 ${randomDevice()}`;
        toast('同步完成', 'success');
      }, 1500);
    });
  }

  function randomAgo() {
    const opts = ['刚刚', '3 秒前', '12 秒前', '45 秒前', '1 分钟前', '2 分钟前'];
    return opts[Math.floor(Math.random() * opts.length)];
  }
  function randomDevice() {
    const opts = ['laptop-abc', 'workstation-def', 'phone-ghi'];
    return opts[Math.floor(Math.random() * opts.length)];
  }

  // ---------- Toast ----------
  function toast(message, type = 'info') {
    const container = $('toast-container');
    const icons = {
      success: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none"><path d="M2 7 L6 11 L12 3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>',
      warning: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none"><path d="M7 2 L12 11 H2 Z" stroke="currentColor" stroke-width="1.2" fill="none" stroke-linejoin="round"/><path d="M7 6 V8 M7 9.5 V10" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
      danger: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none"><circle cx="7" cy="7" r="5" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M5 5 L9 9 M9 5 L5 9" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
      info: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none"><circle cx="7" cy="7" r="5" stroke="currentColor" stroke-width="1.2" fill="none"/><path d="M7 6 V10 M7 4 V4.5" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
    };
    const t = el('div', { class: `toast toast-${type}` },
      el('span', { class: 'toast-icon', html: icons[type] || icons.info }),
      el('span', {}, message)
    );
    container.appendChild(t);
    setTimeout(() => {
      t.classList.add('toast-out');
      setTimeout(() => t.remove(), 300);
    }, 2800);
  }

  // ---------- Misc ----------
  function escapeHtml(s) {
    return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function initTopbarButtons() {
    $('settings-btn').addEventListener('click', () => toast('设置 — 仅原型演示', 'info'));
    $('sort-btn').addEventListener('click', () => toast('排序选项 — 仅原型演示', 'info'));
    $('new-entry-btn').addEventListener('click', openEditor);
    $('clear-filters').addEventListener('click', () => {
      state.searchQuery = '';
      state.activeTag = null;
      $('search-input').value = '';
      $$('.tag-chip').forEach((c) => c.classList.remove('active'));
      renderEntries();
    });
  }

  // ---------- Boot ----------
  document.addEventListener('DOMContentLoaded', () => {
    initUnlock();
    initProfileSwitcher();
    initSearch();
    initTags();
    initFolders();
    initEditor();
    initLock();
    initTopbarButtons();
    renderEntries();
  });
})();
