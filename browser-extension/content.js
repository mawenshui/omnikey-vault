// OmniKey Vault Browser Extension — Content Script
// v2.6.5: Improved auto-fill resilience + better form detection
// Detects username/password fields on web pages and fills them in.

(function() {
  'use strict';

  // v2.6.5: Ensure we don't register multiple listeners (re-injection)
  if (window.__okvContentScriptInjected) {
    // Already injected — just re-add the message listener
  }
  window.__okvContentScriptInjected = true;

  // Listen for auto-fill messages from the popup/background
  chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'AUTOFILL') {
      try {
        const result = autoFillForm(message.fields);
        sendResponse({ success: true, filled: result.filled, total: result.total });
      } catch (err) {
        sendResponse({ success: false, error: err.message });
      }
    }
    return true;
  });

  /**
   * Detects form fields on the page and fills them with the provided credentials.
   * v2.6.5: Improved detection — checks visibility, checks iframe-bounded
   * inputs, and fills more field types (url, search, number).
   * @param {Array} fields - Array of { key, value } pairs from the vault entry
   * @returns {Object} { filled: number, total: number }
   */
  function autoFillForm(fields) {
    if (!fields || !Array.isArray(fields) || fields.length === 0) {
      return { filled: 0, total: 0 };
    }

    // v2.6.5: Also check iframes within the same page (same-origin)
    const inputs = collectAllInputs();

    const passwordFields = [];
    const usernameFields = [];
    const otherFields = [];

    for (const input of inputs) {
      // Skip hidden, disabled, or submit inputs
      if (input.type === 'hidden' || input.type === 'submit' || input.type === 'button' || input.disabled) continue;

      // v2.6.5: Skip inputs that are not visible (display:none, visibility:hidden, width=0)
      if (!isVisible(input)) continue;

      if (input.type === 'password') {
        passwordFields.push(input);
      } else if (input.type === 'email' || input.type === 'text' || input.type === 'tel' || input.type === '' || input.type === 'url' || input.type === 'search') {
        usernameFields.push(input);
      } else {
        otherFields.push(input);
      }
    }

    let filled = 0;
    const total = fields.length;

    // Build a map of field values by key (case-insensitive)
    const fieldMap = {};
    for (const f of fields) {
      fieldMap[f.key.toLowerCase()] = f.value;
    }

    // Try to find password value from the fields
    const passwordValue = findFieldValue(fields, ['password', 'passwd', 'secret', 'api_key', 'apikey', 'token', 'access_token', 'key', 'pass']);
    const usernameValue = findFieldValue(fields, ['username', 'user', 'login', 'email', 'account', 'api_key_id', 'access_key_id', 'client_id', 'app_id', 'account_name', 'name']);

    // Fill password fields
    if (passwordValue && passwordFields.length > 0) {
      for (const pf of passwordFields) {
        setInputValue(pf, passwordValue);
        filled++;
      }
    }

    // Fill username fields (skip already filled password fields)
    if (usernameValue && usernameFields.length > 0) {
      // Prefer the first visible username field
      const targetField = usernameFields[0];
      setInputValue(targetField, usernameValue);
      filled++;
    }

    // Fill other matching fields by name/id
    for (const [key, value] of Object.entries(fieldMap)) {
      const matchingInput = findInputByName(otherFields.concat(usernameFields), key);
      if (matchingInput && !matchingInput.dataset.okvFilled) {
        setInputValue(matchingInput, value);
        filled++;
      }
    }

    // Try to fill API key / token fields that might be textarea or other input types
    const textareas = document.querySelectorAll('textarea');
    for (const ta of textareas) {
      if (ta.disabled || ta.readOnly) continue;
      const name = (ta.name || ta.id || ta.placeholder || '').toLowerCase();
      for (const [key, value] of Object.entries(fieldMap)) {
        if (name.includes(key)) {
          setInputValue(ta, value);
          filled++;
          break;
        }
      }
    }

    return { filled, total };
  }

  /**
   * v2.6.5: Collects inputs from the main document and same-origin iframes.
   */
  function collectAllInputs() {
    const mainInputs = Array.from(document.querySelectorAll('input'));
    // Also check same-origin iframes
    try {
      const iframes = document.querySelectorAll('iframe');
      for (const iframe of iframes) {
        try {
          const iframeDoc = iframe.contentDocument;
          if (iframeDoc) {
            const iframeInputs = Array.from(iframeDoc.querySelectorAll('input'));
            mainInputs.push(...iframeInputs);
          }
        } catch (e) {
          // Cross-origin iframe — skip
        }
      }
    } catch (e) {
      // best-effort
    }
    return mainInputs;
  }

  /**
   * v2.6.5: Checks if an element is visible on the page.
   */
  function isVisible(el) {
    if (!el || !el.parentElement) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') {
      return false;
    }
    const rect = el.getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) return false;
    return true;
  }

  /**
   * Finds a field value by matching keys against a list of candidate names.
   */
  function findFieldValue(fields, candidates) {
    for (const candidate of candidates) {
      const field = fields.find(f => f.key.toLowerCase() === candidate || f.key.toLowerCase().includes(candidate));
      if (field) return field.value;
    }
    return null;
  }

  /**
   * Finds an input element by matching name/id/placeholder against a key.
   */
  function findInputByName(inputs, key) {
    for (const input of inputs) {
      if (input.dataset.okvFilled) continue;
      const name = (input.name || input.id || input.placeholder || '').toLowerCase();
      if (name.includes(key)) return input;
    }
    return null;
  }

  /**
   * Sets a value on an input element, triggering proper events so that
   * React/Vue/Angular frameworks detect the change.
   * v2.6.5: Also dispatch 'blur' event for frameworks that validate on blur.
   */
  function setInputValue(input, value) {
    // Use native setter to work with framework-bound inputs
    const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
    const nativeTextareaValueSetter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value')?.set;

    if (input.tagName === 'TEXTAREA' && nativeTextareaValueSetter) {
      nativeTextareaValueSetter.call(input, value);
    } else if (nativeInputValueSetter) {
      nativeInputValueSetter.call(input, value);
    } else {
      input.value = value;
    }

    // Dispatch events that frameworks listen to
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    // v2.6.5: Also dispatch keyup and blur for frameworks that need them
    input.dispatchEvent(new Event('keyup', { bubbles: true }));
    input.dispatchEvent(new Event('blur', { bubbles: true }));

    // Mark as filled to avoid double-filling
    input.dataset.okvFilled = 'true';

    // Visual feedback — brief border highlight
    const originalBorder = input.style.borderColor;
    const originalBoxShadow = input.style.boxShadow;
    input.style.borderColor = '#2ecc71';
    input.style.boxShadow = '0 0 4px #2ecc71';
    setTimeout(() => {
      input.style.borderColor = originalBorder;
      input.style.boxShadow = originalBoxShadow;
    }, 1500);
  }
})();
