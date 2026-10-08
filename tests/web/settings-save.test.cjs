const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Lightweight WebView2 simulation: test what players actually see in Settings,
// without launching Minecraft or changing any filesystem settings.
const elements = new Map();
const calls = [];
const listeners = {};
const el = id => {
  if (!elements.has(id)) elements.set(id, {
    textContent: '', className: '', disabled: false, checked: false,
    value: '', hidden: false, style: {}, onclick: null, onchange: null
  });
  return elements.get(id);
};
const sandbox = {
  document: { getElementById: el, querySelectorAll: () => [] },
  window: { chrome: { webview: {
    postMessage: message => calls.push(message),
    addEventListener: (name, cb) => { listeners[name] = cb; }
  } } }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../web/app.js'), 'utf8'), sandbox);
assert.equal(calls[0].action, 'ready');
el('loader-path').value = '';
el('feed-url').value = 'https://thehowlingwhispers.com/launcher';
el('local-test-mode').checked = true;
el('update-channel').value = 'stable';
el('close-after').checked = false;
el('save').onclick();
assert.equal(calls.at(-1).action, 'saveSettings');
assert.equal(calls.at(-1).settings.localTestMode, true);
assert.equal(calls.at(-1).settings.updateChannel, 'stable', 'Stable remains default');
assert.equal(el('save').disabled, true);
assert.match(el('settings-save-result').textContent, /Saving/);
const count = calls.length;
el('save').onclick();
assert.equal(calls.length, count, 'duplicate saves must be suppressed while pending');
listeners.message({ data: { type: 'settingsSaveResult', ok: true } });
assert.equal(el('save').disabled, false);
assert.equal(el('save').textContent, 'Save settings');
assert.match(el('settings-save-result').textContent, /Settings saved/);
assert.equal(el('settings-save-result').className, 'settings-save-result ok');
el('local-test-mode').checked = false;
el('save').onclick();
assert.equal(calls.at(-1).settings.localTestMode, false);
listeners.message({ data: { type: 'settingsSaveResult', ok: false, message: 'Write access denied' } });
assert.equal(el('save').disabled, false);
assert.match(el('settings-save-result').textContent, /Write access denied/);
assert.equal(el('settings-save-result').className, 'settings-save-result error');
el('update-channel').value = 'nightly';
el('local-test-mode').checked = false;
el('update-channel').onchange();
assert.equal(el('local-test-mode').checked, true, 'Nightly must select local-only mode');
assert.equal(el('nightly-warning').hidden, false, 'Nightly warns about isolated saves');
el('save').onclick();
assert.equal(calls.at(-1).settings.updateChannel, 'nightly', 'Nightly opt-in reaches native launcher');
assert.equal(calls.at(-1).settings.localTestMode, true, 'Nightly launch remains local');
listeners.message({ data: { type: 'settingsSaveResult', ok: true } });
console.log('PASS: settings save shows pending, success, error, retries and opt-in local switch');
