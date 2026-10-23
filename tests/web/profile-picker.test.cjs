'use strict';
// 0.8.1: the sidebar profile cards set channel and play mode together through the normal Settings save.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync('web/index.html', 'utf8');
const code = fs.readFileSync('web/app.js', 'utf8');

for (const profile of ['classic-stable', 'classic-nightly', 'frontier'])
  assert.ok(html.includes('data-profile="' + profile + '"'), 'Missing profile card ' + profile);
for (const id of ['home-mods', 'hotbar-count', 'launch-message', 'play-progress', 'play'])
  assert.ok(html.includes('id="' + id + '"'), 'Missing Home control ' + id);

const ids = new Map(), calls = [], handlers = {};
function el(id) {
  if (!ids.has(id)) ids.set(id, {
    textContent: '', innerHTML: '', className: '', value: '', hidden: false,
    checked: false, disabled: false, style: {}, dataset: {},
    parentElement: { scrollTop: 0 },
    classList: { add() {}, remove() {}, toggle() {} },
    click() { this.onclick?.(); }
  });
  return ids.get(id);
}
const cards = ['classic-stable', 'classic-nightly', 'frontier'].map(profile => ({
  dataset: { profile }, disabled: false, checked: null, active: false,
  classList: { toggle(name, on) { if (name === 'active') this.owner.active = on; } },
  setAttribute(name, value) { if (name === 'aria-checked') this.checked = value; },
  addEventListener(_name, listener) { this.click = listener; }
}));
cards.forEach(c => { c.classList.owner = c; });
const browser = {
  document: {
    body: { classList: { toggle() {} } },
    getElementById: el,
    querySelectorAll(selector) { return selector === '.profile-card' ? cards : []; }
  },
  window: { chrome: { webview: {
    postMessage(value) { calls.push(value); },
    addEventListener(event, handler) { handlers[event] = handler; }
  } } }
};
vm.runInNewContext(code, browser);

function state(channel, playMode, extra = {}) {
  return { type: 'state', data: Object.assign({
    launcherVersion: '0.8.1', activeChannel: channel, gameRunning: false,
    accountBusy: false, localSingleplayer: true, managedCurrent: true, managedInstalled: true,
    loaderCurrent: true, loaderReady: true, basePackReady: true,
    installedLoaderVersion: '0.0.46', latestLoaderVersion: '0.0.46',
    modCount: 2, optionalMods: [], packs: [], resourcePacks: [], worlds: [],
    mods: [
      { id: 'howl_frontier', name: 'H.O.W.L. Frontier', version: '0.4.0', fileName: 'howl-frontier-0.4.0.jar', valid: true },
      { id: 'broken', name: '<Broken>', version: '', fileName: 'x.jar', valid: false }
    ],
    settings: { updateChannel: channel, localTestMode: channel === 'nightly', playMode, feedUrl: '' },
    account: { configured: false, signedIn: false, offlineAvailable: false, storage: '' },
    profile: { cmlAccount: '', discord: '' }, feed: { news: [] }, logs: []
  }, extra) };
}

handlers.message({ data: state('stable', 'classic') });
assert.equal(cards[0].active, true, 'Classic Stable is shown as the active profile');
assert.equal(cards[0].checked, 'true');
assert.equal(cards[2].active, false);

// The hotbar shows real scan results, escaped, with versions.
assert.match(el('home-mods').innerHTML, /H\.O\.W\.L\. Frontier/);
assert.match(el('home-mods').innerHTML, /v0\.4\.0/);
assert.match(el('home-mods').innerHTML, /&lt;Broken&gt;/, 'Mod names are escaped');
assert.match(el('home-mods').innerHTML, /Unreadable/);
assert.equal(el('hotbar-count').textContent, '1 mod', 'Only readable mods are counted');

calls.length = 0;
cards[2].click();
const save = calls.find(c => c.action === 'saveSettings');
assert.ok(save, 'Choosing Frontier saves the settings');
assert.equal(save.settings.updateChannel, 'nightly');
assert.equal(save.settings.playMode, 'frontier');
assert.equal(save.settings.localTestMode, true, 'Nightly profiles use local test mode');

// While a save is pending nothing else is sent.
calls.length = 0;
cards[1].click();
assert.equal(calls.length, 0, 'A second choice waits for the first save');

handlers.message({ data: { type: 'settingsSaveResult', ok: true } });
handlers.message({ data: state('nightly', 'frontier') });
assert.equal(cards[2].active, true, 'Frontier becomes the active profile');

calls.length = 0;
cards[0].click();
const back = calls.find(c => c.action === 'saveSettings');
assert.equal(back.settings.updateChannel, 'stable');
assert.equal(back.settings.playMode, 'classic', 'Going back to Stable leaves Frontier');

handlers.message({ data: { type: 'settingsSaveResult', ok: true } });
handlers.message({ data: state('nightly', 'frontier', { gameRunning: true }) });
assert.equal(cards[0].disabled, true, 'Profiles cannot change while Minecraft runs');
assert.equal(cards[2].disabled, false, 'The active profile stays visible as chosen');

console.log('PASS: profile cards switch channel and play mode through Settings; hotbar shows real, escaped mod data');
