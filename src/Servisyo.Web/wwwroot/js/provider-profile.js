/* Provider > My Profile.
   1. Sends each form once (stops double-clicks) and puts the cursor on the first problem.
   2. "Request Name Change" (database v2.8): opens the pop-up ONLY when the button is clicked (or when the server sent
      the form back with problems: data-open="true"), checks the obvious things before sending, and asks before a
      request is cancelled.
   3. Live: when an administrator answers (SignalR "nameChangeChanged", see live-bookings.js), the name card, the name
      in the header and the locked name boxes are fetched again. If the live connection is down, every 30 seconds
      while a request waits.
   The real checks happen on the server when a form is submitted. */
(function () {
    "use strict";

    var LETTERS = "A-Za-zÀ-ÖØ-öø-ÿ";       // the same letters as InputRules.Name
    var NAME = new RegExp("^(?=.*[" + LETTERS + "])[" + LETTERS + "\\s.'-]+$");
    var refreshWaiting = false;

    function clean(value) { return (value || "").trim().replace(/\s+/g, " "); }

    function ncDialog() { return document.getElementById("pf-nc-dialog"); }

    function openNameChange() {
        var dialog = ncDialog();
        if (!dialog || dialog.open || typeof dialog.showModal !== "function") return;
        dialog.showModal();
        var first = dialog.querySelector("[aria-invalid='true']") || dialog.querySelector("input");
        if (first) first.focus();
    }

    // ---- clicks (delegated: the name card can be redrawn live)
    document.addEventListener("click", function (event) {
        var opener = event.target.closest("#pf-nc-open");
        if (opener) {
            event.preventDefault();
            if (opener.getAttribute("aria-disabled") === "true") return;      // one request at a time (the title says why)
            openNameChange();
            return;
        }

        var closer = event.target.closest("[data-sv-close]");
        if (closer && closer.closest("dialog")) {
            event.preventDefault();
            closer.closest("dialog").close();
            return;
        }

        var dialog = event.target.closest("dialog");
        if (dialog && event.target === dialog) { dialog.close(); return; }      // a click on the dark backdrop

        var asks = event.target.closest("[data-confirm]");
        if (asks && !window.confirm(asks.getAttribute("data-confirm"))) event.preventDefault();
    });

    // ---- the name change form: the same rules as the server, so most mistakes are shown at once
    function showNcError(form, text) {
        var box = form.querySelector("[data-sv-nc-error]");
        if (!box) return;
        box.textContent = text;
        if (text) box.removeAttribute("hidden"); else box.setAttribute("hidden", "hidden");
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form.matches || !form.matches("form[data-sv-nc-form]")) return;

        var first = clean(form.elements.FirstName.value);
        var middle = clean(form.elements.MiddleName.value);
        var last = clean(form.elements.LastName.value);
        var reason = (form.elements.Reason.value || "").trim();
        var problem = "";

        if (!first || !middle || !last) problem = "Enter your first, middle and last name.";
        else if (!NAME.test(first) || !NAME.test(middle) || !NAME.test(last)) problem = "Please enter a valid name containing letters only.";
        else if (middle.length < 2) problem = "Middle name must be at least 2 characters.";
        else if (first === clean(form.getAttribute("data-current-first")) && middle === clean(form.getAttribute("data-current-middle"))
                 && last === clean(form.getAttribute("data-current-last")))
            problem = "That is already the name on your account. Change at least one of the names.";
        else if (!reason) problem = "Tell the administrator why your name should change (for example: married, or a spelling mistake).";

        if (problem) {
            event.preventDefault();
            showNcError(form, problem);
            return;
        }
        showNcError(form, "");
    });

    // ---- one submit only (registered after the check above, which may stop the submit first)
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form.matches || !form.matches("form[data-sv-once]")) return;
        if (event.defaultPrevented) return;            // another check (e.g. base-fee.js) stopped this submit: stay editable
        if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
        // not disabling the button here: a disabled submit button would not send its form values
        form.setAttribute("data-busy", "1");
        form.classList.add("is-busy");
    });

    // the server sent the Business Information form back with problems: jump to the first box that has one
    var firstBad = document.querySelector(".sv-pf__form [aria-invalid='true']");
    if (firstBad && typeof firstBad.focus === "function") firstBad.focus();

    // the server refused a name change request: show the pop-up again with the problems
    var dialogAtLoad = ncDialog();
    if (dialogAtLoad && dialogAtLoad.getAttribute("data-open") === "true") openNameChange();

    // coming back with the browser's Back button must not leave the forms locked
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
            form.removeAttribute("data-busy");
            form.classList.remove("is-busy");
        });
    });

    // ---- live: an administrator answered (or the provider changed it in another tab)
    function busy() { return !!document.querySelector("dialog[open], form[data-busy]"); }

    function refreshName() {
        var areas = document.querySelectorAll('[data-sv-namechange-live="true"][id]');
        var card = document.getElementById("pf-namechange");
        if (!areas.length || !card || !window.fetch || !window.DOMParser) return;
        if (busy()) { refreshWaiting = true; return; }          // never throw away what the provider is typing
        refreshWaiting = false;

        fetch(card.getAttribute("data-refresh-url") || window.location.pathname,
              { credentials: "same-origin", cache: "no-store", headers: { "X-Requested-With": "fetch" } })
            .then(function (response) { return response.ok && !response.redirected ? response.text() : null; })
            .then(function (html) {
                if (!html) return;
                if (busy()) { refreshWaiting = true; return; }
                var fresh = new DOMParser().parseFromString(html, "text/html");
                document.querySelectorAll('[data-sv-namechange-live="true"][id]').forEach(function (area) {
                    var next = fresh.getElementById(area.id);
                    if (!next || next.innerHTML === area.innerHTML) return;
                    area.innerHTML = next.innerHTML;
                    if (area.id === "pf-namechange") {
                        area.classList.remove("sv-live-flash");
                        void area.offsetWidth;                  // restart the highlight
                        area.classList.add("sv-live-flash");
                    }
                });
            })
            .catch(function () { });
    }

    document.addEventListener("sv:namechange-changed", refreshName);
    document.addEventListener("close", function (event) {
        if (event.target && event.target.id === "pf-nc-dialog" && refreshWaiting) refreshName();
    }, true);

    setInterval(function () {
        if (document.hidden || document.documentElement.getAttribute("data-sv-live-connected") === "true") return;
        if (!document.querySelector("#pf-namechange .is-pending")) return;      // only while a request waits
        refreshName();
    }, 30000);
})();
