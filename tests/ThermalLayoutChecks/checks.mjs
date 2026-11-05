import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const handlers = {};
let height = 400, exists = true, style, printed = 0;
const document = {
    fonts: { ready: Promise.resolve() },
    querySelector: () => exists ? { getBoundingClientRect: () => ({ height }) } : null,
    getElementById: () => style,
    createElement: () => ({ remove() { style = undefined; } }),
    body: { appendChild(value) { style = value; } }
};
const window = { addEventListener(name, handler) { handlers[name] = handler; }, print() { printed++; } };
vm.runInNewContext(readFileSync(new URL('../../wwwroot/js/thermal-print.js', import.meta.url), 'utf8'), { window, document, Number, Math });
await window.papey.printThermalReceipt();
assert.equal(printed, 1);
assert.match(style.textContent, /size: 80mm 110mm; margin: 0/);
handlers.afterprint(); assert.equal(style, undefined);
height = 800; handlers.beforeprint(); assert.match(style.textContent, /size: 80mm 216mm/);
handlers.afterprint(); exists = false; handlers.beforeprint(); assert.equal(style, undefined);
exists = true; height = NaN; handlers.beforeprint(); assert.equal(style, undefined);
console.log('PASS: Receipt height determines 80mm paper dimensions; longer orders retain their content; print cleanup, explicit A4 pages and invalid measurements are safe. No physical printing.');
