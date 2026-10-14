// SERVISYO site script: mobile menu + highlight of the current homepage section.
(function () {
    'use strict';

    // ----- Mobile menu -----
    var toggle = document.querySelector('.sv-nav-toggle');
    var menu = document.getElementById('sv-menu');
    if (toggle && menu) {
        toggle.addEventListener('click', function () {
            var open = menu.classList.toggle('is-open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
        menu.addEventListener('click', function (e) {
            if (e.target.closest('a')) {
                menu.classList.remove('is-open');
                toggle.setAttribute('aria-expanded', 'false');
            }
        });
    }

    // ----- Highlight nav link for the section currently on screen (homepage only) -----
    var links = document.querySelectorAll('.sv-nav a[data-spy]');
    if (!links.length || !('IntersectionObserver' in window)) return;

    var byId = {};
    links.forEach(function (a) { byId[a.getAttribute('data-spy')] = a; });

    var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (!entry.isIntersecting) return;
            links.forEach(function (a) { a.classList.remove('is-active'); });
            var link = byId[entry.target.id];
            if (link) link.classList.add('is-active');
        });
    }, { rootMargin: '-30% 0px -60% 0px' });

    Object.keys(byId).forEach(function (id) {
        var section = document.getElementById(id);
        if (section) observer.observe(section);
    });
})();
