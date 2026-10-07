const post=(action,extra={})=>window.chrome.webview.postMessage({action,...extra});
let state=null;
const $=id=>document.getElementById(id);
document.querySelectorAll('.nav').forEach(btn=>btn.addEventListener('click',()=>{document.querySelectorAll('.nav').forEach(x=>x.classList.remove('active'));document.querySelectorAll('.view').forEach(x=>x.classList.remove('active'));btn.classList.add('active');$(btn.dataset.view).classList.add('active');}));
$('refresh').onclick=()=>post('refresh');
$('play').onclick=()=>post('play');
$('open-loader').onclick=()=>post('openLoaderFolder');
$('save').onclick=()=>post('saveSettings',{settings:{loaderPath:$('loader-path').value.trim(),feedUrl:$('feed-url').value.trim(),closeAfterLaunch:$('close-after').checked}});
window.chrome.webview.addEventListener('message',e=>{const m=e.data;if(m.type==='state'){state=m.data;render();}if(m.type==='log')appendLog(m.line);if(m.type==='launchStatus'){const box=$('launch-message');box.textContent=m.message;box.style.color=m.ok?'#8df0bb':'#ffb28a';}if(m.type==='error')$('launch-message').textContent=m.message;});
function render(){
  $('version').textContent='CodaLauncher '+state.launcherVersion;
  $('loader-chip').textContent=state.loaderReady?'CML READY':'CML NOT CONFIGURED';
  $('loader-chip').className=state.loaderReady?'good':'bad';
  $('mod-chip').textContent=state.modCount+' mod'+(state.modCount===1?'':'s');
  $('play').disabled=!state.loaderReady;
  $('loader-summary').textContent=state.loaderReady?state.loaderPath:'Choose your CodaLoader folder once, then CodaLauncher becomes the front door.';
  const feed=state.feed;$('feed-pill').textContent=feed.online?'NEWS ONLINE':'NEWS OFFLINE';$('feed-pill').className='pill '+(feed.online?'online':'offline');$('feed-note').textContent=feed.online?'Live launcher feed':'Local fallback';
  $('news').innerHTML=(feed.news||[]).map(n=>'<article><time>'+esc(n.date||'')+'</time><b>'+esc(n.title||'Untitled')+'</b><p>'+esc(n.text||'')+'</p></article>').join('');
  $('mods-count').textContent=state.mods.length+' jar'+(state.mods.length===1?'':'s');
  $('mods-list').innerHTML=state.mods.length?state.mods.map(m=>'<div class="mod"><div><b>'+esc(m.name)+'</b><small>'+esc(m.id)+' · '+esc(m.version)+' · '+esc(m.fileName)+'</small></div><div class="'+(m.valid?'':'bad-text')+'">'+(m.valid?'Ready':'Invalid')+'</div>'+(m.error?'<small class="bad-text">'+esc(m.error)+'</small>':'')+'</div>').join(''):'<div class="mod"><div><b>No CML mods found</b><small>run\\mods is empty or the loader path is not configured.</small></div></div>';
  $('loader-path').value=state.settings.loaderPath||'';$('feed-url').value=state.settings.feedUrl||'';$('close-after').checked=!!state.settings.closeAfterLaunch;
  $('p-cml').textContent=state.profile.cmlAccount;$('p-mc').textContent=state.profile.minecraftOwnership;$('p-discord').textContent=state.profile.discord;
  $('log-output').textContent=(state.logs||[]).join('\n')||'No launcher logs yet.';
}
function appendLog(line){const pre=$('log-output');pre.textContent=(pre.textContent==='No launcher logs yet.'?'':pre.textContent+'\n')+line;pre.parentElement.scrollTop=pre.parentElement.scrollHeight;}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
post('ready');