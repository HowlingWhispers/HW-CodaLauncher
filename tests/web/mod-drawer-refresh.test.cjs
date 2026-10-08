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
const sandbox = {
  document: {
    getElementById: el,
    querySelectorAll(selector) {
      if (selector === '.nav') return [homeNav, modsNav];
      return [];
    }
  },
  window: { chrome: { webview } }
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../web/app.js'), 'utf8'), sandbox);
assert.equal(calls[0].action, 'ready', 'app should request initial state');

const source = {
  type: 'state',
  data: {
    launcherVersion: '0.7.7',
    activeChannel: 'nightly',
    nightlyInstalled: true,
    gameRunning: false,
    accountBusy: false,
    modCount: 1,
    mods: [{ fileName: 'buildcraft-cml-0.1.0-dev.jar', id: 'buildcraft_cml',
      name: 'BuildCraft CML', version: '0.1.0-dev', valid: true }],
    account: { configured: false, signedIn: false, offlineAvailable: true, storage: '' },
    settings: { updateChannel: 'nightly', localTestMode: true },
    profile: { cmlAccount: '', discord: '' },
    feed: { online: false, news: [], packs: [] },
    packs: [], resourcePacks: [], logs: []
  }
};
listeners.message({ data: source });
assert.equal(el('mods-count').textContent, '1 jar', 'initial drawer matches cached state');
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
        name: 'BuildCraft CML', version: '0.1.0-dev', valid: true },
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
assert.doesNotMatch(el('mods-list').innerHTML, /Ready/, 'No unverified gameplay success label');
console.log('PASS: Mod Drawer navigation refreshes installed JARs and corrects displayed labels.');
