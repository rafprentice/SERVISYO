/* Admin > Provider applications.
   1. "Review Application" buttons open the application in a pop-up (the page content is fetched from the server).
      If anything goes wrong, the browser simply opens the normal page instead.
   2. Asks "are you sure?" before a rejection and stops double-clicks from sending the form twice.
   3. Lets the green/red message at the top-right be closed.
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
        var link = event.target.closest("[data-review-link]");
        if (!link) return;
        // Ctrl/Cmd/Shift-click and middle-click keep their normal "open in a new tab" behaviour
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        openReview(link);
    });
})();
