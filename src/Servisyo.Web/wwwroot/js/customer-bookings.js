/* Customer > Dashboard and My Bookings: asks "are you sure?" before a cancellation or a payment and stops double-clicks.
   The real work (and every permission check) happens on the server when a form is submitted.
   Listens on the whole document (not on each form), so forms that live-bookings.js puts on the page later work too. */
(function () {
    "use strict";
    if (window.__svOnceForms) return;          // the dashboard and My Bookings may load more than one of these files
    window.__svOnceForms = true;

    // A booking card can hold several forms (for example Accept AND Reject). Once one of them is sent, the whole
    // card is locked so the other button cannot be pressed at the same time.
    function cardOf(form) { return form.closest(".sv-bk") || form.closest(".sv-booking") || form; }

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute("data-sv-once")) return;

        var card = cardOf(form);
        if (card.getAttribute("data-busy")) { event.preventDefault(); return; }

        var question = form.getAttribute("data-sv-confirm");
        if (question && !window.confirm(question)) { event.preventDefault(); return; }

        // not disabling the button here: a disabled submit button would not send its form values
        card.setAttribute("data-busy", "1");
        card.classList.add("is-busy");
    });

    // coming back with the browser's Back button must not leave the cards locked
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("[data-busy]").forEach(function (card) {
            card.removeAttribute("data-busy");
            card.classList.remove("is-busy");
        });
    });
})();
