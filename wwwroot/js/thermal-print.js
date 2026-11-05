(function () {
    'use strict';
    var styleId = 'papey-thermal-paper-size';
    function prepare() {
        var receipt = document.querySelector('.thermal-receipt, .receipt[data-print-ready="true"]');
        if (!receipt) return false;
        // CSS pixels are 1/96 inch. Measure the complete receipt, then allow
        // 4 mm for rounding and print-only padding; never impose an A4 height.
        var height = receipt.getBoundingClientRect().height;
        if (!Number.isFinite(height) || height <= 0) return false;
        var millimetres = Math.ceil(height * 25.4 / 96 + 4);
        var style = document.getElementById(styleId);
        if (!style) { style = document.createElement('style'); style.id = styleId; document.body.appendChild(style); }
        style.textContent = '@page { size: 80mm ' + millimetres + 'mm; margin: 0; }' +
            '@media print { body { width:80mm; margin:0!important; } }';
        return true;
    }
    function clear() { document.getElementById(styleId)?.remove(); }
    window.addEventListener('beforeprint', prepare);
    window.addEventListener('afterprint', clear);
    window.papey = window.papey || {};
    window.papey.printThermalReceipt = async function () {
        if (document.fonts) await document.fonts.ready;
        prepare();
        window.print();
    };
})();
