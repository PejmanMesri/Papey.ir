import * as modal from './ProductSizeGuide.razor.js';
const states = new WeakMap();
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
function paint(s) {
    const image = s.stage.querySelector('img');
    if (!image) return;
    const maxX = Math.max(0, (image.clientWidth * s.scale - s.stage.clientWidth) / 2);
    const maxY = Math.max(0, (image.clientHeight * s.scale - s.stage.clientHeight) / 2);
    s.x = clamp(s.x, -maxX, maxX); s.y = clamp(s.y, -maxY, maxY);
    image.style.transform = `translate(${s.x}px,${s.y}px) scale(${s.scale})`;
    s.stage.classList.toggle('is-zoomed', s.scale > 1);
    s.dialog.querySelector('[data-zoom-value]').textContent = Math.round(s.scale * 100) + '%';
}
function zoom(s, scale, point) {
    const next = clamp(scale, 1, 5), ratio = next / s.scale;
    if (point) { const r = s.stage.getBoundingClientRect(); const x = point.clientX - r.left - r.width / 2, y = point.clientY - r.top - r.height / 2; s.x = x - (x - s.x) * ratio; s.y = y - (y - s.y) * ratio; }
    s.scale = next; if (next === 1) s.x = s.y = 0; paint(s);
}
export function reset(dialog) { const s = states.get(dialog); if (s) { s.scale = 1; s.x = s.y = 0; s.points.clear(); paint(s); } }
export function open(dialog, dotnet) {
    if (states.has(dialog)) { reset(dialog); modal.open(dialog); return; }
    const s = { dialog, stage: dialog.querySelector('[data-viewer-stage]'), scale: 1, x: 0, y: 0, points: new Map(), listeners: [] };
    function on(node, event, handler, options) { node.addEventListener(event, handler, options); s.listeners.push(() => node.removeEventListener(event, handler, options)); }
    function gesture() { const p = [...s.points.values()]; if (p.length >= 2) { s.distance = Math.hypot(p[1].x - p[0].x, p[1].y - p[0].y); s.gestureScale = s.scale; s.center = { x: (p[0].x + p[1].x) / 2, y: (p[0].y + p[1].y) / 2 }; s.baseX = s.x; s.baseY = s.y; } }
    on(s.stage, 'pointerdown', e => { if (e.button !== 0 && e.pointerType !== 'touch') return; s.points.set(e.pointerId, { x: e.clientX, y: e.clientY }); s.stage.setPointerCapture(e.pointerId); s.stage.classList.add('is-dragging'); gesture(); });
    on(s.stage, 'pointermove', e => { const last = s.points.get(e.pointerId); if (!last) return; s.points.set(e.pointerId, { x: e.clientX, y: e.clientY }); const p = [...s.points.values()]; if (p.length >= 2 && s.distance > 0) { s.scale = clamp(s.gestureScale * Math.hypot(p[1].x - p[0].x, p[1].y - p[0].y) / s.distance, 1, 5); s.x = s.baseX + (p[0].x + p[1].x) / 2 - s.center.x; s.y = s.baseY + (p[0].y + p[1].y) / 2 - s.center.y; } else if (s.scale > 1) { s.x += e.clientX - last.x; s.y += e.clientY - last.y; } paint(s); });
    function end(e) { s.points.delete(e.pointerId); if (s.points.size < 2) s.distance = 0; if (s.points.size === 0) s.stage.classList.remove('is-dragging'); }
    on(s.stage, 'pointerup', end); on(s.stage, 'pointercancel', end);
    on(s.stage, 'wheel', e => { e.preventDefault(); zoom(s, s.scale * (e.deltaY < 0 ? 1.15 : 1 / 1.15), e); }, { passive: false });
    on(s.stage, 'dblclick', e => { zoom(s, s.scale > 1 ? 1 : 2.5, e); });
    on(dialog, 'click', e => { const button = e.target.closest('[data-zoom]'); if (!button) return; const mode = button.dataset.zoom; zoom(s, mode === 'reset' ? 1 : s.scale * (mode === 'in' ? 1.35 : 1 / 1.35)); });
    on(dialog, 'keydown', e => { if (e.key === '+' || e.key === '=') { e.preventDefault(); zoom(s, s.scale * 1.35); } else if (e.key === '-') { e.preventDefault(); zoom(s, s.scale / 1.35); } else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') { e.preventDefault(); dotnet.invokeMethodAsync('Step', e.key === 'ArrowLeft' ? 1 : -1); } });
    const cleanup = () => { s.listeners.forEach(remove => remove()); states.delete(dialog); };
    on(dialog, 'close', cleanup);
    states.set(dialog, s); modal.open(dialog); paint(s);
}
export function close(dialog) { modal.close(dialog); }
export function dispose(dialog) { modal.dispose(dialog); const s = states.get(dialog); if (s) { s.listeners.forEach(remove => remove()); states.delete(dialog); } }
