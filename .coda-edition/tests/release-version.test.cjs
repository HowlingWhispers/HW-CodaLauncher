'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
function read(path) { return fs.readFileSync(path, 'utf8'); }
function version(path) {
  const match = read(path).match(/<Version>([^<]+)<\/Version>/);
  assert.ok(match, `Missing version in ${path}`);
  return match[1];
}
const current = version('CodaLauncher.csproj');
assert.equal(version('Desktop/CodaLauncher.Desktop.csproj'), current, 'Platform package versions differ');
const windows = read('App.xaml.cs');
const desktop = read('Desktop/Program.cs');
assert.match(windows, /LauncherVersion\s*=\s*[\s\S]*?AssemblyInformationalVersionAttribute/, 'Windows version must use assembly metadata');
assert.match(desktop, /Version\s*=\s*[\s\S]*?AssemblyInformationalVersionAttribute/, 'Desktop version must use assembly metadata');
assert.doesNotMatch(windows, /LauncherVersion\s*=\s*["']/);
assert.doesNotMatch(desktop, /Version\s*=\s*["']/);
const release = read('.github/workflows/release.yml');
assert.ok(release.includes(`CODA_VERSION: ${current}`), 'Release version drift');
assert.ok(release.includes(`CODA_TAG: v${current}`), 'Release tag drift');
assert.ok(read('Desktop/Info.plist').includes(`<key>CFBundleShortVersionString</key><string>${current}</string>`), 'macOS bundle drift');
assert.ok(read('packaging/windows/CodaLauncher.iss').includes(`#define AppVersion "${current}"`), 'Installer version drift');
assert.ok(read('.github/workflows/build.yml').includes(`-Version ${current}`), 'CI installer version drift');
console.log(`PASS: CodaLauncher version metadata and release tag all agree on ${current}`);
