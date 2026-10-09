'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ui = fs.readFileSync('web/app.js', 'utf8');
const state = fs.readFileSync('MainWindow.xaml.cs', 'utf8');
const core = fs.readFileSync('LauncherCore.cs', 'utf8');
const nightly = fs.readFileSync('NightlyRuntimeInstaller.cs', 'utf8');
assert.match(state, /includeStableLoader:\s*!activeNightly/,
  'Nightly state must bypass Stable release check');
assert.match(state, /NightlyRuntimeInstaller\.GetLatestPublishedVersionAsync/,
  'Nightly status must query the official Nightly runtime releases');
assert.match(state, /latestLoaderVersion = activeNightly \? latestNightlyVersion : managed\.LatestLoaderVersion/,
  'The Nightly UI must never show Stable as its latest release');
assert.match(nightly, /GetLatestPublishedVersionAsync/,
  'Nightly query reuses installer release discovery');
assert.match(core, /bool includeStableLoader = true/,
  'Stable remains the default for non-Nightly callers');
assert.match(ui, /Latest Nightly:/, 'The label names its channel');
assert.match(ui, /state\.loaderVersionStatus/, 'Explicit version comparison status is rendered');
assert.match(ui, /loaderUpdateMessage=state\.loaderCheckMessage/,
  'Release check message replaces its placeholder');
assert.doesNotMatch(ui, /state\.loaderCurrent\?'CURRENT':'UPDATE REQUIRED'/,
  'Nightly must not infer updates from a Stable comparison');
console.log('PASS: Nightly release panel reads official Nightly independently of Stable.');
