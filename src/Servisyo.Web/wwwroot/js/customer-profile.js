/* Customer > My Profile. Without JavaScript every form is simply open and works as a normal form.
   With JavaScript the cards start "closed" (Edit / Edit Address / Change Password buttons open them),
   a double-click cannot send a form twice, and the Saved Address dropdown is saved through
   PUT /api/profile/address without reloading the page. A saved address can be changed but not removed.
   The real checks always happen on the server. */
(function () {
    "use strict";

    var sections = document.querySelectorAll("[data-cp-section]");

    function fieldsOf(section) {
        return section.querySelectorAll("input:not([type=hidden]), textarea, select");
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
            // a field the server locked (class is-locked: name during active bookings) always stays read-only
            fieldsOf(section).forEach(function (f) { f.readOnly = !open || f.classList.contains("is-locked"); });
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

    // ---------------------------------------------------------------- Saved Address (PUT /api/profile/address)

    function showFlash(message) {
        var flash = document.querySelector(".sv-cp > .sv-flash");
        if (!flash) {
            flash = document.createElement("div");
            flash.setAttribute("role", "status");
            var sub = document.querySelector(".sv-cp > .sv-dash__sub");
            if (sub) sub.insertAdjacentElement("afterend", flash);
            else document.querySelector(".sv-cp").prepend(flash);
        }
        flash.className = "sv-flash sv-flash--ok";
        flash.textContent = message;
    }

    document.querySelectorAll("form[data-cp-api]").forEach(function (form) {
        var section = form.closest("[data-cp-section]");
        var select = form.querySelector("select[name=Address]");
        var tokenField = form.querySelector("input[name=__RequestVerificationToken]");
        if (!section || !select || !tokenField || !window.fetch) return;     // keep the normal form

        function showProblem(message) {
            clearProblems(section);
            var problem = document.createElement("p");
            problem.className = "sv-fld__err";
            problem.setAttribute("role", "alert");
            problem.textContent = message;
            select.closest(".sv-fld").appendChild(problem);
            select.setAttribute("aria-invalid", "true");
            select.focus();
        }

        function setBusy(busy) {
            if (busy) { form.setAttribute("data-busy", "1"); form.classList.add("is-busy"); }
            else { form.removeAttribute("data-busy"); form.classList.remove("is-busy"); }
        }

        function saved(answer) {
            var card = section.querySelector("[data-cp-address-card]");
            var text = section.querySelector("[data-cp-address-text]");
            var empty = section.querySelector("[data-cp-address-empty]");
            var openBtn = section.querySelector("[data-cp-address-open]");
            if (text) text.textContent = answer.address;
            if (card) card.hidden = false;
            if (empty) empty.hidden = true;
            if (openBtn) openBtn.textContent = "Edit Address";
            // Cancel (form.reset) now goes back to the NEW saved address
            Array.prototype.forEach.call(select.options, function (o) { o.defaultSelected = o.value === answer.address; });
            clearProblems(section);
            setOpen(section, false);
            showFlash(answer.message);
            if (openBtn) openBtn.focus();
        }

        form.addEventListener("submit", function (event) {
            event.preventDefault();
            if (form.getAttribute("data-busy")) return;                        // a double-click sends it once
            if (!select.value) { showProblem("Please choose one of the addresses in the list."); return; }

            setBusy(true);
            fetch(form.getAttribute("data-cp-api"), {
                method: "PUT",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "RequestVerificationToken": tokenField.value
                },
                body: JSON.stringify({ address: select.value })
            }).then(function (response) {
                // signed out or no longer an active customer: reloading lets the server send them to the login page
                if (response.status === 401 || response.status === 403) { window.location.reload(); return null; }
                return response.json()
                    .catch(function () { return {}; })
                    .then(function (body) { return { ok: response.ok, body: body || {} }; });
            }).then(function (result) {
                if (!result) return;
                if (result.ok) saved(result.body);
                else showProblem(result.body.message || "Your address could not be saved. Please reload the page and try again.");
            }).catch(function () {
                showProblem("Could not reach SERVISYO. Check your connection and try again.");
            }).then(function () { setBusy(false); });
        });
    });

    // the server sent a form back with problems: jump to the first box that has one
    var firstBad = document.querySelector(".sv-cp [aria-invalid='true']");
    if (firstBad && typeof firstBad.focus === "function") firstBad.focus();

    // coming back with the browser's Back button must not leave a form locked
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("form[data-sv-once], form[data-cp-api]").forEach(function (form) {
            form.removeAttribute("data-busy");
            form.classList.remove("is-busy");
        });
    });
})();
