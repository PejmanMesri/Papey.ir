// Fallback interactivity layer for Papey.
// Only intercept clicks when no Blazor Server circuit is active.
// When Blazor is running, it handles @onclick bindings natively.
(function () {
    'use strict';

    // Keep one shared category panel open while moving through the desktop rail.
    var desktopCloseTimers = new WeakMap();
    document.addEventListener('pointerover', function (event) {
        if (event.pointerType !== 'mouse') return;
        var menu = event.target.closest('.desktop-type-menu, .account-menu');
        if (!menu) return;
        clearTimeout(desktopCloseTimers.get(menu));
        if (event.target.closest('.desktop-type-menu > summary, .account-menu > summary')) menu.open = true;
    });
    document.addEventListener('pointerout', function (event) {
        if (event.pointerType !== 'mouse') return;
        var menu = event.target.closest('.desktop-type-menu, .account-menu');
        if (!menu || (event.relatedTarget && menu.contains(event.relatedTarget))) return;
        clearTimeout(desktopCloseTimers.get(menu));
        desktopCloseTimers.set(menu, setTimeout(function () {
            if (!menu.contains(document.activeElement)) menu.open = false;
        }, 180));
    });
    function openDesktopType(group) {
        group.closest('.desktop-type-list').querySelectorAll('.desktop-type-group[open]').forEach(function (other) {
            if (other !== group) other.open = false;
        });
        group.open = true;
    }
    document.addEventListener('pointermove', function (event) {
        if (event.pointerType !== 'mouse') return;
        var summary = event.target.closest('.desktop-type-group > summary');
        if (summary && !summary.parentElement.open) openDesktopType(summary.parentElement);
    });
    document.addEventListener('keydown', function (event) {
        var group = event.target.closest('.desktop-type-group');
        if (!group) return;
        if (event.key === 'ArrowLeft' && event.target.closest('summary')) {
            event.preventDefault();
            openDesktopType(group);
            group.querySelector('.desktop-type-categories a')?.focus();
        } else if (event.key === 'ArrowRight' && event.target.closest('.desktop-type-categories')) {
            event.preventDefault();
            group.querySelector('summary').focus();
        } else if ((event.key === 'Enter' || event.key === ' ') && event.target.closest('summary')) {
            event.preventDefault();
            openDesktopType(group);
        } else if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && event.target.closest('summary')) {
            event.preventDefault();
            var rail = group.closest('.desktop-type-list');
            var summaries = Array.from(rail.querySelectorAll('.desktop-type-group > summary'));
            var currentIndex = summaries.indexOf(group.querySelector('summary'));
            var nextIndex = (currentIndex + (event.key === 'ArrowDown' ? 1 : -1) + summaries.length) % summaries.length;
            openDesktopType(summaries[nextIndex].parentElement);
            summaries[nextIndex].focus();
        }
    });

    document.addEventListener('click', function (event) {
        var typeSummary = event.target.closest('.desktop-type-group > summary');
        if (typeSummary && event.pointerType === 'mouse') {
            event.preventDefault();
            openDesktopType(typeSummary.parentElement);
        }
        var menuSummary = event.target.closest('.desktop-type-menu > summary, .account-menu > summary');
        if (menuSummary && event.pointerType === 'mouse' && !event.target.closest('a[href]')) {
            event.preventDefault();
            menuSummary.parentElement.open = true;
        }
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
    window.papey.productView = {
        read: function () { try { return localStorage.getItem('papey.product-view') === 'compact'; } catch (_) { return false; } },
        write: function (compact) { try { localStorage.setItem('papey.product-view', compact ? 'compact' : 'normal'); } catch (_) { } }
    };
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
