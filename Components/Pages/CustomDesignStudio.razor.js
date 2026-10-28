export function normalizeAngle(angle) { return ((angle + 180) % 360 + 360) % 360 - 180; }
export function constrainLayer(l, aspect = .8) {
    l.rotation = normalizeAngle(l.rotation || 0);
    let cx = l.x + l.width / 2, cy = l.y + l.height / 2;
    const a = l.rotation * Math.PI / 180, cos = Math.abs(Math.cos(a)), sin = Math.abs(Math.sin(a));
    let hx = (cos * l.width + sin * l.height / aspect) / 2;
    let hy = (sin * l.width * aspect + cos * l.height) / 2;
    const fit = Math.min(1, 1/l.width, 1/l.height, 1 / (2 * hx), 1 / (2 * hy));
    l.width *= fit; l.height *= fit; hx *= fit; hy *= fit;
    cx = Math.max(hx, Math.min(1-hx, cx)); cy = Math.max(hy, Math.min(1-hy, cy));
    l.x = Math.max(0, cx - l.width / 2); l.y = Math.max(0, cy - l.height / 2);
    return l;
}
export function resizeAroundCenter(l, factor, aspect = .8) {
    const cx = l.x + l.width/2, cy = l.y + l.height/2;
    factor = Math.max(.02/l.width, .02/l.height, factor);
    l.width *= factor; l.height *= factor; l.x = cx-l.width/2; l.y = cy-l.height/2;
    return constrainLayer(l, aspect);
}
const instances = new WeakMap();
export function init(root, catalog, dotnet) {
    const controller = new AbortController(), signal = controller.signal;
    const q = s => root.querySelector(s), all = s => [...root.querySelectorAll(s)];
    let garment = 'tshirt', colour = 'white', side = 'front', assets = [], layers = [], selected = null, busy = false, submitted = false;
    const key = crypto.randomUUID();
    const zone = q('.studio-print-zone'), stage = q('.studio-stage'), feedback = q('.studio-feedback');
    const colours = () => catalog.find(g => g.id === garment).colours;
    const current = () => layers.find(l => l.id === selected);
    const zoneRatio = () => zone.clientWidth / zone.clientHeight;
    function notice(text, success = false) { feedback.hidden = false; feedback.classList.toggle('is-success', success); feedback.textContent = text; }
    function clamp(l) { constrainLayer(l, zoneRatio()); }
    function setWidth(l, width) {
        const asset = assets.find(a => a.id === l.asset);
        const cx = l.x + l.width / 2, cy = l.y + l.height / 2;
        const ratio = zoneRatio() / asset.ratio;
        l.width = Math.max(.02, Math.min(width, 1, 1 / ratio)); l.height = l.width * ratio;
        if (l.height < .02) { l.height = .02; l.width = l.height / ratio; }
        l.x = cx - l.width/2; l.y = cy - l.height/2; clamp(l);
    }
    function render() {
        const g = catalog.find(g => g.id === garment), c = colours().find(c => c.id === colour);
        const image = q('.studio-garment');
        const url = `/img/custom-studio/${garment}-${colour}-${side}.webp`;
        if (image.getAttribute('src') !== url) image.src = url;
        image.alt = `نمای ${side === 'front' ? 'جلو' : 'پشت'} ${g.name} ${c.name}`;
        // Use the full photograph so sleeves and the hem are reachable as well as the chest.
        all('[data-garment]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.garment === garment)));
        all('[data-side]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.side === side)));
        for (const s of ['front', 'back']) { const count = layers.filter(l => l.side === s).length; q(`[data-count-${s}]`).textContent = count ? `(${count})` : ''; }
        const visible = layers.filter(l => l.side === side);
        for (const node of [...zone.children]) if (!visible.some(l => l.id === node.dataset.layer)) node.remove();
        for (const l of visible) {
            const asset = assets.find(a => a.id === l.asset);
            let wrapper = [...zone.children].find(n => n.dataset.layer === l.id);
            if (!wrapper) {
                wrapper = document.createElement('div'); wrapper.dataset.layer = l.id;
                const move = document.createElement('button'); move.type = 'button'; move.className = 'studio-artwork-move'; move.setAttribute('aria-label', `انتخاب و جابه‌جایی طرح ${asset.name}`);
                const img = document.createElement('img'); img.src = asset.url; img.alt = ''; img.draggable = false; move.append(img); wrapper.append(move);
                for (const corner of ['nw','ne','sw','se']) {
                    const handle = document.createElement('button'); handle.type = 'button'; handle.className = `studio-transform-handle studio-resize-${corner}`;
                    handle.dataset.transform = 'resize'; handle.setAttribute('aria-label', 'تغییر اندازه طرح؛ گوشه ' + ({nw:'بالا چپ',ne:'بالا راست',sw:'پایین چپ',se:'پایین راست'}[corner])); handle.title = 'برای تغییر اندازه بکشید'; wrapper.append(handle);
                }
                const rotate = document.createElement('button'); rotate.type = 'button'; rotate.className = 'studio-transform-handle studio-rotate-handle'; rotate.dataset.transform = 'rotate';
                rotate.textContent = '↻'; rotate.setAttribute('aria-label', 'چرخاندن طرح با کشیدن دستگیره'); rotate.title = 'برای چرخاندن بکشید'; wrapper.append(rotate); zone.append(wrapper);
            }
            wrapper.className = 'studio-artwork' + (selected === l.id ? ' is-selected' : '');
            wrapper.style.zIndex = selected === l.id ? '20' : String(layers.indexOf(l)+1);
            wrapper.querySelector('.studio-artwork-move').setAttribute('aria-pressed', String(selected === l.id));
            Object.assign(wrapper.style, { left: `${l.x*100}%`, top: `${l.y*100}%`, width: `${l.width*100}%`, height: `${l.height*100}%`, transform: `rotate(${l.rotation}deg)` });
        }
        const list = q('.studio-layers'); list.replaceChildren();
        if (!layers.length) { const empty = document.createElement('p'); empty.className = 'studio-empty'; empty.textContent = 'هنوز طرحی اضافه نکردی. بوم منتظر ایده توست.'; list.append(empty); }
        layers.forEach((l, i) => {
            const asset = assets.find(a => a.id === l.asset), b = document.createElement('button'); b.type = 'button'; b.dataset.choose = l.id; b.className = 'studio-layer-row' + (selected === l.id ? ' is-selected' : '');
            const thumb = document.createElement('img'); thumb.src = asset.url; thumb.alt = '';
            const label = document.createElement('span'); label.textContent = `${i+1}. ${asset.name}`;
            const badge = document.createElement('small'); badge.textContent = l.side === 'front' ? 'جلو' : 'پشت'; b.append(thumb, label, badge); list.append(b);
        });
        const l = current(); q('.studio-layer-controls').hidden = !l; q('.studio-quick-tools').hidden = !l;
        if (l) {
            q('[data-rotation]').value = Math.round(l.rotation); q('[data-angle]').textContent = `${Math.round(l.rotation)}°`;
            q('[data-scale]').value = Math.round(l.width * 100); q('[data-x]').value = Math.round(l.x * 100); q('[data-y]').value = Math.round(l.y * 100);
            q('[data-x]').max = Math.floor((1-l.width)*100); q('[data-y]').max = Math.floor((1-l.height)*100);
        }
    }
    function renderColours() {
        const parent = q('.studio-colours'); parent.replaceChildren();
        colours().forEach(c => { const b = document.createElement('button'); b.type = 'button'; b.dataset.colour = c.id; b.setAttribute('aria-label', c.name); b.setAttribute('aria-pressed', String(c.id === colour)); b.title = c.name;
            const swatch = document.createElement('span'); swatch.style.background = c.hex; const label = document.createElement('small'); label.textContent = c.name; b.append(swatch, label); parent.append(b); });
    }
    root.addEventListener('click', async event => {
        const b = event.target.closest('button'); if (!b || busy || submitted) return;
        if (b.dataset.garment) { garment = b.dataset.garment; if (!colours().some(c => c.id === colour)) colour = 'white'; renderColours(); render(); layers.forEach(l => setWidth(l, l.width)); await dotnet.invokeMethodAsync('GarmentChanged', garment); }
        if (b.dataset.colour) { colour = b.dataset.colour; renderColours(); }
        if (b.dataset.side) { side = b.dataset.side; selected = null; }
        if (b.dataset.choose) { selected = b.dataset.choose; side = current().side; }
        if (b.closest('[data-layer]')) selected = b.closest('[data-layer]').dataset.layer;
        const l = current();
        if (l && b.hasAttribute('data-remove')) { layers = layers.filter(x => x.id !== l.id); selected = null; const used = new Set(layers.map(x => x.asset)); assets.filter(a => !used.has(a.id)).forEach(a => URL.revokeObjectURL(a.url)); assets = assets.filter(a => used.has(a.id)); }
        if (l && b.hasAttribute('data-smaller')) resizeAroundCenter(l, .9, zoneRatio());
        if (l && b.hasAttribute('data-larger')) resizeAroundCenter(l, 1.1, zoneRatio());
        if (l && b.hasAttribute('data-turn-left')) { l.rotation -= 15; clamp(l); }
        if (l && b.hasAttribute('data-turn-right')) { l.rotation += 15; clamp(l); }
        if (l && b.hasAttribute('data-center')) { l.x = (1-l.width)/2; l.y = (1-l.height)/2; }
        if (l && b.hasAttribute('data-other-side')) { l.side = l.side === 'front' ? 'back' : 'front'; side = l.side; render(); setWidth(l, l.width); }
        if (l && b.hasAttribute('data-duplicate')) { if (layers.length >= 12) notice('حداکثر ۱۲ طرح قابل چیدمان است.'); else { const copy = {...l, id: crypto.randomUUID(), x: l.x+.03, y:l.y+.03}; clamp(copy); layers.push(copy); selected = copy.id; } }
        render();
    }, {signal});
    root.addEventListener('input', event => {
        const l = current(), target = event.target; if (!l || busy || submitted) return;
        if (target.hasAttribute('data-scale')) setWidth(l, Number(target.value)/100);
        else if (target.hasAttribute('data-rotation')) { l.rotation = Number(target.value); clamp(l); }
        else if (target.hasAttribute('data-x')) { l.x = Number(target.value)/100; clamp(l); }
        else if (target.hasAttribute('data-y')) { l.y = Number(target.value)/100; clamp(l); }
        else return; render();
    }, {signal});
    const files = q('[data-files]');
    files?.addEventListener('change', async () => {
        if (busy || submitted) return;
        busy = true; q('[data-submit]').disabled = true;
        try {
            for (const file of [...files.files]) {
                if (assets.length >= 8 || layers.length >= 12) throw Error('حداکثر ۸ فایل و ۱۲ طرح می‌توان اضافه کرد.');
                if (file.type !== 'image/png' || !file.name.toLowerCase().endsWith('.png')) throw Error('لطفاً فایل PNG انتخاب کن.');
                if (file.size > 5*1024*1024 || file.size + assets.reduce((s,a) => s+a.file.size,0) > 24*1024*1024) throw Error('هر فایل تا ۵ مگابایت و مجموع فایل‌ها تا ۲۴ مگابایت مجاز است.');
                const url = URL.createObjectURL(file), img = new Image(); img.src = url;
                try { await img.decode(); } catch { URL.revokeObjectURL(url); throw Error('تصویر قابل خواندن نیست؛ یک PNG سالم انتخاب کن.'); }
                if (!img.naturalWidth || img.naturalWidth > 12000 || img.naturalHeight > 12000 || img.naturalWidth*img.naturalHeight > 40000000) { URL.revokeObjectURL(url); throw Error('ابعاد تصویر بیش از حد مجاز است؛ حداکثر ۴۰ مگاپیکسل.'); }
                const asset = {id:crypto.randomUUID(), file, name:file.name, url, ratio:img.naturalWidth/img.naturalHeight}; assets.push(asset);
                const l = {id:crypto.randomUUID(), asset:asset.id, side, x:.25, y:.2, width:.25, height:.25, rotation:0}; setWidth(l,.25); l.x=(1-l.width)/2; l.y=(1-l.height)/2; layers.push(l); selected=l.id;
            }
            feedback.hidden = true;
        } catch(error) { notice(error.message); }
        finally { files.value = ''; busy = false; q('[data-submit]').disabled=false; render(); }
    }, {signal});
    let drag = null;
    zone.addEventListener('pointerdown', e => {
        if (busy || submitted || (drag && e.pointerId !== drag.id)) return;
        const wrapper = e.target.closest('[data-layer]'); if (!wrapper) return;
        selected = wrapper.dataset.layer; const l = current(), rect = zone.getBoundingClientRect();
        const cx = rect.left+(l.x+l.width/2)*rect.width, cy = rect.top+(l.y+l.height/2)*rect.height;
        drag = {id:e.pointerId, x:e.clientX, y:e.clientY, layer:{...l}, cx, cy,
            angle:Math.atan2(e.clientY-cy,e.clientX-cx), distance:Math.max(1,Math.hypot(e.clientX-cx,e.clientY-cy)),
            mode:e.target.closest('[data-transform]')?.dataset.transform || 'move'};
        zone.setPointerCapture(e.pointerId); e.preventDefault(); render();
    }, {signal});
    zone.addEventListener('pointermove', e => {
        if (!drag || e.pointerId !== drag.id) return;
        const l = current(), rect = zone.getBoundingClientRect(); if (!l) return;
        Object.assign(l, drag.layer);
        if (drag.mode === 'rotate') {
            l.rotation = drag.layer.rotation + (Math.atan2(e.clientY-drag.cy,e.clientX-drag.cx)-drag.angle)*180/Math.PI; clamp(l);
        } else if (drag.mode === 'resize') {
            resizeAroundCenter(l, Math.hypot(e.clientX-drag.cx,e.clientY-drag.cy)/drag.distance, zoneRatio());
        } else { l.x += (e.clientX-drag.x)/rect.width; l.y += (e.clientY-drag.y)/rect.height; clamp(l); }
        render();
    }, {signal});
    for (const event of ['pointerup','pointercancel','lostpointercapture']) zone.addEventListener(event, () => drag=null, {signal});
    zone.addEventListener('keydown', e => {
        const l = current(); if (!l || busy || submitted) return;
        if (e.target.closest('[data-transform]')) {
            if (!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)) return; e.preventDefault();
            const positive = e.key === 'ArrowRight' || e.key === 'ArrowUp';
            if (e.target.dataset.transform === 'rotate') { l.rotation += positive ? 5 : -5; clamp(l); }
            else resizeAroundCenter(l, positive ? 1.05 : .95, zoneRatio()); render(); return;
        }
        const moves = {ArrowLeft:[-.01,0],ArrowRight:[.01,0],ArrowUp:[0,-.01],ArrowDown:[0,.01]};
        if (moves[e.key]) { e.preventDefault(); l.x+=moves[e.key][0]; l.y+=moves[e.key][1]; clamp(l); render(); }
    }, {signal});
    q('form').addEventListener('submit',async event=>{
        event.preventDefault();if(busy || submitted || root.dataset.auth!=='yes')return;
        if(!layers.length){notice('اول یک طرح PNG روی لباس قرار بده.');feedback.focus();return;}
        busy=true;root.classList.add('is-busy');const button=q('[data-submit]');button.disabled=true;button.textContent='در حال ارسال طرح‌ها…';
        const form = new FormData(q('form'));form.set('key',key);form.set('garment',garment);form.set('colour',colour);form.set('size',q('[data-size]').value);form.set('quantity',q('[data-quantity]').value);
        form.set('layers',JSON.stringify(layers.map(l=>({file:assets.findIndex(a=>a.id===l.asset),side:l.side,x:l.x,y:l.y,width:l.width,height:l.height,rotation:l.rotation,canvasVersion:2}))));
        assets.forEach((a,i)=>form.append('files',a.file,`design-${i}.png`));
        try {
            const r=await fetch('/api/custom-designs',{method:'POST',body:form,headers:{'X-CSRF-TOKEN':form.get('__RequestVerificationToken')},signal,redirect:'error'});
            if(r.status===401)throw Error('نشست شما منقضی شده؛ دوباره وارد حساب شوید.');
            const result=await r.json().catch(()=>({}));if(!r.ok)throw Error(result.error || (r.status===429?'کمی صبر کن و دوباره تلاش کن.':'ارسال انجام نشد؛ اتصال و حجم فایل‌ها را بررسی کن.'));
            submitted=true;notice(`درخواست ${result.number} ثبت شد. پاپی برای بررسی قیمت و جزئیات با شما تماس می‌گیرد.`,true);
            const link=document.createElement('a');link.href=result.url;link.textContent='مشاهده درخواست‌های من ←';feedback.append(document.createElement('br'),link);button.textContent='درخواست ثبت شد ✓';q('form').querySelectorAll('input,textarea').forEach(el=>el.disabled=true);feedback.focus();feedback.scrollIntoView({behavior:'smooth',block:'center'});
        } catch(error){if(error.name!=='AbortError'){notice(error.message);feedback.focus();button.textContent='تلاش دوباره برای ثبت درخواست ←';button.disabled=false;}}
        finally{busy=false;root.classList.remove('is-busy');}
    },{signal});
    renderColours();render();
    instances.set(root,()=>{controller.abort();assets.forEach(a=>URL.revokeObjectURL(a.url));});
}
export function dispose(root){instances.get(root)?.();instances.delete(root);}
