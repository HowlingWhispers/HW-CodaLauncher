// Browser-only design demo. Native WebView2 supplies the real launcher bridge.
// Never simulate a successful game launch or installation in the browser.
(() => {
  if(window.chrome?.webview) return;
  const listeners=[];
  const send=data=>listeners.forEach(fn=>fn({data}));
  const demo={launcherVersion:'0.7.15',activeChannel:'stable',gameRunning:false,accountBusy:false,localSingleplayer:false,managedCurrent:true,managedInstalled:true,loaderCurrent:true,loaderReady:true,basePackReady:true,modCount:1,installedLoaderVersion:'0.0.28',latestLoaderVersion:'0.0.28',mods:[{id:'hw_essentials',name:'HW Essentials',version:'0.2.0',fileName:'hw-essentials-0.2.0.jar',valid:true}],packs:[{id:'howl',name:'H.O.W.L.',description:'Your Howling Whispers adventure, with its required mods and presentation ready together.',required:true,current:true,installed:true,availableVersion:'0.0.28',source:'Illustrative demo data',status:'Current',dependencies:['HOWL Base Resources']}],resourcePacks:[{id:'base',name:'HOWL Base Resources',description:'Coda’s menus, music and little touches of home.',required:true,current:true,installed:true,availableVersion:'1',source:'Illustrative demo data',status:'Current',requiredBy:['H.O.W.L.'],contents:['Menu artwork','Music','Coda splashes']}],settings:{feedUrl:'https://thehowlingwhispers.com/launcher',loaderPath:'',localTestMode:false,updateChannel:'stable',closeAfterLaunch:false},profile:{cmlAccount:'Not configured',discord:'Not linked'},account:{configured:false,signedIn:false,storage:'This browser demo has no access to your account or files.'},logs:['[Design demo] This is illustrative data, not an installed game.'],feed:{newsSource:'Bundled',news:[{date:'FROM THE WORKSHOP',title:'A little quieter underground',text:'Meet the Quiet Underground prototype: a different rhythm for brand-new adventures. Development playtests are still in progress.'},{date:'A NOTE FROM CODA',title:'Fresh from the workshop',text:'A warmer little desk, a freshly sharpened pencil, and room for your next big idea. Welcome to the Coda Edition.'}]}};
  window.chrome=window.chrome||{};
  window.chrome.webview={addEventListener:(name,fn)=>{if(name==='message') listeners.push(fn);},postMessage:async message=>{
    const action=message.action;
    if(action==='ready'||action==='refresh'){send({type:'state',data:demo});return;}
    if(action==='refreshMods'){send({type:'modsState',mods:demo.mods,modCount:demo.modCount});return;}
    if(action==='saveSettings'){demo.settings={...demo.settings,...message.settings};demo.localSingleplayer=demo.settings.localTestMode;demo.activeChannel=demo.settings.updateChannel;send({type:'state',data:demo});send({type:'settingsSaveResult',ok:true});$('settings-save-result').textContent='Demo settings applied for this session.';return;}
    if(action==='copyAllLogs'){try{await navigator.clipboard.writeText(demo.logs.join('\n'));send({type:'copyLogsResult',ok:true,count:demo.logs.length});}catch{send({type:'copyLogsResult',ok:false,message:'Your browser did not allow clipboard access.'});}return;}
    if(['play','install','installResourcePack'].includes(action)){send({type:'installStatus',busy:false,ok:false,message:'Browser demo only. Open the desktop Coda Edition to install or launch Minecraft.'});return;}
    if(action==='openExternal'){try{const url=new URL(message.url);if(url.protocol==='https:')window.open(url.href,'_blank','noopener,noreferrer');}catch{}return;}
    send({type:'error',message:'This action needs the desktop Coda Edition. The browser demo cannot access your game files.'});
  }};
  document.addEventListener('DOMContentLoaded',()=>{document.getElementById('demo-badge').hidden=false;});
})();
