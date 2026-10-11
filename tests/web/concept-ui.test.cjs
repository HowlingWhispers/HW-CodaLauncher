'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const html = fs.readFileSync('web/index.html', 'utf8');
const code = fs.readFileSync('web/app.js', 'utf8');
const win = fs.readFileSync('MainWindow.xaml.cs', 'utf8');
const portable = fs.readFileSync('Desktop/DesktopWindow.cs', 'utf8');
const concept = fs.readFileSync('Desktop/LauncherConceptUI.cs', 'utf8');

// These are functional launcher controls, not the simulated setTimeout demo.
for (const id of ['play','update-launcher','verification-label','worlds-list','open-worlds-folder','channel-note']) {
  assert.ok(html.includes('id="'+id+'"'), 'Missing concept control '+id);
}
for (const view of ['home','addons','worlds','settings','logs','profile'])
  assert.ok(html.includes('id="'+view+'"'), 'Missing view '+view);
assert.ok(!code.includes('setMoon('), 'Moon must not report simulated verification');
assert.ok(win.includes('case "openWorldsFolder":') && win.includes('case "refreshWorlds":'));
assert.ok(win.includes('worlds = ListWorlds()'), 'Worlds must come from the actual active game root');
assert.ok(win.includes('case "updateLauncher":') && win.includes('await OpenLauncherUpdateAsync()'));
assert.ok(portable.includes('LauncherConceptUI') && concept.includes('ShowManaged'));
assert.match(html,/Not yet implemented\. Release metadata is not a cryptographic trust proof/);

const ids = new Map(), calls = [], handlers = {};
function el(id) {
  if (!ids.has(id)) ids.set(id, {
    textContent:'', innerHTML:'', className:'', value:'', hidden:false,
    checked:false, disabled:false, style:{}, dataset:{},
    parentElement:{scrollTop:0},
    classList:{ add(){}, remove(){}, toggle(){} },
    click(){ this.onclick?.(); }
  });
  return ids.get(id);
}
const channels=['stable','nightly'].map(channel=>({
  dataset:{channel}, disabled:false, classList:{toggle(){}},
  setAttribute(){}, addEventListener(_name,listener){ this.click=listener; }
}));
const browser={
  document: {
    getElementById: el,
    querySelectorAll(selector){ return selector==='.channel'?channels:[]; }
  },
  window: { chrome: { webview: {
    postMessage(value){calls.push(value);},
    addEventListener(event,handler){handlers[event]=handler;}
  } } }
};
vm.runInNewContext(code,browser);
assert.equal(calls[0].action,'ready');
handlers.message({data:{type:'state',data:{
  launcherVersion:'0.7.29',activeChannel:'stable',gameRunning:false,
  accountBusy:false,localSingleplayer:false,managedCurrent:true,
  loaderCurrent:true,loaderReady:true,basePackReady:true,
  installedLoaderVersion:'0.0.32',latestLoaderVersion:'0.0.32',
  modCount:0,mods:[],optionalMods:[],packs:[],resourcePacks:[],
  worlds:[{name:'Home <Test>',lastModified:'2026-10-11T10:00:00Z',hasLevelDat:true}],
  settings:{updateChannel:'stable',localTestMode:false,feedUrl:'https://example.invalid'},
  account:{configured:false,signedIn:false,offlineAvailable:false,storage:''},
  profile:{cmlAccount:'',discord:''},feed:{news:[]},logs:[]
}}});
assert.equal(el('verification-label').textContent,'NOT SIGNED','Local version match is not a signature');
assert.match(el('worlds-list').innerHTML,/Home &lt;Test&gt;/,'World names must be HTML-escaped');
assert.doesNotMatch(el('worlds-list').innerHTML,/<Test>/,'Unsafe world name rendered');
channels[1].click();
assert.equal(calls.at(-1).action,'saveSettings','Sidebar channel uses native save');
assert.equal(calls.at(-1).settings.updateChannel,'nightly');
assert.equal(calls.at(-1).settings.localTestMode,true,'Nightly requires local isolation');
console.log('PASS: concept controls, live worlds, trust warning, channel switch and native updater');
