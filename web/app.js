const post=(action,extra={})=>window.chrome.webview.postMessage({action,...extra});
let state=null;
let installBusy=false;
const $=id=>document.getElementById(id);

function setInstallBusy(busy,message=''){
  installBusy=busy;
  const home=$('play');
  if(home){
    home.disabled=busy;
    if(busy) home.textContent='WORKING…';
  }
  document.querySelectorAll('.pack-action,.resourcepack-action').forEach(btn=>{
    btn.disabled=busy;
    if(busy) btn.textContent='WORKING…';
  });
  if(message){
    const box=$('launch-message');
    if(box) box.textContent=message;
  }
}

function requestInstall(){
  if(installBusy) return;
  setInstallBusy(true,'Preparing install…');
  post('install');
}
document.querySelectorAll('.nav').forEach(btn=>btn.addEventListener('click',()=>{document.querySelectorAll('.nav').forEach(x=>x.classList.remove('active'));document.querySelectorAll('.view').forEach(x=>x.classList.remove('active'));btn.classList.add('active');$(btn.dataset.view).classList.add('active');}));
$('refresh').onclick=()=>post('refresh');
$('play').onclick=()=>{
  if(state&&state.readyToPlay) post('play');
  else requestInstall();
};
$('open-loader').onclick=()=>post('openLoaderFolder');
$('save').onclick=()=>post('saveSettings',{settings:{loaderPath:$('loader-path').value.trim(),feedUrl:$('feed-url').value.trim(),closeAfterLaunch:$('close-after').checked}});
window.chrome.webview.addEventListener('message',e=>{const m=e.data;if(m.type==='state'){state=m.data;render();}if(m.type==='log')appendLog(m.line);if(m.type==='installStatus'){const box=$('launch-message');box.textContent=m.message;box.style.color=m.ok?'#8df0bb':'#ffb28a';setInstallBusy(!!m.busy,m.message);if(!m.busy&&state)render();}if(m.type==='launchStatus'){const box=$('launch-message');box.textContent=m.message;box.style.color=m.ok?'#8df0bb':'#ffb28a';}if(m.type==='error')$('launch-message').textContent=m.message;});
function render(){
  $('version').textContent='CodaLauncher '+state.launcherVersion;
  $('loader-chip').textContent=state.loaderReady?'CML READY':'CML NOT CONFIGURED';
  $('loader-chip').className=state.loaderReady?'good':'bad';
  $('pack-chip').textContent=state.basePackReady?'CML BASE READY':'CML BASE REQUIRED';
  $('pack-chip').className=state.basePackReady?'good':'bad';
  $('mod-chip').textContent=state.modCount+' mod'+(state.modCount===1?'':'s');
  $('play').disabled=installBusy;
  if(!installBusy) $('play').textContent=state.readyToPlay?'PLAY ▶':(state.loaderReady?'REPAIR':'INSTALL');
  $('loader-summary').textContent=state.readyToPlay
    ? state.minecraftRoot
    : 'Install CodaLoader, CML Base and its required Resourcepacks.';
  const feed=state.feed;$('feed-pill').textContent=feed.online?'NEWS ONLINE':'NEWS OFFLINE';$('feed-pill').className='pill '+(feed.online?'online':'offline');$('feed-note').textContent=feed.online?'Live launcher feed':'Local fallback';
  $('news').innerHTML=(feed.news||[]).map(n=>'<article><time>'+esc(n.date||'')+'</time><b>'+esc(n.title||'Untitled')+'</b><p>'+esc(n.text||'')+'</p>'+(n.link?'<button class="news-link" data-link="'+escAttr(n.link)+'">Open</button>':'')+'</article>').join('');
  document.querySelectorAll('.news-link').forEach(btn=>btn.onclick=()=>post('openExternal',{url:btn.dataset.link}));
  $('packs-list').innerHTML=(state.packs||[]).map(p=>'<article class="pack-card"><div class="pack-top"><div><em>'+(p.required?'REQUIRED':'OPTIONAL')+'</em><h3>'+esc(p.name)+'</h3></div><span class="pill '+(p.installed?'online':'offline')+'">'+esc(p.status)+'</span></div><p>'+esc(p.description)+'</p><div class="dependency-note">Requires: '+esc((p.dependencies||[]).join(', ')||'None')+'</div><div class="pack-meta"><span>Available v'+esc(p.availableVersion||'?')+'</span><span>'+esc(p.source||'')+'</span></div><button class="pack-action '+(p.installed?'quiet':'save')+'" data-pack="'+escAttr(p.id)+'">'+(p.installed?'REPAIR':'INSTALL')+'</button></article>').join('');
  document.querySelectorAll('.pack-action').forEach(btn=>{
    btn.disabled=installBusy;
    if(installBusy) btn.textContent='WORKING…';
    btn.onclick=requestInstall;
  });
  $('resourcepacks-list').innerHTML=(state.resourcePacks||[]).map(r=>'<article class="pack-card"><div class="pack-top"><div><em>'+(r.required?'REQUIRED DEPENDENCY':'OPTIONAL')+'</em><h3>'+esc(r.name)+'</h3></div><span class="pill '+(r.installed?'online':'offline')+'">'+esc(r.status)+'</span></div><p>'+esc(r.description)+'</p><div class="dependency-note">Required by: '+esc((r.requiredBy||[]).join(', ')||'None')+'</div><div class="contents-note">'+(r.contents||[]).map(x=>'<span>'+esc(x)+'</span>').join('')+'</div><div class="pack-meta"><span>Available v'+esc(r.availableVersion||'?')+'</span><span>'+esc(r.source||'')+'</span></div><button class="resourcepack-action '+(r.installed?'quiet':'save')+'" data-resourcepack="'+escAttr(r.id)+'">'+(r.installed?'REPAIR':'INSTALL')+'</button></article>').join('');
  document.querySelectorAll('.resourcepack-action').forEach(btn=>{
    btn.disabled=installBusy;
    if(installBusy) btn.textContent='WORKING…';
    btn.onclick=()=>{
      if(installBusy) return;
      setInstallBusy(true,'Preparing Resourcepack install…');
      post('installResourcePack');
    };
  });
  $('mods-count').textContent=state.mods.length+' jar'+(state.mods.length===1?'':'s');
  $('mods-list').innerHTML=state.mods.length?state.mods.map(m=>'<div class="mod"><div><b>'+esc(m.name)+'</b><small>'+esc(m.id)+' · '+esc(m.version)+' · '+esc(m.fileName)+'</small></div><div class="'+(m.valid?'':'bad-text')+'">'+(m.valid?'Ready':'Invalid')+'</div>'+(m.error?'<small class="bad-text">'+esc(m.error)+'</small>':'')+'</div>').join(''):'<div class="mod"><div><b>No CML mods found</b><small>run\\mods is empty or the loader path is not configured.</small></div></div>';
  $('loader-path').value=state.settings.loaderPath||'';$('feed-url').value=state.settings.feedUrl||'';$('close-after').checked=!!state.settings.closeAfterLaunch;
  $('p-cml').textContent=state.profile.cmlAccount;$('p-mc').textContent=state.profile.minecraftOwnership;$('p-discord').textContent=state.profile.discord;
  $('log-output').textContent=(state.logs||[]).join('\n')||'No launcher logs yet.';
}
function appendLog(line){const pre=$('log-output');pre.textContent=(pre.textContent==='No launcher logs yet.'?'':pre.textContent+'\n')+line;pre.parentElement.scrollTop=pre.parentElement.scrollHeight;}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function escAttr(v){return esc(v).replace(/\x60/g,'&#96;');}
post('ready');