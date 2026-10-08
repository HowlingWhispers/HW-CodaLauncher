'use strict';
function showView(id){
  document.querySelectorAll('.view').forEach(x=>x.classList.toggle('active',x.id===id));
  document.querySelectorAll('.nav').forEach(x=>x.classList.toggle('active',x.dataset.view===id || (x.dataset.view==='mods' && ['packs','resourcepacks'].includes(id))));
  document.querySelectorAll('[data-shelf]').forEach(x=>x.classList.toggle('active',x.dataset.shelf===id));
  document.querySelector('main').scrollTop=0;
  if(id==='mods') post('refreshMods');
}
document.querySelectorAll('[data-shelf]').forEach(x=>x.onclick=()=>showView(x.dataset.shelf));
document.querySelectorAll('.nav').forEach(x=>x.addEventListener('click',()=>showView(x.dataset.view)));
$('profile-link').onclick=()=>showView('profile');
$('help-home').onclick=()=>showView('home');
$('repair-game').onclick=()=>requestInstall();
$('help-repair').onclick=()=>{showView('home');requestInstall();};
let quiet=false;
try{quiet=localStorage.getItem('coda-edition-quiet')==='true';}catch{}
$('quiet-coda').checked=quiet;
document.body.classList.toggle('quiet-coda',quiet);
$('quiet-coda').onchange=()=>{quiet=$('quiet-coda').checked;document.body.classList.toggle('quiet-coda',quiet);try{localStorage.setItem('coda-edition-quiet',String(quiet));}catch{}};
const antics=['Clipboard? Check. You? Check. Excellent.','That was a boop. It goes in the report.','I packed enthusiasm. Possibly too much.','The desk is tidy. Please don’t open that drawer.','One adventure, coming right up.','I’m supervising. The ears are part of the uniform.'];
let antic=0;
$('coda-boop').onclick=()=>{
  $('coda-speech').textContent=quiet?'Good to see you.':antics[antic++%antics.length];
  if(!quiet){$('coda-boop').classList.remove('boop');void $('coda-boop').offsetWidth;$('coda-boop').classList.add('boop');}
};
showView('home');
