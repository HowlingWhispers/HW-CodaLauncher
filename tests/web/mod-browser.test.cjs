'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const path=require('node:path');
const els=new Map(), listeners={}, posted=[];
function el(id){
  if(!els.has(id))els.set(id,{
    textContent:'',innerHTML:'',value:'',hidden:false,disabled:false,
    style:{},className:'',dataset:{},parentElement:{scrollTop:0},
    classList:{add(){},remove(){},toggle(){}},
    setAttribute(){},addEventListener(){},onclick:null,onchange:null,oninput:null
  });
  return els.get(id);
}
const sandbox={
  document:{getElementById:el,querySelectorAll:()=>[]},
  window:{chrome:{webview:{
    postMessage:m=>posted.push(m),
    addEventListener:(type,cb)=>{listeners[type]=cb;}
  }},confirm:()=>false}
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../../web/app.js'),'utf8'),sandbox);
assert.equal(posted[0].action,'ready');
const base={
  launcherVersion:'0.7.30',gameRunning:false,accountBusy:false,
  managedCurrent:true,managedInstalled:true,basePackReady:true,loaderCurrent:true,loaderReady:true,
  activeChannel:'nightly',loaderVersionStatus:'CURRENT',localSingleplayer:true,
  launcherUpdateVersion:null,modCount:1,mods:[],worlds:[],news:[],
  feed:{newsSource:'Bundled',news:[]},packs:[],resourcePacks:[],
  profile:{cmlAccount:'',discord:''},
  settings:{updateChannel:'nightly',localTestMode:true},
  optionalMods:[
    {id:'github:howl-sorting-pipes',name:'H.O.W.L. Sorting Pipes',installed:true,
     managed:true,required:true,nightlyOnly:true,version:'1.0.0',availableVersion:'1.0.0',
     source:'GitHub: HowlingWhispers/HW-Mods'},
    {id:'github:howl-crystals',name:'HOWL Crystals',installed:false,managed:false,
     required:false,nightlyOnly:true,version:'',availableVersion:'0.1.0',source:'GitHub: HowlingWhispers/HW-Mods'},
    {id:'hw_essentials',name:'HW Essentials',installed:true,managed:true,
     required:true,nightlyOnly:false,version:'0.2.0'},
    {id:'github:unsafe',name:'<img src=x onerror=alert(1)>',installed:false,
     required:false,nightlyOnly:false,availableVersion:'0.0.1',source:'GitHub: HowlingWhispers/HW-Mods'}
  ]
};
listeners.message({data:{type:'state',data:base}});
assert.match(el('optional-mods').innerHTML,/H.O.W.L. Sorting Pipes/);
assert.match(el('optional-mods').innerHTML,/HOWL Crystals/);
assert.equal(el('mod-browser-count').textContent,'4 of 4 mods · Nightly');
assert.doesNotMatch(el('optional-mods').innerHTML,/<img src=x/,'remote name escaped');
el('mod-browser-search').value='crystals';
el('mod-browser-search').oninput();
assert.equal(el('mod-browser-count').textContent,'1 of 4 mods · Nightly');
assert.match(el('optional-mods').innerHTML,/HOWL Crystals/);
assert.doesNotMatch(el('optional-mods').innerHTML,/Sorting Pipes/);
el('mod-browser-search').value='';
el('mod-browser-search').oninput();
el('mod-browser-filter').value='required';
el('mod-browser-filter').onchange();
assert.equal(el('mod-browser-count').textContent,'2 of 4 mods · Nightly');
assert.doesNotMatch(el('optional-mods').innerHTML,/data-mod="github:howl-sorting-pipes"/,
  'required Nightly mod has no manual action');
el('mod-browser-filter').value='available';
el('mod-browser-filter').onchange();
assert.equal(el('mod-browser-count').textContent,'2 of 4 mods · Nightly');
assert.doesNotMatch(el('optional-mods').innerHTML,/HW Essentials/);
el('mod-browser-search').value='not a mod';
el('mod-browser-search').oninput();
assert.match(el('optional-mods').innerHTML,/No mods match/);
base.activeChannel='stable';
base.settings.updateChannel='stable';
listeners.message({data:{type:'state',data:base}});
el('mod-browser-search').value='';
el('mod-browser-search').oninput();
assert.match(el('optional-mods').innerHTML,/Available in Nightly only/,
  'Stable listings cannot enable Nightly-only mods');
console.log('PASS: H.O.W.L. mod browser search, filters, status and escaping');
