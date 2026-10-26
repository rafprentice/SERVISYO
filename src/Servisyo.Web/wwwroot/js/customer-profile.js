/* Customer > My Profile. Without JavaScript every form is simply open and works as a normal form.
   With JavaScript the cards start "closed" (Edit / Add Address / Change Password buttons open them),
   a double-click cannot send a form twice, and removing the address asks first.
   The real checks always happen on the server. */
(function () {
    "use strict";

    var sections = document.querySelectorAll("[data-cp-section]");

    function fieldsOf(section) {
        return section.querySelectorAll("input:not([type=hidden]), textarea");
    }

    function clearProblems(section) {
        section.querySelectorAll(".sv-fld__err, .sv-formerr").forEach(function (n) { n.remove(); });
        section.querySelectorAll("[aria-invalid]").forEach(function (n) { n.removeAttribute("aria-invalid"); });
    }

    function setOpen(section, open) {
        var mode = section.getAttribute("data-cp-mode");
        var openBtn = section.querySelector("[data-cp-open]");
        var cancelBtns = section.querySelectorAll("[data-cp-cancel]");

        if (mode === "readonly") {
            // the form stays on screen; the boxes are just locked until Edit is pressed
            var actions = section.querySelector("[data-cp-actions]");
            var form = section.querySelector("[data-cp-form]");
            fieldsOf(section).forEach(function (f) { f.readOnly = !open; });
            if (form) form.classList.toggle("is-readonly", !open);
            if (actions) actions.hidden = !open;
        } else {
            var panel = section.querySelector("[data-cp-panel]");
            if (panel) panel.hidden = !open;
        }

        if (openBtn) openBtn.hidden = open;
        cancelBtns.forEach(function (b) { b.hidden = !open; });
        section.setAttribute("data-open", open ? "true" : "false");
    }

    sections.forEach(function (section) {
        var openBtn = section.querySelector("[data-cp-open]");
        var form = section.querySelector("[data-cp-form]");

        setOpen(section, section.getAttribute("data-open") === "true");

        if (openBtn) {
            openBtn.addEventListener("click", function () {
                setOpen(section, true);
                var first = fieldsOf(section)[0];
                if (first) { first.focus(); if (first.select && first.type !== "password") first.select(); }
            });
        }

        section.querySelectorAll("[data-cp-cancel]").forEach(function (btn) {
            btn.addEventListener("click", function () {
                if (form) form.reset();               // back to what is saved
                clearProblems(section);
                setOpen(section, false);
                if (openBtn) openBtn.focus();
            });
        });
    });

    // send a form once (a second click is ignored); the buttons are not disabled because a disabled
    // submit button would not send its value
    document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var message = form.getAttribute("data-confirm");
            if (message && !window.confirm(message)) { event.preventDefault(); return; }
            if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
            form.setAttribute("data-busy", "1");
            form.classList.add("is-busy");
        });
    });

    // the server sent a form back with problems: jump to the first box that has one
    var firstBad = document.querySelector(".sv-cp [aria-invalid='true']");
    if (firstBad && typeof firstBad.focus === "function") firstBad.focus();

    // coming back with the browser's Back button must not leave a form locked
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
            form.removeAttribute("data-busy");
            form.classList.remove("is-busy");
        });
    });
})();
