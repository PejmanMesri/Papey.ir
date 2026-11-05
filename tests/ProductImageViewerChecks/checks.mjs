import assert from 'node:assert/strict';
class Target {
    events = new Map();
    addEventListener(name, fn) { if (!this.events.has(name)) this.events.set(name, new Set()); this.events.get(name).add(fn); }
    removeEventListener(name, fn) { this.events.get(name)?.delete(fn); }
    emit(name, event = {}) { for (const fn of [...(this.events.get(name) ?? [])]) fn({ target: this, preventDefault() {}, ...event }); }
}
const image = { clientWidth: 300, clientHeight: 400, style: {} };
const output = { textContent: '' };
const stage = new Target();
Object.assign(stage, { clientWidth: 600, clientHeight: 400, classList: { toggle() {}, add() {}, remove() {} }, setPointerCapture() {}, querySelector: () => image, getBoundingClientRect: () => ({ left: 0, top: 0, width: 600, height: 400 }) });
const dialog = new Target();
Object.assign(dialog, { open: false, querySelector: selector => selector.includes('stage') ? stage : output, showModal() { this.open = true; }, close() { this.open = false; this.emit('close'); } });
let focused = false;
globalThis.document = { body: { style: { overflow: 'auto' } }, activeElement: { isConnected: true, focus() { focused = true; } } };
const viewer = await import('../../Components/Shared/ProductImageLightbox.razor.js');
const steps = [];
const dotnet = { invokeMethodAsync(name, delta) { assert.equal(name, 'Step'); steps.push(delta); return Promise.resolve(); } };
viewer.open(dialog, dotnet);
assert.equal(dialog.open, true); assert.equal(document.body.style.overflow, 'hidden');
stage.emit('dblclick', { clientX: 300, clientY: 200 });
assert.equal(output.textContent, '250%');
stage.emit('pointerdown', { pointerId: 1, button: 0, clientX: 100, clientY: 100 });
stage.emit('pointermove', { pointerId: 1, clientX: 1100, clientY: 1100 });
assert.match(image.style.transform, /translate\(75px,300px\)/);
stage.emit('pointerup', { pointerId: 1 });
viewer.reset(dialog); assert.equal(output.textContent, '100%');
stage.emit('pointerdown', { pointerId: 1, button: 0, clientX: 200, clientY: 200 });
stage.emit('pointerdown', { pointerId: 2, button: 0, clientX: 400, clientY: 200 });
stage.emit('pointermove', { pointerId: 2, clientX: 600, clientY: 200 });
assert.equal(output.textContent, '200%');
dialog.emit('keydown', { key: 'ArrowLeft' }); assert.deepEqual(steps, [1]);
for (let n = 0; n < 30; n++) stage.emit('wheel', { deltaY: -1, clientX: 300, clientY: 200 });
assert.equal(output.textContent, '500%');
viewer.close(dialog); assert.equal(document.body.style.overflow, 'auto'); assert.equal(focused, true);
assert.equal([...stage.events.values()].reduce((n, listeners) => n + listeners.size, 0), 0);
viewer.open(dialog, dotnet); viewer.dispose(dialog);
assert.equal(dialog.open, false); assert.equal(document.body.style.overflow, 'auto');
console.log('PASS: Dialog opening, focus restoration, double-click zoom, bounded pan, two-finger pinch, keyboard navigation, zoom limits, closing, reopening and disposal.');
