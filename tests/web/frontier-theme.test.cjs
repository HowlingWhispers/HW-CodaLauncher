'use strict';
// Frontier theme (0.8.0): presentation follows the saved play mode, never a fake progress value.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync('web/index.html', 'utf8');
const css = fs.readFileSync('web/styles.css', 'utf8');
const code = fs.readFileSync('web/app.js', 'utf8');

for (const id of ['play-progress', 'verification-detail', 'build-details'])
  assert.ok(html.includes('id="' + id + '"'), 'Missing Frontier control ' + id);
assert.ok(html.includes('class="frontier-scene"'), 'Missing Frontier camp scene');
assert.ok(fs.existsSync('web/assets/fonts/Jersey15-Regular.ttf'), 'Pixel font must be bundled for offline use');
assert.ok(fs.existsSync('web/assets/fonts/Jersey15-OFL.txt'), 'Pixel font licence must ship with the font');
assert.doesNotMatch(css, /fonts\.googleapis|fonts\.gstatic/, 'Theme must not fetch fonts from the network');
assert.match(css, /prefers-reduced-motion/, 'Frontier animations must respect reduced motion');
// 0.8.2: no Classic flash on start. The host passes the saved theme, and the page applies it before painting.
const host = fs.readFileSync('MainWindow.xaml.cs', 'utf8');
assert.match(host, /index\.html" \+ \(frontierTheme \? "\?theme=frontier" : ""\)/, 'The launcher opens the page with the saved theme');
assert.match(host, /DefaultBackgroundColor/, 'The WebView background matches the theme before the page loads');
const early = html.indexOf('theme=frontier'), bodyAt = html.indexOf('<body>'), firstMarkup = html.indexOf('<div class="shell">');
assert.ok(early > bodyAt && early < firstMarkup, 'The theme is applied before any launcher markup is painted');
assert.doesNotMatch(code, /aria-valuenow|progress\.value\s*=/, 'Activity bar must not report a simulated percentage');

const ids = new Map(), handlers = {}, bodyClasses = new Set();
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
const browser = {
  document: {
    body: { classList: { toggle(name, on) { on ? bodyClasses.add(name) : bodyClasses.delete(name); } } },
    getElementById: el,
    querySelectorAll() { return []; }
  },
  window: { chrome: { webview: {
    postMessage() {},
    addEventListener(event, handler) { handlers[event] = handler; }
  } } }
};
vm.runInNewContext(code, browser);

function state(channel, playMode, managedCurrent) {
  return { type: 'state', data: {
    launcherVersion: '0.8.0', activeChannel: channel, gameRunning: false,
    accountBusy: false, localSingleplayer: true, managedCurrent, managedInstalled: true,
    loaderCurrent: managedCurrent, loaderReady: true, basePackReady: true,
    installedLoaderVersion: '0.0.43', latestLoaderVersion: '0.0.43',
    modCount: 0, mods: [], optionalMods: [], packs: [], resourcePacks: [], worlds: [],
    settings: { updateChannel: channel, localTestMode: true, playMode, feedUrl: '' },
    account: { configured: false, signedIn: false, offlineAvailable: false, storage: '' },
    profile: { cmlAccount: '', discord: '' }, feed: { news: [] }, logs: []
  } };
}

handlers.message({ data: state('nightly', 'frontier', true) });
assert.ok(bodyClasses.has('theme-frontier'), 'Frontier Nightly must use the Frontier theme');
assert.match(el('verification-detail').textContent, /Checksums matched/);
assert.match(el('verification-detail').textContent, /signature not yet available/);
assert.match(el('build-details').textContent, /Frontier Nightly/);

handlers.message({ data: state('nightly', 'classic', false) });
assert.ok(!bodyClasses.has('theme-frontier'), 'Classic Nightly keeps the Classic theme');
assert.equal(el('verification-detail').textContent, 'Distribution trust', 'No checksum claim when components are not current');

handlers.message({ data: state('stable', 'classic', true) });
assert.ok(!bodyClasses.has('theme-frontier'), 'Stable keeps the Classic theme');

handlers.message({ data: { type: 'installStatus', busy: true, ok: true, message: 'Verifying…' } });
assert.equal(el('play-progress').hidden, false, 'Activity bar shows while Play is working');
handlers.message({ data: { type: 'installStatus', busy: false, ok: true, message: 'Ready.' } });
assert.equal(el('play-progress').hidden, true, 'Activity bar hides when work finishes');

// Nightly's hero follows the Nightly runtime, not Stable's managed install (0.8.1 fix).
const nightlyCurrent = state('nightly', 'frontier', true);
nightlyCurrent.data.managedCurrent = false; nightlyCurrent.data.managedInstalled = false;
handlers.message({ data: nightlyCurrent });
assert.equal(el('hero-title').textContent, 'Ready to play', 'A current Nightly runtime is ready even if Stable is not installed');
console.log('PASS: Frontier theme follows play mode, bundles its font, and makes no simulated progress or trust claims');
