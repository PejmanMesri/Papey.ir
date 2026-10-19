const states = new WeakMap();
export function open(dialog) {
    if (dialog.open) return;
    const state = { overflow: document.body.style.overflow, focus: document.activeElement };
    state.restore = () => {
        document.body.style.overflow = state.overflow;
        dialog.removeEventListener('close', state.restore);
        dialog.removeEventListener('click', state.backdrop);
        states.delete(dialog);
        if (state.focus?.isConnected) state.focus.focus();
    };
    state.backdrop = event => {
        if (event.target !== dialog) return;
        const r = dialog.getBoundingClientRect();
        if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) dialog.close();
    };
    states.set(dialog, state);
    dialog.addEventListener('close', state.restore);
    dialog.addEventListener('click', state.backdrop);
    dialog.showModal();
    document.body.style.overflow = 'hidden';
}
export function close(dialog) { if (dialog.open) dialog.close(); }
export function dispose(dialog) {
    const state = states.get(dialog);
    if (dialog.open) dialog.close();
    if (state && states.has(dialog)) state.restore();
}
