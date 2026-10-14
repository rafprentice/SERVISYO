/* Messages: sends without reloading, shows new messages within a few seconds (it asks the server every 3 s while the
   tab is visible), keeps the conversation list and the unread badge up to date, and filters the list as you type.
   The server decides everything that matters (who may message whom, what is saved); this file only draws the screen.
   Text from other people is always put on the page with textContent, never as HTML. */
(function () {
    "use strict";

    var root = document.querySelector("[data-ms]");
    if (!root) return;

    var pollUrl = root.getAttribute("data-poll-url");
    var partner = parseInt(root.getAttribute("data-partner") || "", 10) || null;
    var lastId = parseInt(root.getAttribute("data-last-id") || "0", 10) || 0;
    var meInitials = root.getAttribute("data-me-initials") || "";
    var partnerInitials = root.getAttribute("data-partner-initials") || "";

    var stream = root.querySelector("[data-ms-stream]");
    var form = root.querySelector("[data-ms-form]");
    var input = root.querySelector("[data-ms-input]");
    var sendBtn = root.querySelector("[data-ms-send]");
    var errorBox = root.querySelector("[data-ms-error]");
    var list = root.querySelector("[data-ms-convs]");
    var search = root.querySelector("[data-ms-search]");
    var noMatch = root.querySelector("[data-ms-nomatch]");
    var tplMsg = root.querySelector("[data-ms-tpl-msg]");
    var tplConv = root.querySelector("[data-ms-tpl-conv]");
    var statusPill = root.querySelector("[data-ms-status]");
    var contextLine = root.querySelector("[data-ms-context]");
    var navBadge = document.querySelector("[data-sv-msg-badge]");

    var POLL_MS = 3000;
    var SLOW_POLL_MS = 15000;
    var sending = false;
    var polling = false;
    var stopped = false;
    var failures = 0;
    var timer = null;

    // ---------------------------------------------------------------- small helpers

    function token() {
        var field = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        return field ? field.value : "";
    }

    function nearBottom() {
        return !stream || stream.scrollHeight - stream.scrollTop - stream.clientHeight < 80;
    }

    function scrollToBottom() {
        if (stream) stream.scrollTop = stream.scrollHeight;
    }

    function showError(text) {
        if (!errorBox) return;
        errorBox.textContent = text;
        errorBox.hidden = false;
    }

    function clearError() {
        if (errorBox) { errorBox.hidden = true; errorBox.textContent = ""; }
    }

    function autosize() {
        if (!input) return;
        input.style.height = "auto";
        input.style.height = Math.min(input.scrollHeight + 2, 120) + "px";     // +2 for the border
    }

    function field(node, name) {
        return node.querySelector('[data-f="' + name + '"]');
    }

    function updateNavBadge(total) {
        if (!navBadge || typeof total !== "number") return;
        navBadge.textContent = String(total);
        navBadge.hidden = total <= 0;
    }

    // ---------------------------------------------------------------- the open conversation

    // Adds one bubble in the right place (by message number). Returns false if it is already on the screen.
    function addMessage(m) {
        if (!stream || !tplMsg) return false;
        if (stream.querySelector('[data-mid="' + m.id + '"]')) return false;

        var hello = stream.querySelector("[data-ms-hello]");
        if (hello) hello.remove();

        var row = tplMsg.content.firstElementChild.cloneNode(true);
        row.setAttribute("data-mid", String(m.id));
        if (m.mine) row.classList.add("sv-ms__row--mine");
        field(row, "avatar").textContent = m.mine ? meInitials : partnerInitials;
        field(row, "text").textContent = m.text;
        field(row, "time").textContent = m.timeText;

        var rows = stream.querySelectorAll(".sv-ms__row");
        var before = null;
        for (var i = 0; i < rows.length; i++) {
            if (parseInt(rows[i].getAttribute("data-mid"), 10) > m.id) { before = rows[i]; break; }
        }
        stream.insertBefore(row, before);
        return true;
    }

    function readUrl() {
        return form ? form.getAttribute("action").replace(/\/send$/, "/read") : null;
    }

    // Tell the server the open conversation has been seen (only while the tab is visible).
    function markRead() {
        var url = readUrl();
        if (!url || document.hidden) return;
        var body = new FormData();
        body.append("__RequestVerificationToken", token());
        fetch(url, { method: "POST", body: body, credentials: "same-origin", headers: { "Accept": "application/json" } })
            .then(function (res) { return res.ok ? res.json() : null; })
            .then(function (data) { if (data) updateNavBadge(data.unreadTotal); })
            .catch(function () { /* the next poll will try again */ });
    }

    function send() {
        if (sending || !form || !input) return;
        var text = input.value;
        if (!text.trim()) { input.value = ""; autosize(); return; }

        sending = true;
        clearError();
        if (sendBtn) sendBtn.disabled = true;

        fetch(form.getAttribute("action"), {
            method: "POST",
            body: new FormData(form),                       // the text and the anti-forgery token
            credentials: "same-origin",
            headers: { "Accept": "application/json" }
        })
            .then(function (res) {
                if (res.redirected) { window.location.href = res.url; throw new Error("redirected"); }
                return res.json().catch(function () { return {}; }).then(function (data) { return { ok: res.ok, status: res.status, data: data }; });
            })
            .then(function (r) {
                if (r.ok && r.data && r.data.ok && r.data.sent) {
                    addMessage(r.data.sent);
                    scrollToBottom();
                    input.value = "";
                    autosize();
                } else if (r.status === 401) {
                    window.location.reload();
                } else {
                    showError((r.data && r.data.error) || "Your message could not be sent. Please try again.");
                }
            })
            .catch(function (err) {
                if (err && err.message === "redirected") return;
                showError("Could not send. Check your connection and try again.");
            })
            .then(function () {
                sending = false;
                if (sendBtn) sendBtn.disabled = false;
                input.focus();
            });
    }

    if (form && input) {
        form.addEventListener("submit", function (event) { event.preventDefault(); send(); });

        input.addEventListener("keydown", function (event) {
            // Enter sends, Shift+Enter makes a new line (and Enter while choosing a character in an IME does neither)
            if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
                event.preventDefault();
                send();
            }
        });
        input.addEventListener("input", function () { autosize(); clearError(); });
        autosize();
    }

    // ---------------------------------------------------------------- the conversation list

    function applySearch() {
        if (!list) return;
        var q = search ? search.value.trim().toLowerCase() : "";
        var items = list.querySelectorAll("[data-ms-item]");
        var shown = 0;
        items.forEach(function (li) {
            var match = !q || (li.getAttribute("data-search") || "").indexOf(q) !== -1;
            li.hidden = !match;
            if (match) shown++;
        });
        if (noMatch) noMatch.hidden = !(q && items.length > 0 && shown === 0);
    }

    function updateList(convs) {
        if (!list || !tplConv) return;

        convs.forEach(function (c) {
            var li = list.querySelector('[data-ms-item][data-partner="' + c.partnerUserId + '"]');
            if (!li) {
                li = tplConv.content.firstElementChild.cloneNode(true);
                li.setAttribute("data-partner", String(c.partnerUserId));
                li.querySelector("a").setAttribute("href", window.location.pathname + "?with=" + c.partnerUserId);
            }

            var isOpen = c.partnerUserId === partner;
            li.setAttribute("data-search", c.searchText || "");
            li.querySelector("a").classList.toggle("is-active", isOpen);
            field(li, "initials").textContent = c.initials;
            field(li, "name").textContent = c.name;
            field(li, "time").textContent = c.timeText || "";
            field(li, "context").textContent = c.contextText || "";
            field(li, "preview").textContent = c.preview || "";

            // the conversation on screen is being read, so it never shows an unread badge
            var unread = isOpen ? 0 : c.unread;
            var badge = field(li, "unread");
            badge.textContent = String(unread);
            badge.hidden = unread <= 0;

            list.appendChild(li);                          // appending in server order also re-orders the list

            if (isOpen) {
                if (statusPill) {
                    statusPill.textContent = c.statusText;
                    statusPill.className = "sv-tag sv-tag--" + c.statusTone;
                }
                if (contextLine) contextLine.textContent = c.contextText || "";
            }
        });

        applySearch();
    }

    if (search) {
        search.addEventListener("input", applySearch);
        search.addEventListener("keydown", function (event) {
            if (event.key === "Escape") { search.value = ""; applySearch(); }
        });
    }

    // ---------------------------------------------------------------- asking the server what is new

    function handlePoll(data) {
        var wasNearBottom = nearBottom();
        var added = false;
        var incoming = false;

        (data.messages || []).forEach(function (m) {
            if (m.id > lastId) lastId = m.id;
            if (addMessage(m)) {
                added = true;
                if (!m.mine) incoming = true;
            }
        });

        if (added && wasNearBottom) scrollToBottom();
        updateList(data.conversations || []);

        // new words from the other person are being looked at right now, so they count as read
        if (incoming) markRead();
        else updateNavBadge(data.unreadTotal);
    }

    function schedule(ms) {
        clearTimeout(timer);
        if (!stopped) timer = setTimeout(poll, ms);
    }

    function poll() {
        if (stopped || polling) return;
        if (document.hidden) { schedule(POLL_MS); return; }      // nothing to draw while nobody is looking

        polling = true;
        var url = pollUrl + "?after=" + lastId + (partner ? "&with=" + partner : "");

        fetch(url, { headers: { "Accept": "application/json" }, credentials: "same-origin", cache: "no-store" })
            .then(function (res) {
                if (res.redirected || res.status === 401) {          // the session ended: go to the sign-in page
                    stopped = true;
                    window.location.reload();
                    throw new Error("signed-out");
                }
                if (!res.ok) throw new Error("http " + res.status);
                return res.json();
            })
            .then(function (data) { failures = 0; handlePoll(data); })
            .catch(function (err) { if (!err || err.message !== "signed-out") failures++; })
            .then(function () {
                polling = false;
                schedule(failures >= 3 ? SLOW_POLL_MS : POLL_MS);     // be gentle with the server when it keeps failing
            });
    }

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) { clearTimeout(timer); poll(); }
    });

    // ---------------------------------------------------------------- start

    scrollToBottom();
    applySearch();
    schedule(POLL_MS);
})();
