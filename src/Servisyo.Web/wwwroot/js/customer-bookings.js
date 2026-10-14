/* Customer > My Bookings: asks "are you sure?" before a cancellation and stops double-clicks.
   The real work (and every permission check) happens on the server when a form is submitted. */
(function () {
    "use strict";

    // A booking card holds one form per action (only the Cancel button for now). Once one of them is sent, the whole
    // card is locked so the other button cannot be pressed at the same time.
    function lockCard(form) {
        var card = form.closest(".sv-bk") || form;
        card.setAttribute("data-busy", "1");
        card.classList.add("is-busy");
    }

    function unlockAll() {
        document.querySelectorAll(".sv-bk[data-busy]").forEach(function (card) {
            card.removeAttribute("data-busy");
            card.classList.remove("is-busy");
        });
    }

    document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            var card = form.closest(".sv-bk") || form;
            if (card.getAttribute("data-busy")) { event.preventDefault(); return; }

            var question = form.getAttribute("data-sv-confirm");
            if (question && !window.confirm(question)) { event.preventDefault(); return; }

            // not disabling the button here: a disabled submit button would not send its form values
            lockCard(form);
        });
    });

    // coming back with the browser's Back button must not leave the cards locked
    window.addEventListener("pageshow", function (event) {
        if (event.persisted) unlockAll();
    });
})();
