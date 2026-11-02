import assert from 'node:assert/strict';import vm from 'node:vm';import fs from 'node:fs';
let checks=0,calls=0,reloads=0,offline=false,fail=false;const events={},pageEvents={},windowEvents={},timers=new Map();let id=0;
const marker={addEventListener:(name,fn)=>events[name]=fn};const context={document:{hidden:false,getElementById:()=>marker,addEventListener:(name,fn)=>pageEvents[name]=fn},window:{addEventListener:(name,fn)=>windowEvents[name]=fn},navigator:{get onLine(){return !offline;}},location:{reload:()=>reloads++},Blazor:{reconnect:async()=>{calls++;if(fail)throw Error('offline');return true;},resumeCircuit:async()=>false},setTimeout:(fn)=>{timers.set(++id,fn);return id;},clearTimeout:(key)=>timers.delete(key)};
vm.runInNewContext(fs.readFileSync('Components/Layout/ReconnectModal.razor.js','utf8'),context);
async function tick(){await Promise.resolve();await Promise.resolve();}
function check(ok,name){assert.ok(ok,name);checks++;}
pageEvents.visibilitychange();await tick();check(calls===0,'Healthy tab visibility does not attempt reconnect or reload');
events['components-reconnect-state-changed']({detail:{state:'show'}});check(calls===0,'Initial disconnect leaves retries to Blazor without opening a modal');
events['components-reconnect-state-changed']({detail:{state:'failed'}});const fn=[...timers.values()].at(-1);timers.clear();await fn();check(calls===1,'Failed initial reconnect retries silently');
events['components-reconnect-state-changed']({detail:{state:'hide'}});windowEvents.online();await tick();check(calls===1,'Recovered connection stops further retries');
events['components-reconnect-state-changed']({detail:{state:'show'}});offline=true;windowEvents.online();await tick();check(calls===1,'Offline state does not loop connection attempts');offline=false;fail=true;await windowEvents.online();check(timers.size>0,'Transient network failure schedules recovery');
events['components-reconnect-state-changed']({detail:{state:'rejected'}});check(reloads===1,'Rejected server circuit reloads the page');
console.log(`${checks} silent reconnection checks passed.`);
