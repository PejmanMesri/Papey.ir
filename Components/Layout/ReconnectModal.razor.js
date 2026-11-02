// Let Blazor reconnect normally, without an overlay interrupting browsing.
const marker=document.getElementById('components-reconnect-modal');
let timer, running=false, disconnected=false;
function stop(){clearTimeout(timer);timer=undefined;}
async function retry(){
 if(!disconnected||running||document.hidden||!navigator.onLine)return;
 running=true;
 try { const ok=await Blazor.reconnect(); if(!ok){const resumed=await Blazor.resumeCircuit();if(!resumed)location.reload();}else stop(); }
 catch { stop();timer=setTimeout(retry,5000); }
 finally {running=false;}
}
marker?.addEventListener('components-reconnect-state-changed',event=>{
 if(event.detail.state==='hide'){disconnected=false;stop();}
 else if(event.detail.state==='show')disconnected=true;
 else if(event.detail.state==='rejected')location.reload();
 else if(event.detail.state==='failed'||event.detail.state==='paused'){disconnected=true;stop();timer=setTimeout(retry,1000);}
});
window.addEventListener('online',retry);
document.addEventListener('visibilitychange',()=>{if(!document.hidden)retry();});
