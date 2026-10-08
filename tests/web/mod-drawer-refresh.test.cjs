const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Exercise Mod Drawer navigation, rescan request, and refreshed rendering.
const elements = new Map();
const calls = [];
const listeners = {};
const el = id => {
  if (!elements.has(id)) elements.set(id, {
    textContent: '', innerHTML: '', className: '', disabled: false,
    checked: false, value: '', hidden: false, style: {},
    onclick: null, onchange: null, dataset: {}, parentElement: { scrollTop: 0 },
    classList: { add() {}, remove() {} }
  });
  return elements.get(id);
};
const modsNav = {
  dataset: { view: 'mods' },
  classList: { add() {}, remove() {} },
  addEventListener(event, listener) { this[event] = listener; }
};
const homeNav = {
  dataset: { view: 'home' },
  classList: { add() {}, remove() {} },
  addEventListener(event, listener) { this[event] = listener; }
};
const webview = {
  postMessage: message => calls.push(message),
  addEventListener(name, callback) { listeners[name] = callback; }
};
const modButtons = [
  { dataset: { action: 'installMod', mod: 'coda_wolf' }, disabled: false, onclick: null },
  { dataset: { action: 'uninstallMod', mod: 'buildcraft_cml' }, disabled: false, onclick: null }
];
let confirmResult = false;
const sandbox = {
  document: {
    getElementById: el,
    querySelectorAll(selector) {
      if (selector === '.nav') return [homeNav, modsNav];
      if (selector === '.mod-action') return modButtons;
      return [];
    }
  },
  window: { chrome: { webview }, confirm: () => confirmResult }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../web/app.js'), 'utf8'), sandbox);
assert.equal(calls[0].action, 'ready', 'app should request initial state');

const source = {
  type: 'state',
  data: {
    launcherVersion: '0.7.7',
    activeChannel: 'nightly',
    nightlyInstalled: true,
    nightlyQuietInstalled: true,
    gameRunning: false,
    accountBusy: false,
    modCount: 1,
    mods: [{ fileName: 'buildcraft-cml-0.1.0-dev.jar', id: 'buildcraft_cml',
      name: 'BuildCraft CML', version: '0.1.0-dev', valid: true }],
    optionalMods: [
      { id: 'buildcraft_cml', name: 'BuildCraft CML', installed: true, managed: true, recommended: true, nightlyOnly: true, version: '0.1.0-dev' },
      { id: 'coda_wolf', name: 'Coda Wolf Companion', installed: false, managed: false, recommended: true, nightlyOnly: true, version: '' },
      { id: 'hw_essentials', name: 'HW Essentials', installed: false, managed: false, recommended: true, nightlyOnly: false, version: '' }
    ],
    account: { configured: false, signedIn: false, offlineAvailable: true, storage: '' },
    settings: { updateChannel: 'nightly', localTestMode: true },
    profile: { cmlAccount: '', discord: '' },
    feed: { online: false, news: [], packs: [] },
    packs: [], resourcePacks: [], logs: []
  }
};
listeners.message({ data: source });
assert.equal(el('mods-count').textContent, '1 jar', 'initial drawer matches cached state');
assert.match(el('optional-mods').innerHTML, /Coda Wolf Companion/, 'Optional Coda Wolf listed when uninstalled');
assert.match(el('optional-mods').innerHTML, /RECOMMENDED/, 'Recommendation is distinct from requirement');
assert.match(el('optional-mods').innerHTML, /INSTALL/, 'Optional installation requires explicit button');
assert.match(el('optional-mods').innerHTML, /UNINSTALL/, 'Optional installed mod supports removal');
assert.doesNotMatch(el('optional-mods').innerHTML, /REQUIRED/, 'No add-on labeled required');

modButtons[0].onclick();
assert.equal(calls.at(-1).action, 'installMod', 'Coda Wolf install requires a deliberate Mods button');
assert.equal(calls.at(-1).id, 'coda_wolf', 'Install requests exactly the selected optional mod');
listeners.message({ data: { type: 'modActionStatus', busy: false, ok: true, message: 'Installed' } });
modButtons[1].onclick();
assert.notEqual(calls.at(-1).action, 'uninstallMod', 'Uninstall requires confirmation before request');
confirmResult = true;
modButtons[1].onclick();
assert.equal(calls.at(-1).action, 'uninstallMod', 'Confirmed uninstall posts explicit removal');
assert.equal(calls.at(-1).id, 'buildcraft_cml', 'Uninstall targets only selected mod');
listeners.message({ data: { type: 'modActionStatus', busy: false, ok: true, message: 'Uninstalled' } });
assert.equal(el('nightly-quiet-card').hidden, false, 'Nightly Quiet Underground appears under Packs');
assert.equal(el('nightly-quiet-status').textContent, 'Optional · Installed',
  'Quiet Underground is explicitly optional, not a required mod');
modsNav.click();
assert.equal(calls.at(-1).action, 'refreshMods', 'opening Mods requests immediate rescan');
el('refresh-mods').onclick();
assert.equal(calls.at(-1).action, 'refreshMods', 'button requests rescan without feed/network wait');

listeners.message({
  data: {
    type: 'modsState',
    minecraftRoot: 'C:/HOWL/nightly/minecraft',
    modCount: 2,
    mods: [
      { fileName: 'buildcraft-cml-0.1.0-dev.jar', id: 'buildcraft_cml',
        name: 'BuildCraft CML', version: '0.1.0-dev', valid: true,
        releaseStatus: 'Verified', releaseTag: 'nightly-buildcraft-20261008-abc123' },
      { fileName: 'hw-essentials.jar', id: 'hw_essentials',
        name: 'HW Essentials', version: '0.2.0', valid: true }
    ]
  }
});
assert.equal(el('mods-count').textContent, '2 jars', 'Mod Drawer shows both installed mod JARs');
assert.equal(el('mod-chip').textContent, '2 mods', 'Home counter updates with fresh scan');
assert.match(el('mods-list').innerHTML, /hw-essentials\.jar/, 'HW Essentials appears in drawer');
assert.match(el('mods-list').innerHTML, /buildcraft-cml/, 'BuildCraft remains listed');
assert.match(el('mods-list').innerHTML, /Recognized/, 'JAR recognition is not mistaken for in-game load');
assert.match(el('mods-list').innerHTML, /nightly-buildcraft-20261008-abc123/, 'Mods shelf shows verified installed release tag');
assert.match(el('mods-list').innerHTML, /Manifest v0\.1\.0-dev/, 'JAR metadata version remains distinguishable from installed release');
assert.match(el('mods-list').innerHTML, /SHA-256 verified locally/, 'Release identity is locally checksum verified');
assert.doesNotMatch(el('mods-list').innerHTML, /Latest release/, 'Local verification never claims remote freshness');
assert.doesNotMatch(el('mods-list').innerHTML, /Ready/, 'No unverified gameplay success label');
console.log('PASS: Mod Drawer navigation refreshes installed JARs and corrects displayed labels.');
