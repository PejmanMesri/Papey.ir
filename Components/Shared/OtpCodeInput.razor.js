const states = new WeakMap();
export function digits(value) {
    return String(value ?? '').replace(/[۰-۹]/g, c => String(c.charCodeAt(0) - 1776)).replace(/[٠-٩]/g, c => String(c.charCodeAt(0) - 1632)).replace(/[^0-9]/g, '');
}
function move(s, index) { const input = s.inputs[Math.max(0, Math.min(4, index))]; if (!input.disabled) { input.focus(); input.select(); } }
function notify(s) {
    const code = s.inputs.map(input => input.value).join('');
    const complete = s.inputs.every(input => input.value.length === 1);
    if (!complete) s.lastComplete = null;
    const submit = complete && s.lastComplete !== code;
    if (submit) { s.lastComplete = code; s.pending = true; s.inputs.forEach(input => input.readOnly = true); }
    if (code === s.lastCode && !submit) return;
    s.lastCode = code;
    s.dotnet.invokeMethodAsync('Update', code, submit).catch(() => {}).finally(() => { if (submit) { s.pending = false; s.inputs.forEach(input => input.readOnly = false); } });
}
function fill(s, index, value) {
    if (s.inputs[index].disabled) return;
    if (s.pending) { s.inputs.forEach((input, i) => input.value = s.lastCode[i] ?? ''); return; }
    const text = digits(value);
    if (!text) { s.inputs[index].value = ''; notify(s); return; }
    const start = text.length >= 5 ? 0 : index;
    const chunk = text.slice(0, 5 - start);
    for (let i = 0; i < chunk.length; i++) s.inputs[start + i].value = chunk[i];
    move(s, Math.min(4, start + chunk.length));
    notify(s);
}
export function set(root, value) {
    const s = states.get(root); if (!s) return;
    const code = digits(value).slice(0, 5);
    s.inputs.forEach((input, i) => input.value = code[i] ?? '');
    s.lastCode = code; s.lastComplete = code.length === 5 ? code : null;
    if (!code) move(s, 0);
}
export function focus(root) { const s = states.get(root); if (s) move(s, 0); }
export function bind(root, dotnet, value = '') {
    dispose(root);
    const s = { inputs: [...root.querySelectorAll('input')], dotnet, listeners: [], lastCode: '', lastComplete: null, pending: false };
    function on(input, event, handler) { input.addEventListener(event, handler); s.listeners.push(() => input.removeEventListener(event, handler)); }
    s.inputs.forEach((input, index) => {
        on(input, 'input', e => { if (!e.isComposing) fill(s, index, input.value); });
        on(input, 'compositionend', () => fill(s, index, input.value));
        on(input, 'focus', () => input.select());
        on(input, 'paste', e => { const text = e.clipboardData?.getData('text'); if (text !== undefined) { e.preventDefault(); fill(s, index, text); } });
        on(input, 'keydown', e => {
            if (input.disabled) return;
            if (e.key === 'Backspace' && !input.value && index > 0) { e.preventDefault(); move(s, index - 1); }
            else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') { e.preventDefault(); move(s, index + (e.key === 'ArrowLeft' ? -1 : 1)); }
            else if (e.key === 'Home' || e.key === 'End') { e.preventDefault(); move(s, e.key === 'Home' ? 0 : 4); }
        });
    });
    states.set(root, s); set(root, value); move(s, Math.max(0, s.inputs.findIndex(input => !input.value)));
}
export function dispose(root) { const s = states.get(root); if (s) { s.inputs.forEach(input => input.readOnly = false); s.listeners.forEach(remove => remove()); states.delete(root); } }
