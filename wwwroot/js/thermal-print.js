(function () {
    'use strict';
    var styleId = 'papey-thermal-paper-size';
    var customPaper = false;
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
        // The default respects the paper form supported by the installed driver.
        // A narrow RTL body on a wider print canvas can otherwise sit to the
        // right of an 80mm roll: anchor the print canvas on the physical left.
        var pageSize = customPaper ? '80mm ' + millimetres + 'mm' : 'auto';
        style.textContent = '@page { size: ' + pageSize + '; margin: 0; }' +
            '@media print {' +
            'html,body{direction:ltr!important;width:80mm!important;min-width:0!important;max-width:80mm!important;height:auto!important;min-height:0!important;margin:0!important;padding:0!important;background:white!important;overflow:visible!important;}' +
            '.receipt,.thermal-receipt{display:block!important;position:static!important;float:none!important;transform:none!important;direction:rtl!important;color:black!important;background:white!important;visibility:visible!important;opacity:1!important;width:72mm!important;max-width:72mm!important;min-width:0!important;height:auto!important;margin:0 0 0 4mm!important;padding:1mm!important;box-sizing:border-box!important;overflow:visible!important;}' +
            '.no-print,#blazor-error-ui,dialog{display:none!important;}' +
            '}';
        return true;
    }
    function clear() { document.getElementById(styleId)?.remove(); customPaper = false; }
    window.addEventListener('beforeprint', prepare);
    window.addEventListener('afterprint', clear);
    window.papey = window.papey || {};
    window.papey.printThermalReceipt = async function (useContentHeight) {
        customPaper = useContentHeight === true;
        if (document.fonts) await document.fonts.ready;
        if (!prepare()) return false;
        window.print();
        return true;
    };
})();
