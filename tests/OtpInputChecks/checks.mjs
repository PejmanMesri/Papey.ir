import assert from 'node:assert/strict';
import { bind, set, dispose, digits } from '../../Components/Shared/OtpCodeInput.razor.js';
class Input {
    value = ''; disabled = false; events = new Map();
    addEventListener(name, fn) { if (!this.events.has(name)) this.events.set(name, new Set()); this.events.get(name).add(fn); }
    removeEventListener(name, fn) { this.events.get(name)?.delete(fn); }
    focus() { focused = inputs.indexOf(this); }
    select() {}
    emit(name, event = {}) { for (const fn of this.events.get(name) ?? []) fn({ target: this, preventDefault() {}, ...event }); }
}
let focused = -1;
const inputs = Array.from({ length: 5 }, () => new Input());
const root = { querySelectorAll: () => inputs };
const updates = [];
const dotnet = { invokeMethodAsync(name, code, complete) { assert.equal(name, 'Update'); updates.push({ code, complete }); return Promise.resolve(); } };
bind(root, dotnet); assert.equal(focused, 0);
for (let i = 0; i < 5; i++) { inputs[i].value = String(i + 1); inputs[i].emit('input'); assert.equal(focused, Math.min(i + 1, 4)); }
assert.deepEqual(updates.at(-1), { code: '12345', complete: true });
assert.equal(updates.filter(u => u.complete).length, 1);
inputs[4].emit('input'); inputs[4].emit('compositionend');
assert.equal(updates.filter(u => u.complete).length, 1, 'Repeated input and composition events cannot auto-submit twice');
assert.ok(inputs.every(i => i.readOnly), 'Completed code stays read-only while verification is pending');
inputs[0].emit('paste', { clipboardData: { getData: () => '99999' } });
assert.equal(inputs.map(i => i.value).join(''), '12345');
assert.equal(updates.filter(u => u.complete).length, 1);
await new Promise(setImmediate);
assert.ok(inputs.every(i => !i.readOnly));
set(root, ''); inputs[0].emit('paste', { clipboardData: { getData: () => '۰۱۲۳۴' } });
assert.deepEqual(inputs.map(i => i.value), ['0', '1', '2', '3', '4']);
assert.deepEqual(updates.at(-1), { code: '01234', complete: true });
await new Promise(setImmediate);
set(root, ''); inputs[3].emit('paste', { clipboardData: { getData: () => 'كود: ١٢٣٤٥' } });
assert.equal(inputs.map(i => i.value).join(''), '12345', 'A complete pasted code fills from the first box regardless of focus');
await new Promise(setImmediate);
set(root, ''); inputs[0].value = '98765'; inputs[0].emit('input');
assert.deepEqual(updates.at(-1), { code: '98765', complete: true }, 'SMS autofill is distributed and submitted');
await new Promise(setImmediate);
set(root, '12'); inputs[2].emit('keydown', { key: 'Backspace' }); assert.equal(focused, 1);
inputs[1].value = ''; inputs[1].emit('input'); assert.equal(updates.at(-1).complete, false);
inputs[1].emit('keydown', { key: 'ArrowLeft' }); assert.equal(focused, 0);
inputs[0].emit('keydown', { key: 'ArrowRight' }); assert.equal(focused, 1);
inputs[0].emit('keydown', { key: 'End' }); assert.equal(focused, 4);
set(root, ''); inputs[0].value = 'abc'; inputs[0].emit('input'); assert.equal(inputs[0].value, '');
assert.equal(digits('۰١2۳٤'), '01234');
const count = updates.length; inputs[0].disabled = true; inputs[0].emit('paste', { clipboardData: { getData: () => '12345' } }); assert.equal(updates.length, count);
inputs[0].disabled = false;
set(root, ''); for (const i of inputs) assert.equal(i.value, '', 'A failed verification can clear every digit before retry');
dispose(root); assert.equal(inputs.reduce((n, i) => n + [...i.events.values()].reduce((n, e) => n + e.size, 0), 0), 0);
bind(root, dotnet); dispose(root);
console.log('PASS: Five digits, immediate focus advancement, one auto-submit, Persian/Arabic paste, SMS autofill, leading zero, backspace/arrows, invalid characters, disabled state and reset/disposal. No SMS or account requests.');
