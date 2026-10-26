/* Provider > My Profile: sends the form once (stops double-clicks) and puts the cursor on the first problem.
   The real checks happen on the server when the form is submitted. */
(function () {
    "use strict";

    document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
            // not disabling the button here: a disabled submit button would not send its form values
            form.setAttribute("data-busy", "1");
            form.classList.add("is-busy");
        });
    });

    // the server sent the form back with problems: jump to the first box that has one
    var firstBad = document.querySelector(".sv-pf__form [aria-invalid='true']");
    if (firstBad && typeof firstBad.focus === "function") firstBad.focus();

    // coming back with the browser's Back button must not leave the form locked
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
            form.removeAttribute("data-busy");
            form.classList.remove("is-busy");
        });
    });
})();
