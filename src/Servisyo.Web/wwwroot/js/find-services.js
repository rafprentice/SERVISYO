/* Customer > Find Services
   - changing the sort list searches straight away (no extra click);
   - empty / default boxes are left out of the address, so it stays short: /customer/find-services?q=plumb
   The page works without this file too: the Enter key (or the hidden Search button) still submits the form. */
(function () {
    'use strict';

    var form = document.querySelector('[data-fs-form]');
    if (!form) return;

    var q = form.querySelector('[data-fs-q]');
    var sort = form.querySelector('[data-fs-sort]');
    var busy = false;

    // Boxes that are empty or still at their default value should not appear in the address at all.
    function tidy() {
        if (q) {
            q.value = q.value.replace(/\s+/g, ' ').trim();
            q.disabled = q.value === '';
        }
        if (sort) sort.disabled = sort.value === 'rating';
    }

    // The browser may restore this page from its cache (Back button) with the boxes still switched off.
    function restore() {
        busy = false;
        if (q) q.disabled = false;
        if (sort) sort.disabled = false;
    }

    form.addEventListener('submit', function (event) {
        if (busy) { event.preventDefault(); return; }   // a double click / double Enter sends only one request
        busy = true;
        tidy();
    });

    if (sort) {
        sort.addEventListener('change', function () {
            if (busy) return;
            if (typeof form.requestSubmit === 'function') form.requestSubmit();
            else { busy = true; tidy(); form.submit(); }
        });
    }

    window.addEventListener('pageshow', restore);
})();
