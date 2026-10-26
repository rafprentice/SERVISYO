/* The notification bell (top bar, every signed-in role).
   - the red number comes from the server and is refreshed every 30 seconds while the tab is visible
   - clicking the bell opens a panel with the newest notifications (loaded from the server each time)
   - clicking a notification opens the page it is about (the server marks it read on the way)
   - "Mark all as read" asks the server, then updates the panel and the number
   Text coming from the server is only ever put on the page with textContent. Every permission check is on the server. */
(function () {
    "use strict";

    var root = document.querySelector("[data-sv-np]");
    if (!root) return;

    var toggle = root.querySelector("[data-sv-np-toggle]");
    var panel = root.querySelector(".sv-np__panel");
    var list = root.querySelector("[data-sv-np-list]");
    var badge = root.querySelector("[data-sv-np-badge]");
    var pill = root.querySelector("[data-sv-np-pill]");
    var readAll = root.querySelector("[data-sv-np-readall]");
    var panelUrl = root.getAttribute("data-panel-url");
    var readAllUrl = root.getAttribute("data-readall-url");
    if (!toggle || !panel || !list || !badge || !panelUrl) return;

    var SVG = "http://www.w3.org/2000/svg";
    var ICONS = {
        calendar: ['<path d="M8 2v4"/>', '<path d="M16 2v4"/>', '<rect width="18" height="18" x="3" y="4" rx="2"/>', '<path d="M3 10h18"/>'],
        user: ['<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/>', '<circle cx="12" cy="7" r="4"/>'],
        bell: ['<path d="M10.268 21a2 2 0 0 0 3.464 0"/>', '<path d="M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326"/>']
    };

    var requestId = 0;      // answers that arrive late (older than the newest request) are ignored
    var failures = 0;

    function icon(name) {
        // the markup comes from the fixed table above, never from the server
        var svg = document.createElementNS(SVG, "svg");
        svg.setAttribute("width", "16");
        svg.setAttribute("height", "16");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("fill", "none");
        svg.setAttribute("stroke", "currentColor");
        svg.setAttribute("stroke-width", "2");
        svg.setAttribute("stroke-linecap", "round");
        svg.setAttribute("stroke-linejoin", "round");
        svg.setAttribute("aria-hidden", "true");
        svg.innerHTML = (ICONS[name] || ICONS.bell).join("");
        return svg;
    }

    function token() {
        var field = root.querySelector('input[name="__RequestVerificationToken"]');
        return field ? field.value : "";
    }

    function setUnread(count) {
        count = Math.max(0, count | 0);
        badge.textContent = count > 99 ? "99+" : String(count);
        if (count > 0) badge.removeAttribute("hidden"); else badge.setAttribute("hidden", "hidden");
        toggle.setAttribute("aria-label", "Notifications, " + count + " unread");
        if (pill) {
            pill.textContent = count + " Unread";
            if (count > 0) pill.removeAttribute("hidden"); else pill.setAttribute("hidden", "hidden");
        }
        if (readAll) readAll.hidden = count === 0;
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function message(text) {
        list.textContent = "";
        list.appendChild(el("li", "sv-np__empty", text));
    }

    function render(data) {
        setUnread(data.unread);
        list.textContent = "";
        if (!data.items || data.items.length === 0) { message("You're all caught up."); return; }

        data.items.forEach(function (n) {
            var li = document.createElement("li");
            var a = el("a", "sv-np__item" + (n.read ? "" : " is-unread"));
            a.href = n.url;

            var ic = el("span", "sv-np__icon sv-np__icon--" + (n.icon || "bell"));
            ic.appendChild(icon(n.icon));
            a.appendChild(ic);

            var body = el("span", "sv-np__body");
            body.appendChild(el("span", "sv-np__title", n.title));
            body.appendChild(el("span", "sv-np__msg", n.message));
            body.appendChild(el("span", "sv-np__time", n.time));
            a.appendChild(body);

            if (!n.read) {
                var dot = el("span", "sv-np__dot");
                dot.setAttribute("role", "img");
                dot.setAttribute("aria-label", "Unread");
                a.appendChild(dot);
            }
            li.appendChild(a);
            list.appendChild(li);
        });
    }

    function isOpen() { return !panel.hidden; }

    function load(showLoading) {
        var mine = ++requestId;
        if (showLoading && list.children.length === 0) message("Loading…");

        return fetch(panelUrl, { headers: { "Accept": "application/json" }, credentials: "same-origin", cache: "no-store" })
            .then(function (response) {
                if (response.status === 401) { window.location.reload(); throw new Error("signed out"); }
                if (!response.ok) throw new Error("bad answer");
                return response.json();
            })
            .then(function (data) {
                failures = 0;
                if (mine !== requestId) return;
                if (isOpen() || list.children.length === 0 || showLoading) render(data); else setUnread(data.unread);
            })
            .catch(function () {
                failures++;
                if (isOpen() && mine === requestId) message("Couldn't load notifications. Please try again.");
            });
    }

    function open() {
        panel.hidden = false;
        toggle.setAttribute("aria-expanded", "true");
        load(true);
    }

    function close(returnFocus) {
        if (!isOpen()) return;
        panel.hidden = true;
        toggle.setAttribute("aria-expanded", "false");
        if (returnFocus) toggle.focus();
    }

    toggle.addEventListener("click", function () { if (isOpen()) close(false); else open(); });

    document.addEventListener("mousedown", function (event) { if (isOpen() && !root.contains(event.target)) close(false); });
    document.addEventListener("keydown", function (event) { if (event.key === "Escape") close(true); });

    if (readAll && readAllUrl) {
        readAll.addEventListener("click", function () {
            if (readAll.getAttribute("data-busy")) return;
            readAll.setAttribute("data-busy", "1");

            var body = new FormData();
            body.append("__RequestVerificationToken", token());
            fetch(readAllUrl, { method: "POST", body: body, credentials: "same-origin", headers: { "Accept": "application/json" } })
                .then(function (response) {
                    if (response.status === 401) { window.location.reload(); throw new Error("signed out"); }
                    if (!response.ok) throw new Error("bad answer");
                    setUnread(0);
                    list.querySelectorAll(".sv-np__item.is-unread").forEach(function (a) { a.classList.remove("is-unread"); });
                    list.querySelectorAll(".sv-np__dot").forEach(function (d) { d.remove(); });
                })
                .catch(function () { /* nothing changed on the server, so the panel is left as it is */ })
                .finally(function () { readAll.removeAttribute("data-busy"); });
        });
    }

    // keep the number fresh: every 30 s while the tab is visible (every 2 min after repeated failures)
    window.setInterval(function () {
        if (document.hidden) return;
        if (failures >= 3 && Math.random() > 0.25) return;
        load(false);
    }, 30000);

    document.addEventListener("visibilitychange", function () { if (!document.hidden) load(false); });

    // the first number is already on the page; set the panel's own pieces to match it
    setUnread(parseInt(badge.textContent, 10) > 0 && !badge.hasAttribute("hidden") ? parseInt(badge.textContent, 10) : 0);
})();
