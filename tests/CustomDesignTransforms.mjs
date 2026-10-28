import assert from 'node:assert/strict';
import {normalizeAngle,constrainLayer,resizeAroundCenter} from '../Components/Pages/CustomDesignStudio.razor.js';
let checks=0;
function check(ok,name){assert.ok(ok,name);checks++;}
function inside(l){const a=l.rotation*Math.PI/180,c=Math.abs(Math.cos(a)),s=Math.abs(Math.sin(a)),hx=(c*l.width+s*l.height/.8)/2,hy=(s*l.width*.8+c*l.height)/2,cx=l.x+l.width/2,cy=l.y+l.height/2;return cx-hx>=-1e-9&&cy-hy>=-1e-9&&cx+hx<=1+1e-9&&cy+hy<=1+1e-9&&l.width<=1&&l.height<=1;}
for(const angle of [-540,-181,-180,-90,-45,0,30,90,180,270,540])check(normalizeAngle(angle)>=-180&&normalizeAngle(angle)<180,'Angles normalize safely');
for(let angle=-180;angle<=180;angle+=15){const l=constrainLayer({x:-.1,y:.95,width:.7,height:.4,rotation:angle});check(inside(l),`Rotated bounds stay inside at ${angle} degrees`);check(Math.abs(l.width/l.height-1.75)<1e-8,'Constraining preserves artwork aspect ratio');}
const base={x:.3,y:.3,width:.3,height:.15,rotation:37};const bigger=resizeAroundCenter({...base},1.2);check(Math.abs((bigger.x+bigger.width/2)-.45)<1e-8&&Math.abs((bigger.y+bigger.height/2)-.375)<1e-8,'Handle resize preserves center');check(Math.abs(bigger.width/bigger.height-2)<1e-8,'Handle resize preserves proportions');check(bigger.rotation===37,'Resize preserves rotation');
check(inside(resizeAroundCenter({...base},100)),'Oversized drag fits rotated artwork within the canvas');check(resizeAroundCenter({...base},0).height>=.02,'Resizing cannot collapse the artwork');
check(inside(constrainLayer({x:.2,y:.2,width:1.1,height:.1,rotation:45})),'Unrotated dimension limits also hold for a diagonal layout');
console.log(`${checks} artwork transform checks passed.`);
