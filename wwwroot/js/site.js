// Fallback interactivity layer for Papey.
// If the Blazor Server circuit is not available (e.g. WebSocket blocked), plain HTML
// data-* behaviors still work: color/size selection, quantity steppers, and cart removal.
(function () {
    'use strict';

    function fa(n) { return String(n).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]); }

    // ---- Delegated clicks ----
    document.addEventListener('click', function (e) {
        var dot = e.target.closest('.color-dot[data-color]');
        if (dot) {
            var wrap = dot.closest('.product-detail');
            wrap.querySelectorAll('.color-dot').forEach(function (d) { d.classList.remove('selected'); });
            dot.classList.add('selected');
            var label = wrap.querySelector('.js-color-label');
            if (label) label.textContent = dot.getAttribute('data-color');
            wrap.querySelectorAll('.size-chip').forEach(function (chip) {
                var key = dot.getAttribute('data-color') + '|' + chip.getAttribute('data-size');
                var stock = window.__papeyStock ? (window.__papeyStock[key] || 0) : 0;
                chip.classList.toggle('out', stock <= 0);
            });
            return;
        }

        var chip = e.target.closest('.size-chip:not(.out)');
        if (chip) {
            chip.closest('.product-detail, body').querySelectorAll('.size-chip').forEach(function (c) { c.classList.remove('selected'); });
            chip.classList.add('selected');
            var sizeInput = document.querySelector('.js-size-input');
            if (sizeInput) sizeInput.value = chip.getAttribute('data-size');
            return;
        }

        var qtyBtn = e.target.closest('.qty-box button[data-dir]');
        if (qtyBtn) {
            var box = qtyBtn.closest('.qty-box');
            var span = box.querySelector('span');
            var current = parseInt(span.getAttribute('data-qty') || '1', 10);
            var max = parseInt(box.getAttribute('data-max') || '99', 10);
            var next = qtyBtn.getAttribute('data-dir') === '+' ? Math.min(current + 1, max) : Math.max(current - 1, 1);
            span.setAttribute('data-qty', next);
            span.textContent = fa(next);
            var qtyInput = document.querySelector('.js-qty-input');
            if (qtyInput) qtyInput.value = next;
            return;
        }
    });
})();
