// Fallback interactivity layer for Papey.
// Only intercept clicks when no Blazor Server circuit is active.
// When Blazor is running, it handles @onclick bindings natively.
(function () {
    'use strict';

    document.addEventListener('click', function (event) {
        document.querySelectorAll('details[data-navigation-menu][open]').forEach(function (menu) {
            if (!menu.contains(event.target) || event.target.closest('a[href]')) menu.open = false;
        });
    });
    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') return;
        document.querySelectorAll('details[data-navigation-menu][open]').forEach(function (menu) {
            menu.open = false;
            menu.querySelector('summary').focus();
        });
    });

    window.papey = window.papey || {};
    window.papey.openOrderInvoiceOnce = function (orderId, url) {
        try {
            var key = 'papey:invoice-opened:' + orderId;
            if (window.sessionStorage.getItem(key)) return true;

            var invoiceWindow = window.open(url, '_blank');
            if (!invoiceWindow) return false;

            // The document is same-origin, but it does not need access to the payment callback.
            invoiceWindow.opener = null;
            window.sessionStorage.setItem(key, '1');
            return true;
        } catch (_) {
            return false;
        }
    };

    function fa(n) { return String(n).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]); }

    function isBlazorConnected() {
        // Blazor Server adds a "components-reconnect-modal" dialog.
        // When the circuit is live the dialog stays closed / hidden.
        // A more reliable check: Blazor sets window.Blazor after boot.
        return !!(window.Blazor);
    }

    // ---- Delegated clicks (fallback only) ----
    document.addEventListener('click', function (e) {
        // If Blazor circuit is alive, let Blazor handle all interactions
        if (isBlazorConnected()) return;

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
