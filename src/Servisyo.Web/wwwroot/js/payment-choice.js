/* "Approve & Pay" window (Views/Shared/_PayChoice.cshtml): Cash on Service Completion or Mock E-Wallet.
   - a button with data-pay-open="pay-{id}" opens the window of that booking;
   - choosing the e-wallet shows GCash / Maya, a test mobile number and an instant test reference code;
   - the form is checked here first so the customer gets a quick message, but the SERVER checks everything again
     (who is paying, the booking, the amount, the number and the reference).
   Listens on the whole document, so windows that live-bookings.js puts on the page later work too. */
(function () {
    "use strict";
    if (window.__svPayChoice) return;
    window.__svPayChoice = true;

    function dialogOf(el) { return el.closest("[data-pay-dialog]"); }
    function q(dialog, sel) { return dialog.querySelector(sel); }
    function qa(dialog, sel) { return Array.prototype.slice.call(dialog.querySelectorAll(sel)); }

    function chosenMethod(dialog) {
        var r = q(dialog, "[data-pay-method]:checked");
        return r ? r.value : "";
    }

    function chosenPrefix(dialog) {
        var r = q(dialog, "[data-pay-walletname]:checked");
        return (r && r.getAttribute("data-prefix")) || "GCASH";
    }

    function showError(dialog, text) {
        var box = q(dialog, "[data-pay-error]");
        if (!box) return;
        box.textContent = text || "";
        box.hidden = !text;
    }

    // 8 random digits; crypto when the browser has it
    function digits8() {
        var out = "";
        if (window.crypto && window.crypto.getRandomValues) {
            var a = new Uint32Array(8);
            window.crypto.getRandomValues(a);
            for (var i = 0; i < 8; i++) out += String(a[i] % 10);
        } else {
            for (var j = 0; j < 8; j++) out += String(Math.floor(Math.random() * 10));
        }
        return out;
    }

    function newReference(dialog) {
        var ref = q(dialog, "[data-pay-ref]");
        if (ref) ref.value = chosenPrefix(dialog) + "-" + digits8();
    }

    // 09171234567 -> 0917-123-4567 (only digits are kept; at most 11)
    function formatMobile(value) {
        var d = String(value || "").replace(/\D/g, "").slice(0, 11);
        if (d.length <= 4) return d;
        if (d.length <= 7) return d.slice(0, 4) + "-" + d.slice(4);
        return d.slice(0, 4) + "-" + d.slice(4, 7) + "-" + d.slice(7);
    }

    function validMobile(value) { return /^09\d{9}$/.test(String(value || "").replace(/\D/g, "")); }

    // show / hide the e-wallet part; its inputs are disabled when hidden, so they are not sent with a cash choice
    function update(dialog) {
        var method = chosenMethod(dialog);
        var wallet = method === "MockEWallet";
        var panel = q(dialog, "[data-pay-wallet]");
        if (panel) panel.hidden = !wallet;
        qa(dialog, "[data-pay-wallet] input").forEach(function (i) { i.disabled = !wallet; });

        if (wallet) {
            var ref = q(dialog, "[data-pay-ref]");
            if (ref && !ref.value) newReference(dialog);
        }

        var submit = q(dialog, "[data-pay-submit]");
        var form = q(dialog, "[data-pay-form]");
        var amount = (form && form.getAttribute("data-pay-amount")) || "";
        if (submit) {
            submit.disabled = !method;
            submit.textContent = method === "CashOnCompletion" ? "Confirm — Pay Cash on Completion"
                : wallet ? "Pay " + amount + " (Test)"
                : "Choose a payment option";
        }
        qa(dialog, ".sv-payopt").forEach(function (opt) {
            var input = opt.querySelector("input");
            opt.classList.toggle("is-chosen", !!(input && input.checked));
        });
        showError(dialog, "");
    }

    function reset(dialog) {
        var form = q(dialog, "[data-pay-form]");
        if (form) form.reset();
        var ref = q(dialog, "[data-pay-ref]");
        if (ref) ref.value = "";
        update(dialog);
    }

    function open(id) {
        var dialog = document.getElementById(id);
        if (!dialog || !dialog.hasAttribute("data-pay-dialog")) return;
        reset(dialog);
        if (typeof dialog.showModal === "function") dialog.showModal(); else dialog.setAttribute("open", "");
        var first = q(dialog, "[data-pay-method]");
        if (first) first.focus();
    }

    function close(dialog) {
        if (!dialog) return;
        if (typeof dialog.close === "function") dialog.close(); else dialog.removeAttribute("open");
    }

    document.addEventListener("click", function (e) {
        var t = e.target;
        if (!(t instanceof Element)) return;

        var opener = t.closest("[data-pay-open]");
        if (opener) { e.preventDefault(); open(opener.getAttribute("data-pay-open")); return; }

        if (t.closest("[data-pay-close]")) { close(dialogOf(t)); return; }

        var gen = t.closest("[data-pay-gen]");
        if (gen) { newReference(dialogOf(gen)); return; }

        // a click on the dark area around the window closes it
        if (t.matches("dialog[data-pay-dialog]")) close(t);
    });

    document.addEventListener("change", function (e) {
        var t = e.target;
        if (!(t instanceof Element)) return;
        var dialog = dialogOf(t);
        if (!dialog) return;
        if (t.matches("[data-pay-method]")) update(dialog);
        if (t.matches("[data-pay-walletname]")) newReference(dialog);     // GCASH-... <-> MAYA-...
    });

    document.addEventListener("input", function (e) {
        var t = e.target;
        if (!(t instanceof Element) || !t.matches("[data-pay-mobile]")) return;
        var formatted = formatMobile(t.value);
        if (formatted !== t.value) t.value = formatted;
        showError(dialogOf(t), "");                 // the old message no longer applies
    });

    // Runs BEFORE customer-bookings.js (capture phase): a form that is not complete is stopped here,
    // so the booking card is not locked as "busy".
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute("data-pay-form")) return;
        var dialog = dialogOf(form);

        function stop(text, focusEl) {
            e.preventDefault();
            e.stopPropagation();
            showError(dialog, text);
            if (focusEl) focusEl.focus();
        }

        var method = chosenMethod(dialog);
        if (!method) return stop("Please choose how you want to pay.", q(dialog, "[data-pay-method]"));

        if (method === "MockEWallet") {
            var mobile = q(dialog, "[data-pay-mobile]");
            if (!mobile || !validMobile(mobile.value))
                return stop("Enter an 11-digit mobile number that starts with 09, like 0917-000-0000.", mobile);
            var ref = q(dialog, "[data-pay-ref]");
            if (ref && !ref.value) newReference(dialog);
        }

        var submit = q(dialog, "[data-pay-submit]");
        if (submit) submit.textContent = method === "MockEWallet" ? "Processing test payment…" : "Confirming…";
    }, true);

    // coming back with the browser's Back button: close any window that was left open
    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        document.querySelectorAll("dialog[data-pay-dialog][open]").forEach(close);
    });
})();
