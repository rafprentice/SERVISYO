/* Live booking updates (SignalR).
   When one of the signed-in user's bookings changes (a request, a quotation, a payment, a start, a completion...),
   the server sends "bookingChanged" to that user's open pages. This script then:
     - asks the bell for its new number;
     - on pages marked data-sv-live="on" (dashboards, My Bookings, Booking Requests): fetches the same page again and
       swaps the main area, so the progress meter, badges and buttons are redrawn by the server with all its checks;
     - elsewhere (or while the user is typing): shows a small "updated" note with a Refresh link.
   Nothing here changes data: the message only says "something changed, look again". */
(function () {
    "use strict";
    if (!window.signalR || !window.fetch || !window.DOMParser) return;    // old browser or CDN blocked: the page still works, just not live

    var script = document.currentScript;
    var hubUrl = (script && script.getAttribute("data-hub")) || "/hubs/bookings";
    var timer = null;
    var pending = null;

    function region() { return document.querySelector('[data-sv-live="on"]'); }

    // Don't throw away something the user is in the middle of (typing, a confirm box, a form being sent).
    function busy(area) {
        if (area.querySelector("[data-busy]")) return true;
        var active = document.activeElement;
        return !!(active && area.contains(active) && /^(INPUT|TEXTAREA|SELECT)$/.test(active.tagName));
    }

    function note(change) {
        var box = document.getElementById("sv-live-note");
        if (!box) {
            box = document.createElement("div");
            box.id = "sv-live-note";
            box.className = "sv-live-note";
            box.setAttribute("role", "status");
            document.body.appendChild(box);
        }
        box.innerHTML = "";
        var text = document.createElement("span");
        text.textContent = change && change.bookingId ? "Booking #" + change.bookingId + " was updated." : "Your bookings were updated.";
        var link = document.createElement("a");
        link.href = window.location.href;
        link.textContent = "Refresh";
        var close = document.createElement("button");
        close.type = "button";
        close.setAttribute("aria-label", "Dismiss");
        close.textContent = "×";
        close.addEventListener("click", function () { box.remove(); });
        box.appendChild(text);
        box.appendChild(link);
        box.appendChild(close);
    }

    function refresh(change) {
        document.dispatchEvent(new CustomEvent("sv:notifications-refresh"));

        var area = region();
        if (!area) { note(change); return; }
        if (busy(area)) { note(change); return; }

        fetch(window.location.href, { credentials: "same-origin", cache: "no-store", headers: { "X-Requested-With": "XMLHttpRequest" } })
            .then(function (response) {
                // signed out, suspended, or sent somewhere else: let the normal page handle it on the next click
                if (!response.ok || response.redirected) return null;
                return response.text();
            })
            .then(function (html) {
                if (!html) { note(change); return; }
                var fresh = new DOMParser().parseFromString(html, "text/html").querySelector('[data-sv-live="on"]');
                var current = region();
                if (!fresh || !current || busy(current)) { note(change); return; }

                current.innerHTML = fresh.innerHTML;
                current.classList.remove("sv-live-flash");
                void current.offsetWidth;                // restart the highlight animation
                current.classList.add("sv-live-flash");
                var old = document.getElementById("sv-live-note");
                if (old) old.remove();
            })
            .catch(function () { note(change); });
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on("bookingChanged", function (change) {
        // several changes close together (e.g. two tabs) -> one refresh
        pending = change || { bookingId: "" };
        clearTimeout(timer);
        timer = setTimeout(function () { refresh(pending); }, 300);
    });

    // ---- chat messages: keep the sidebar "Messages" badge right on every page
    function setMessageBadge(total) {
        document.querySelectorAll("[data-sv-msg-badge]").forEach(function (b) {
            var n = Math.max(0, parseInt(total, 10) || 0);
            b.textContent = n > 99 ? "99+" : String(n);
            if (n > 0) b.removeAttribute("hidden"); else b.setAttribute("hidden", "hidden");
        });
    }

    connection.on("ReceiveMessage", function (data) {
        setMessageBadge(data && data.unreadTotal);
        // the Messages page fetches the new message straight away (and marks it read if that chat is open)
        document.dispatchEvent(new CustomEvent("sv:message-received", { detail: data || {} }));
    });

    connection.on("MessagesRead", function (data) { setMessageBadge(data && data.unreadTotal); });

    // after a lost connection, catch up once (something may have changed while we were away)
    connection.onreconnected(function () { refresh({ bookingId: "" }); });

    function start() {
        connection.start().catch(function () { setTimeout(start, 10000); });   // server asleep / offline: try again later
    }
    start();
})();
