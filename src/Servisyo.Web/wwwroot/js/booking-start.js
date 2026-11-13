/* Provider > Booking Requests: "Mark In Progress" inside the start window (database v2.12, Philippine time).
   - Before the scheduled time the card has no button, only "Scheduled for ... Start button unlocks at scheduled time."
   - 0 to 15 minutes after: the blue "Mark In Progress" (sent with fetch; a plain form without JavaScript).
   - 16 minutes to 2 hours after: the amber "Mark In Progress (Late)" opens "Late Start"; the reason is required.
   - Later: no button; the booking is cancelled automatically as a provider no-show.
   The server decides all of this again (ProviderBookingsService.StartAsync) and answers 400 when the time is wrong.
   Each card says in how many seconds its state changes (data-start-refresh-in); the page redraws itself then
   (live-bookings.js, or a reload when that script is not running), so buttons appear and disappear on time.
   Listens on the whole document, so it keeps working after live-bookings.js redraws the cards. */
(function () {
    "use strict";
    if (window.__svStart) return;
    window.__svStart = true;

    var MAX_CHARS = 500;
    var TEXT = {
        required: "Please enter the reason for the late start before marking the booking In Progress.",
        tooLong: "Please keep the late reason to 500 characters or fewer.",
        offline: "Could not reach SERVISYO. Check your connection and try again.",
        failed: "Something went wrong. Refresh the page and try again."
    };

    var opener = null;

    // ------------------------------------------------------------------ sending

    function post(url, form) {
        return fetch(url, {
            method: "POST",
            body: new FormData(form),            // the reason (late), the anti-forgery token, the tab and page
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "application/json" }
        }).then(function (response) {
            return response.text().then(function (text) {
                var data = null;
                try { data = text ? JSON.parse(text) : null; } catch (ignore) { data = null; }
                return { status: response.status, data: data || {} };
            });
        });
    }

    function followRedirect(answer) {
        var data = answer.data;
        if (data.redirect && (answer.status === 200 || answer.status === 401 || answer.status === 403)) {
            window.location.assign(data.redirect);       // started: the page shows the green message
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the on-time button

    function lock(card, on) {
        if (!card) return;
        if (on) { card.setAttribute("data-busy", "1"); card.classList.add("is-busy"); }
        else { card.removeAttribute("data-busy"); card.classList.remove("is-busy"); }
    }

    function cardError(card, text) {
        var box = card && card.querySelector("[data-start-error]");
        if (!box) return;
        box.textContent = text || "";
        box.hidden = !text;
    }

    function startOnTime(form) {
        var card = form.closest(".sv-bk");
        if (card && card.getAttribute("data-busy")) return;
        lock(card, true);
        cardError(card, "");
        post(form.getAttribute("action"), form)
            .then(function (answer) {
                if (followRedirect(answer)) return;
                lock(card, false);
                cardError(card, answer.data.message || TEXT.failed);
                // too early / too late: show the card as it is now (the right notice or button) in a moment
                if (answer.status === 400 || answer.status === 409) setTimeout(redraw, 2500);
            })
            .catch(function () {
                lock(card, false);
                cardError(card, TEXT.offline);
            });
    }

    // ------------------------------------------------------------------ the "Late Start" window

    function dialog() { return document.querySelector("[data-late-dialog]"); }
    function part(d, name) { return d.querySelector("[data-late-" + name + "]"); }
    function errorBox(d, which) { return d.querySelector('[data-late-error="' + which + '"]'); }

    function setError(d, which, text) {
        var box = errorBox(d, which);
        if (!box) return;
        box.textContent = text || "";
        box.hidden = !text;
        if (which === "reason") {
            var field = part(d, "reason");
            if (text) field.setAttribute("aria-invalid", "true"); else field.removeAttribute("aria-invalid");
        }
    }

    function setBusy(d, busy) {
        part(d, "submit").disabled = busy;
        part(d, "submit-text").textContent = busy ? "Starting…" : "Start Job (Late)";
        d.setAttribute("aria-busy", busy ? "true" : "false");
    }

    function count(d) {
        part(d, "count").textContent = String(part(d, "reason").value.length);
    }

    function reset(d) {
        part(d, "reason").value = "";
        count(d);
        setError(d, "reason", "");
        setError(d, "general", "");
        setBusy(d, false);
    }

    function open(button) {
        var d = dialog();
        if (!d) return;
        opener = button;
        reset(d);
        part(d, "form").setAttribute("action", button.getAttribute("data-late-url"));
        var id = button.getAttribute("data-late-id");
        var title = button.getAttribute("data-late-title") || "this booking";
        var customer = button.getAttribute("data-late-customer");
        part(d, "sub").textContent = "Booking #" + id + " · " + title + (customer ? " for " + customer : "") +
            ". You are starting more than 15 minutes after the scheduled time, so please say why.";
        part(d, "badge-text").textContent = button.getAttribute("data-late-minutes") || "Late";
        if (typeof d.showModal === "function") d.showModal(); else d.setAttribute("open", "");
        part(d, "reason").focus();
    }

    function close(d) {
        if (d.getAttribute("aria-busy") === "true") return;          // sending: wait for the answer
        reset(d);
        if (d.open) d.close(); else d.removeAttribute("open");
        if (opener && document.body.contains(opener)) opener.focus();
        opener = null;
    }

    // the same checks as the server, before anything is sent
    function check(d) {
        var reason = part(d, "reason").value.trim();
        var error = reason.length === 0 ? TEXT.required : reason.length > MAX_CHARS ? TEXT.tooLong : null;
        setError(d, "reason", error);
        setError(d, "general", "");
        if (error) part(d, "reason").focus();
        return !error;
    }

    function sendLate(d) {
        var form = part(d, "form");
        setBusy(d, true);
        post(form.getAttribute("action"), form)
            .then(function (answer) {
                if (followRedirect(answer)) return;
                setBusy(d, false);
                var errors = answer.data.errors;
                if (answer.status === 400 && errors && errors.lateReason) {
                    setError(d, "reason", errors.lateReason);
                    part(d, "reason").focus();
                    return;
                }
                setError(d, "general", answer.data.message || TEXT.failed);
                // too early or the 2 hours have passed: the card changes; show it as it is now in a moment
                if (answer.status === 400 || answer.status === 409) {
                    setTimeout(function () { if (d.open) close(d); redraw(); }, 2500);
                }
            })
            .catch(function () {
                setBusy(d, false);
                setError(d, "general", TEXT.offline);
            });
    }

    // ------------------------------------------------------------------ events

    document.addEventListener("click", function (event) {
        var target = event.target;
        if (!(target instanceof Element)) return;
        var button = target.closest("[data-late-open]");
        if (button) { event.preventDefault(); open(button); return; }

        var d = dialog();
        if (!d || !d.open) return;
        if (target.closest("[data-late-cancel]")) { close(d); return; }
        if (target === d) close(d);                                   // a click on the dark backdrop
    });

    document.addEventListener("input", function (event) {
        var d = dialog();
        if (!d || event.target !== part(d, "reason")) return;
        count(d);
        if (part(d, "reason").value.trim().length > 0) setError(d, "reason", "");
    });

    // Esc: close the same way as Cancel ("cancel" does not bubble, so it is caught on the way down)
    document.addEventListener("cancel", function (event) {
        var d = dialog();
        if (d && event.target === d) { event.preventDefault(); close(d); }
    }, true);

    // caught on the way down, before provider-bookings.js locks the card for a plain post
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement) || !window.fetch || !window.FormData) return;   // old browser: a normal post

        var d = dialog();
        if (d && form === part(d, "form")) {
            event.preventDefault();
            if (part(d, "submit").disabled) return;                   // already sending
            if (check(d)) sendLate(d);
            return;
        }
        if (form.hasAttribute("data-start-form")) {
            event.preventDefault();
            startOnTime(form);
        }
    }, true);

    // ------------------------------------------------------------------ redraw when a window opens, turns late or closes

    function userIsBusy() {
        if (document.querySelector("dialog[open], [data-busy]")) return true;
        var active = document.activeElement;
        return !!(active && /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName));
    }

    function redraw() {
        if (typeof window.svLiveRefresh === "function") { window.svLiveRefresh(); return; }
        if (!userIsBusy()) window.location.reload();                   // live updates are not running: load the page again
    }

    function tick() {
        var now = Date.now();
        var due = [];
        document.querySelectorAll("[data-start-refresh-in]").forEach(function (el) {
            // counted from when this card was drawn (the seconds come from the server, so a wrong phone clock does not matter)
            if (!el.__svDue) el.__svDue = now + (parseInt(el.getAttribute("data-start-refresh-in"), 10) || 0) * 1000;
            if (el.__svDue <= now) due.push(el);
        });
        if (due.length === 0) return;
        due.forEach(function (el) { el.__svDue = now + 30000; });   // not again for 30 seconds if the redraw has to wait
        redraw();
    }

    tick();
    setInterval(tick, 5000);
})();
