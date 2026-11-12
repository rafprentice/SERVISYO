/* Admin > Provider applications.
   1. "Review Application" buttons open the application in a pop-up (the page content is fetched from the server).
      If anything goes wrong, the browser simply opens the normal page instead.
   2. Asks "are you sure?" before a rejection and stops double-clicks from sending the form twice.
   3. Lets the green/red message at the top-right be closed.
   4. Day-off requests (database v2.7), live: when a provider sends or cancels a request (SignalR, see
      live-bookings.js), the provider's open "Days Off" list and the providers page's "waiting" list are fetched again.
   The real work (and every permission check) always happens on the server. */
(function () {
    "use strict";

    // ---- the message (toast): close button, and remove it once it has faded away
    document.querySelectorAll("[data-sv-toast]").forEach(function (toast) {
        var close = toast.querySelector("[data-sv-toast-close]");
        if (close) close.addEventListener("click", function () { toast.remove(); });
        toast.addEventListener("animationend", function (event) {
            if (event.animationName === "sv-toast-out") toast.remove();
        });
    });

    // ---- "are you sure?" for buttons that carry a data-confirm text (works in the pop-up and on the normal page)
    document.addEventListener("click", function (event) {
        var button = event.target.closest("[data-confirm]");
        if (button && !window.confirm(button.getAttribute("data-confirm"))) event.preventDefault();
    });

    // ---- one submit only
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form.matches || !form.matches("[data-sv-decision-form]")) return;
        if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
        form.setAttribute("data-busy", "1");
        form.classList.add("is-busy");
    });

    // ---- day-off requests, live (database v2.7)
    var reviewDialog = document.getElementById("sv-review-dialog");
    var boardWaiting = false;                     // the providers page changed while the pop-up was open

    function dialogOpen() { return !!(reviewDialog && reviewDialog.open); }

    /// Fetches url and puts the inside of its `selector` element into `target` (nothing happens when that fails:
    /// the next push or the next visit shows it).
    function swap(target, url, selector) {
        if (!url || !window.fetch || !window.DOMParser) return;
        fetch(url, { credentials: "same-origin", cache: "no-store", headers: { "X-Requested-With": "fetch" } })
            .then(function (response) { return response.ok && !response.redirected ? response.text() : null; })
            .then(function (html) {
                if (!html || !document.contains(target)) return;
                var fresh = new DOMParser().parseFromString(html, "text/html").querySelector(selector);
                if (!fresh || fresh.innerHTML === target.innerHTML) return;
                if (target.querySelector("[data-busy]")) return;      // an Approve / Reject is being sent right now
                target.innerHTML = fresh.innerHTML;
                target.classList.remove("sv-live-flash");
                void target.offsetWidth;                              // restart the highlight
                target.classList.add("sv-live-flash");
            })
            .catch(function () { });
    }

    function refreshBoard() {
        var board = document.querySelector('[data-sv-dayoff-board="true"]');
        if (!board) return;
        if (dialogOpen()) { boardWaiting = true; return; }        // do not redraw the cards behind an open pop-up
        boardWaiting = false;
        swap(board, board.getAttribute("data-refresh-url"), '[data-sv-dayoff-board="true"]');
    }

    function refreshDaysOff(providerId) {
        document.querySelectorAll('[data-sv-dayoff-live="true"][data-provider-id]').forEach(function (box) {
            if (providerId && box.getAttribute("data-provider-id") !== String(providerId)) return;
            swap(box, box.getAttribute("data-refresh-url"), '[data-sv-dayoff-live="true"]');
        });
    }

    document.addEventListener("sv:dayoff-changed", function (event) {
        var providerId = event.detail && event.detail.providerId;
        refreshDaysOff(providerId);
        refreshBoard();
    });
    if (reviewDialog) reviewDialog.addEventListener("close", function () { if (boardWaiting) refreshBoard(); });

    // If the live connection is not working (e.g. the SignalR script could not load), ask every 30 seconds instead.
    setInterval(function () {
        if (document.hidden || document.documentElement.getAttribute("data-sv-live-connected") === "true") return;
        if (!document.querySelector('[data-sv-dayoff-board="true"], [data-sv-dayoff-live="true"][data-provider-id]')) return;
        refreshDaysOff(null);
        refreshBoard();
    }, 30000);

    // ---- the pop-up
    var dialog = document.getElementById("sv-review-dialog");
    var body = document.getElementById("sv-review-body");
    if (!dialog || !body || typeof dialog.showModal !== "function") return;   // no pop-up here: links just work as normal links

    function closeDialog() { if (dialog.open) dialog.close(); }

    dialog.addEventListener("close", function () { body.innerHTML = ""; });

    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) { closeDialog(); return; }                // click on the dark backdrop
        var closer = event.target.closest("[data-sv-close]");
        if (closer) { event.preventDefault(); closeDialog(); }
    });

    function openReview(link) {
        if (dialog.open || link.getAttribute("data-loading")) return;
        link.setAttribute("data-loading", "1");

        var url = new URL(link.href, window.location.href);
        url.searchParams.set("partial", "true");

        fetch(url.toString(), { credentials: "same-origin", headers: { "X-Requested-With": "fetch" } })
            .then(function (response) {
                // a redirect means the sign-in expired; an error means the application is gone: use the normal page
                if (!response.ok || response.redirected) throw new Error("use the normal page");
                return response.text();
            })
            .then(function (html) {
                body.innerHTML = html;
                if (!body.querySelector("[data-sv-review]")) throw new Error("use the normal page");
                dialog.showModal();
            })
            .catch(function () { window.location.href = link.href; })
            .finally(function () { link.removeAttribute("data-loading"); });
    }

    document.addEventListener("click", function (event) {
        var link = event.target.closest('[data-review-link="true"]');   // Razor writes data-review-link="" for the others
        if (!link) return;
        // Ctrl/Cmd/Shift-click and middle-click keep their normal "open in a new tab" behaviour
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        openReview(link);
    });
})();
