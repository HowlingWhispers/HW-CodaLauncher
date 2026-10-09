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
  dataset: { view: 'addons' },
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
  { dataset: { action: 'installMod', mod: 'buildcraft_cml' }, disabled: false, onclick: null },
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
      { id: 'coda_wolf', name: 'Coda Wolf Companion', installed: false, managed: false, recommended: false, nightlyOnly: false, required: true, version: '' },
      { id: 'hw_essentials', name: 'HW Essentials', installed: false, managed: false, recommended: false, nightlyOnly: false, required: true, version: '' }
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
assert.match(el('optional-mods').innerHTML, /Coda Wolf Companion/, 'Required Coda displayed');
assert.match(el('optional-mods').innerHTML, /HW Essentials/, 'Required Essentials displayed');
assert.equal((el('optional-mods').innerHTML.match(/REQUIRED · BUNDLED WITH H\.O\.W\.L\./g)||[]).length,2,
  'Both foundational mods marked required');
assert.doesNotMatch(el('optional-mods').innerHTML, /data-mod="coda_wolf"/,
  'No Coda manual install or uninstall button');
assert.doesNotMatch(el('optional-mods').innerHTML, /data-mod="hw_essentials"/,
  'No Essentials manual install or uninstall button');
assert.match(el('optional-mods').innerHTML, /OPTIONAL · RECOMMENDED/,
  'BuildCraft remains an optional addon');
assert.match(el('optional-mods').innerHTML, /UNINSTALL/,
  'Optional installed mod still supports removal');

modButtons[0].onclick();
assert.equal(calls.at(-1).action, 'installMod', 'Optional BuildCraft installation still explicit');
assert.equal(calls.at(-1).id, 'buildcraft_cml', 'Only selected optional mod is installed');
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
assert.equal(calls.at(-1).action, 'refreshMods', 'opening Add-ons requests immediate mod rescan');
el('refresh-mods').onclick();
assert.equal(calls.at(-1).action, 'refreshMods', 'button requests rescan without feed/network wait');

listeners.message({
  data: {
    type: 'modsState',
    minecraftRoot: 'C:/HOWL/nightly/minecraft',
    modCount: 2,
    mods: [
      { fileName: 'buildcraft-cml-0.1.0-dev.jar', id: 'buildcraft_cml',
        name: 'BuildCraft CML', version: '0.1.2-dev', valid: true,
        description: 'Original pipes & engines', changeSummary: 'Fixed placement <crash>',
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
assert.match(el('mods-list').innerHTML, /Manifest v0\.1\.2-dev/, 'JAR metadata version remains distinguishable from installed release');
assert.match(el('mods-list').innerHTML, /SHA-256 verified locally/, 'Release identity is locally checksum verified');
assert.doesNotMatch(el('mods-list').innerHTML, /Latest release/, 'Local verification never claims remote freshness');
assert.doesNotMatch(el('mods-list').innerHTML, /Ready/, 'No unverified gameplay success label');
assert.match(el('mods-list').innerHTML, /Original pipes &amp; engines/, 'Description displays as escaped text');
assert.match(el('mods-list').innerHTML, /What changed in v0\.1\.2-dev:/, 'Notes belong to the installed manifest version');
assert.match(el('mods-list').innerHTML, /Fixed placement &lt;crash&gt;/, 'Release summary displays as escaped text');
assert.doesNotMatch(el('mods-list').innerHTML, /<crash>/, 'Mod metadata cannot inject markup');
// Stable channel also shows Coda and Essentials as required even when
// neither is installed yet; no Nightly-only or manual uninstall control.
source.data.activeChannel = 'stable';
source.data.settings.updateChannel = 'stable';
// A preceding Mods refresh intentionally supplied no catalog and cleared the
// cached list, so restore the required catalog as Stable's fresh state.
source.data.optionalMods = [
  { id: 'buildcraft_cml', name: 'BuildCraft CML', installed: false,
    managed: false, recommended: true, nightlyOnly: true, version: '' },
  { id: 'coda_wolf', name: 'Coda Wolf Companion', installed: false,
    managed: false, recommended: false, nightlyOnly: false, required: true, version: '' },
  { id: 'hw_essentials', name: 'HW Essentials', installed: false,
    managed: false, recommended: false, nightlyOnly: false, required: true, version: '' }
];
listeners.message({data:source});
assert.equal((el('optional-mods').innerHTML.match(/REQUIRED · BUNDLED WITH H\.O\.W\.L\./g)||[]).length,2,
  'Stable treats both bundled mods as required');
assert.match(el('optional-mods').innerHTML, /Will install with H\.O\.W\.L\. at launch/,
  'Stable informs player of automatic dependency provisioning');
assert.doesNotMatch(el('optional-mods').innerHTML, /data-mod="coda_wolf"|data-mod="hw_essentials"/,
  'Stable cannot accidentally uninstall required mods');
console.log('PASS: Mods drawer marks Coda and Essentials required on Stable/Nightly; optional BuildCraft stays removable.');
