// Confirm delete dialogs
document.querySelectorAll('[data-confirm]').forEach(el => {
    el.addEventListener('click', function (e) {
        if (!confirm(this.dataset.confirm)) e.preventDefault();
    });
});

// Auto-format amount inputs
document.querySelectorAll('input[data-amount]').forEach(el => {
    el.addEventListener('blur', function () {
        const v = parseFloat(this.value.replace(/,/g, ''));
        if (!isNaN(v)) this.value = v.toFixed(2);
    });
});

// Number formatting helper
function fmt(n) {
    return new Intl.NumberFormat('en-KE', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n || 0);
}
