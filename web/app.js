const post=(action,extra={})=>window.chrome.webview.postMessage({action,...extra});
let state=null;
let installBusy=false;
let launcherUpdateVersion=null;
let launcherUpdateBusy=false;
const $=id=>document.getElementById(id);

function renderLauncherUpdateNotice(){
  const notice=$('launcher-update-notice');
  notice.hidden=!launcherUpdateVersion;
  const gameRunning=!!state?.gameRunning;
  $('launcher-update-copy').textContent=launcherUpdateVersion
    ? 'Coda found CodaLauncher '+launcherUpdateVersion+'. '+(gameRunning
      ? 'Ready to update when Minecraft closes.'
      : 'Fresh paperwork is ready whenever you are.')
    : '';
  const button=$('update-launcher');
  button.disabled=launcherUpdateBusy||installBusy||gameRunning;
  button.textContent=launcherUpdateBusy?'UPDATING…':'UPDATE LAUNCHER';
}
$('update-launcher').onclick=()=>{
  if(launcherUpdateBusy||installBusy||state?.gameRunning) return;
  launcherUpdateBusy=true;
  renderLauncherUpdateNotice();
  post('updateLauncher');
};

function setInstallBusy(busy,message=''){
  installBusy=busy;
  renderLauncherUpdateNotice();
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
function renderAccount(){
  const a=state?.account;
  if(!a) return;
  const locked=!!state.gameRunning||!!state.accountBusy||installBusy;
  $('account-status').textContent=a.status;
  $('account-name').textContent=a.signedIn?a.playerName:'Not signed in';
  $('account-verified').textContent=a.verifiedAt?'Last verified: '+new Date(a.verifiedAt).toLocaleString():'';
  $('account-storage').textContent=a.storage;
  $('account-signin').disabled=locked||!a.configured;
  $('account-verify').disabled=locked||!a.signedIn||!a.configured;
  $('account-signout').disabled=!!state.gameRunning||installBusy||!a.signedIn;
  $('account-cancel').hidden=!state.accountBusy;
  $('account-offline').checked=!!state.settings.offlineMode;
  $('account-offline').disabled=locked||(!a.offlineAvailable&&!state.settings.offlineMode);
  $('p-mc').textContent=a.signedIn?'Previously verified':'Not verified';
}
$('account-signin').onclick=()=>post('signInMicrosoft');
$('account-verify').onclick=()=>post('verifyAccount');
$('account-signout').onclick=()=>post('signOutAccount');
$('account-cancel').onclick=()=>post('cancelSignIn');
$('account-offline').onchange=()=>post('setPlayMode',{offline:$('account-offline').checked});
$('refresh').onclick=()=>post('refresh');
$('play').onclick=()=>{
  if(installBusy) return;
  setInstallBusy(true,'Checking required components…');
  post('play');
};
$('open-loader').onclick=()=>post('openLoaderFolder');
$('save').onclick=()=>post('saveSettings',{settings:{loaderPath:$('loader-path').value.trim(),feedUrl:$('feed-url').value.trim(),closeAfterLaunch:$('close-after').checked}});
window.chrome.webview.addEventListener('message',e=>{
  const m=e.data;
  if(m.type==='account'&&state){state.account=m.account;state.accountBusy=m.busy;state.settings.offlineMode=m.offline;render();}
  if(m.type==='accountMessage') $('account-message').textContent=m.message;
  if(m.type==='accountCode'){ $('account-code').hidden=!m.code; $('account-code').textContent=m.code?'Enter '+m.code+' at '+m.url:''; }

  if(m.type==='state'){
    state=m.data;
    launcherUpdateVersion=state.launcherUpdateVersion||null;
    render();
  }
  if(m.type==='launcherUpdate'){
    launcherUpdateVersion=m.version||null;
    launcherUpdateBusy=!!m.busy;
    renderLauncherUpdateNotice();
  }
  if(m.type==='log') appendLog(m.line);
  if(m.type==='installStatus'){
    const box=$('launch-message');
    box.textContent=m.message;
    box.style.color=m.ok?'#8df0bb':'#ffb28a';
    setInstallBusy(!!m.busy,m.message);
    if(!m.busy&&state) render();
  }
  if(m.type==='sessionStatus'){
    if(state) state.gameRunning=!!m.running;
    installBusy=false;
    const box=$('launch-message');
    box.textContent=m.message;
    box.style.color=m.crashed?'#ffb28a':'#8df0bb';
    render();
  }
  if(m.type==='launchStatus'){
    const box=$('launch-message');
    box.textContent=m.message;
    box.style.color=m.ok?'#8df0bb':'#ffb28a';
  }
  if(m.type==='error') $('launch-message').textContent=m.message;
});
function render(){
  renderAccount();
  renderLauncherUpdateNotice();
  $('version').textContent='CodaLauncher '+state.launcherVersion;
  $('home-heading').textContent=state.gameRunning?'World session active.':'Ready when you are.';
  $('coda-status').textContent=state.gameRunning?'on standby':'clipboard online';
  $('loader-chip').textContent=state.loaderCurrent?'CML CURRENT':(state.loaderReady?'CML UPDATE READY':'CML INSTALL');
  $('loader-chip').className=state.loaderCurrent?'good':(state.loaderReady?'warn':'bad');
  $('pack-chip').textContent=state.basePackReady?'CML BASE CURRENT':(state.managedInstalled?'CML BASE UPDATE READY':'CML BASE INSTALL');
  $('pack-chip').className=state.basePackReady?'good':(state.managedInstalled?'warn':'bad');
  $('mod-chip').textContent=state.modCount+' mod'+(state.modCount===1?'':'s');
  $('play').disabled=installBusy||state.gameRunning||state.accountBusy||!state.account?.signedIn||(state.settings.offlineMode&&!state.account.offlineAvailable);
  if(state.gameRunning) $('play').textContent='RUNNING';
  else if(!installBusy) $('play').textContent=state.settings.offlineMode?'PLAY OFFLINE ▶':state.managedInstalled?'PLAY ▶':'INSTALL & PLAY ▶';
  $('loader-summary').textContent=state.gameRunning
    ? 'Minecraft is running. Coda is keeping the clipboard warm.'
    : state.managedCurrent
      ? 'Coda checked the essentials. Everything is where it belongs.'
      : state.managedInstalled
        ? 'Coda found a few things that need freshening up. PLAY will handle them automatically.'
        : 'Coda will install CodaLoader, CML Base and the required Resourcepacks for you.';
  const feed=state.feed;
  $('feed-pill').textContent=feed.online?'NEWS ONLINE':'NEWS OFFLINE';
  $('feed-pill').className='pill '+(feed.online?'online':'offline');
  $('feed-note').textContent=feed.online?'Fresh notes from Howling Whispers.':"Coda can't reach the bulletin board right now.";
  $('news').innerHTML=(feed.news||[]).map(n=>'<article><time>'+esc(n.date||'')+'</time><b>'+esc(n.title||'Untitled')+'</b><p>'+esc(n.text||'')+'</p>'+(n.link?'<button class="news-link" data-link="'+escAttr(n.link)+'">Open</button>':'')+'</article>').join('');
  document.querySelectorAll('.news-link').forEach(btn=>btn.onclick=()=>post('openExternal',{url:btn.dataset.link}));
  $('packs-list').innerHTML=(state.packs||[]).map(p=>'<article class="pack-card"><div class="pack-top"><div><em>'+(p.required?'REQUIRED':'OPTIONAL')+'</em><h3>'+esc(p.name)+'</h3></div><span class="pill '+(p.current?'online':(p.installed?'update':'offline'))+'">'+esc(p.status)+'</span></div><p>'+esc(p.description)+'</p><div class="dependency-note">Requires: '+esc((p.dependencies||[]).join(', ')||'None')+'</div><div class="pack-meta"><span>Available v'+esc(p.availableVersion||'?')+'</span><span>'+esc(p.source||'')+'</span></div>'+(p.required?'<div class="managed-label">Managed automatically when you press PLAY</div>':'<button class="pack-action save" data-pack="'+escAttr(p.id)+'">INSTALL</button>')+'</article>').join('');
  document.querySelectorAll('.pack-action').forEach(btn=>{
    btn.disabled=installBusy;
    if(installBusy) btn.textContent='WORKING…';
    btn.onclick=requestInstall;
  });
  $('resourcepacks-list').innerHTML=(state.resourcePacks||[]).map(r=>'<article class="pack-card"><div class="pack-top"><div><em>'+(r.required?'REQUIRED DEPENDENCY':'OPTIONAL')+'</em><h3>'+esc(r.name)+'</h3></div><span class="pill '+(r.current?'online':(r.installed?'update':'offline'))+'">'+esc(r.status)+'</span></div><p>'+esc(r.description)+'</p><div class="dependency-note">Required by: '+esc((r.requiredBy||[]).join(', ')||'None')+'</div><div class="contents-note">'+(r.contents||[]).map(x=>'<span>'+esc(x)+'</span>').join('')+'</div><div class="pack-meta"><span>Available v'+esc(r.availableVersion||'?')+'</span><span>'+esc(r.source||'')+'</span></div>'+(r.required?'<div class="managed-label">Managed automatically by '+esc((r.requiredBy||[]).join(', ')||'CML')+'</div>':'<button class="resourcepack-action save" data-resourcepack="'+escAttr(r.id)+'">INSTALL</button>')+'</article>').join('');
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
  $('mods-list').innerHTML=state.mods.length?state.mods.map(m=>'<div class="mod"><div><b>'+esc(m.name)+'</b><small>'+esc(m.id)+' · '+esc(m.version)+' · '+esc(m.fileName)+'</small></div><div class="'+(m.valid?'':'bad-text')+'">'+(m.valid?'Ready':'Invalid')+'</div>'+(m.error?'<small class="bad-text">'+esc(m.error)+'</small>':'')+'</div>').join(''):'<div class="mod"><div><b>No CML mods found</b><small>It is suspiciously tidy in here.</small></div></div>';
  $('loader-path').value=state.settings.loaderPath||'';$('feed-url').value=state.settings.feedUrl||'';$('close-after').checked=!!state.settings.closeAfterLaunch;
  $('p-cml').textContent=state.profile.cmlAccount;$('p-mc').textContent=state.profile.minecraftOwnership;$('p-discord').textContent=state.profile.discord;
  $('log-output').textContent=(state.logs||[]).join('\n')||'Nothing interesting has happened yet.';
}
function appendLog(line){const pre=$('log-output');pre.textContent=(pre.textContent==='Nothing interesting has happened yet.'?'':pre.textContent+'\n')+line;pre.parentElement.scrollTop=pre.parentElement.scrollHeight;}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function escAttr(v){return esc(v).replace(/\x60/g,'&#96;');}
post('ready');