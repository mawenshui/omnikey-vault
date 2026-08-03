// OmniKey Vault Browser Extension — Background Service Worker
// v2.4.0: Icon status indicator + auto-fill message relay
// Periodically checks vault status and updates the extension icon badge.

const DEFAULT_PORT = 14725;
const STATUS_CHECK_INTERVAL_MS = 5000; // Check every 5 seconds

let apiBase = `http://127.0.0.1:${DEFAULT_PORT}`;
let authToken = '';
let lastLockedState = null;

// Load settings from storage on startup
chrome.storage.local.get(['okv_auth_token', 'okv_api_port'], (result) => {
  authToken = result.okv_auth_token || '';
  const port = result.okv_api_port || DEFAULT_PORT;
  apiBase = `http://127.0.0.1:${port}`;
  checkVaultStatus();
});

// Listen for storage changes (e.g., when user updates auth token in popup)
chrome.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local') return;
  if (changes.okv_auth_token) authToken = changes.okv_auth_token.newValue || '';
  if (changes.okv_api_port) {
    const port = changes.okv_api_port.newValue || DEFAULT_PORT;
    apiBase = `http://127.0.0.1:${port}`;
  }
});

async function checkVaultStatus() {
  try {
    const url = new URL(apiBase + '/api/status');
    const response = await fetch(url, {
      headers: { 'Authorization': `Bearer ${authToken}` },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.json();

    const isLocked = data.locked;
    if (isLocked !== lastLockedState) {
      lastLockedState = isLocked;
      updateIcon(isLocked);
    }
  } catch (err) {
    // Cannot connect — show disconnected state
    if (lastLockedState !== 'disconnected') {
      lastLockedState = 'disconnected';
      updateIcon('disconnected');
    }
  }
}

function updateIcon(state) {
  // Set badge text and color based on vault state
  if (state === true) {
    // Locked — red badge "L"
    chrome.action.setBadgeText({ text: 'L' });
    chrome.action.setBadgeBackgroundColor({ color: '#e74c3c' });
    chrome.action.setTitle({ title: 'OmniKey Vault — 保险箱已锁定' });
  } else if (state === false) {
    // Unlocked — green badge "U"
    chrome.action.setBadgeText({ text: 'U' });
    chrome.action.setBadgeBackgroundColor({ color: '#2ecc71' });
    chrome.action.setTitle({ title: 'OmniKey Vault — 已连接' });
  } else if (state === 'disconnected') {
    // Disconnected — gray badge "!"
    chrome.action.setBadgeText({ text: '!' });
    chrome.action.setBadgeBackgroundColor({ color: '#95a5a6' });
    chrome.action.setTitle({ title: 'OmniKey Vault — 无法连接到桌面应用' });
  }
}

// Periodic status check
setInterval(checkVaultStatus, STATUS_CHECK_INTERVAL_MS);

// Handle messages from popup/content scripts
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === 'AUTOFILL_ENTRY') {
    // Relay auto-fill request to the active tab's content script
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
      if (tabs[0]) {
        chrome.tabs.sendMessage(tabs[0].id, {
          type: 'AUTOFILL',
          entry: message.entry,
          fields: message.fields,
        }, (response) => {
          sendResponse(response);
        });
      } else {
        sendResponse({ success: false, error: 'No active tab' });
      }
    });
    return true; // Keep channel open for async response
  }

  if (message.type === 'GET_AUTOFILL_DATA') {
    // Fetch actual field values from the API for auto-fill
    fetchAutofillData(message.entryId, message.profile).then(data => {
      sendResponse(data);
    }).catch(err => {
      sendResponse({ success: false, error: err.message });
    });
    return true;
  }
});

async function fetchAutofillData(entryId, profile) {
  const params = new URLSearchParams({ entryId, profile: profile || 'prod' });
  const url = new URL(apiBase + '/api/autofill');
  url.search = params.toString();
  const response = await fetch(url, {
    headers: { 'Authorization': `Bearer ${authToken}` },
  });
  if (!response.ok) throw new Error(`API error: ${response.status}`);
  return response.json();
}

// Track recent entries — called from popup when an entry is copied
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === 'TRACK_RECENT') {
    trackRecentEntry(message.entry);
    sendResponse({ success: true });
  }
});

function trackRecentEntry(entry) {
  chrome.storage.local.get(['okv_recent_entries'], (result) => {
    let recent = result.okv_recent_entries || [];
    // Remove if already exists (de-dup)
    recent = recent.filter(e => e.id !== entry.id);
    // Add to front
    recent.unshift(entry);
    // Keep max 10
    recent = recent.slice(0, 10);
    chrome.storage.local.set({ okv_recent_entries: recent });
  });
}
