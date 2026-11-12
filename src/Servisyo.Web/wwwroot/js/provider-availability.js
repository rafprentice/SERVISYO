/* Provider > Availability: switches, the 1-hour break, "Copy Mon -> Weekdays", the settings, and the
   "Request Day Off" dialog (database v2.7: 1 to 7 days, from tomorrow, this year, one provider per date).
   Everything is only a convenience: the server checks every value again
   (BreakRules and UnavailableDateRules in ProviderAvailabilityViewModels.cs). */
(function () {
    "use strict";

    var form = document.getElementById("sv-av-form");
    if (!form) return;

    var daysList = document.getElementById("sv-av-days");
    var breakTpl = document.getElementById("sv-break-tpl");

    // the break rules (the same numbers as BreakRules on the server)
    var MIN_SHIFT = 8 * 60, BREAK = 60, STEP = 30;
    var SHORT_WARN = "This break is removed when you save: the shift is shorter than 8 hours.";

    function days() { return Array.prototype.slice.call(daysList.querySelectorAll(".sv-av__day")); }

    // ---------- time helpers: "13:30" <-> 810 minutes, 810 -> "1:30 pm" ----------
    function toMinutes(value) {
        if (!value) return null;
        var parts = value.split(":");
        return Number(parts[0]) * 60 + Number(parts[1]);
    }
    function pad(n) { return (n < 10 ? "0" : "") + n; }
    function hhmm(m) { return pad(Math.floor(m / 60)) + ":" + pad(m % 60); }
    function clock(m) {
        var h = Math.floor(m / 60), h12 = h % 12 === 0 ? 12 : h % 12;
        return h12 + ":" + pad(m % 60) + (h < 12 ? " am" : " pm");
    }

    // ---------- names: the server reads Days[i].Breaks[j].Start, so they are renumbered after every change ----------
    function renumber() {
        days().forEach(function (li) {
            var i = li.dataset.index;
            li.querySelectorAll("[data-break]").forEach(function (row, j) {
                row.querySelector("[data-b-start]").name = "Days[" + i + "].Breaks[" + j + "].Start";
            });
        });
    }

    // ---------- the break of one day ----------
    function setWarning(row, kind, text) {
        var warn = row.querySelector("[data-b-warn]");
        if (!text) { if (warn && (!kind || warn.getAttribute("data-b-warn") === kind)) warn.remove(); return; }
        if (!warn) {
            warn = document.createElement("p");
            warn.className = "sv-av__breakwarn";
            row.appendChild(warn);
        }
        warn.setAttribute("data-b-warn", kind);
        warn.textContent = text;
    }

    /// Re-reads the day's hours: enables or disables "+ Add break" and rebuilds the start-time dropdown
    /// (every 30 minutes from the start up to the end minus 1 hour), keeping the chosen time or the nearest one.
    function refreshDay(li) {
        var start = toMinutes(li.querySelector("[data-start]").value);
        var end = toMinutes(li.querySelector("[data-end]").value);
        var allows = start !== null && end !== null && end - start >= MIN_SHIFT;
        var rows = li.querySelectorAll("[data-break]");

        li.querySelector("[data-break-note]").hidden = allows;
        li.querySelector("[data-add-break]").disabled = !allows || rows.length >= 1;

        rows.forEach(function (row) {
            var select = row.querySelector("[data-b-start]");
            row.classList.toggle("is-disabled", !allows);
            select.disabled = !allows;                              // a disabled box is not sent: the break is dropped
            if (!allows) {
                // keep showing the chosen time, greyed out, so it comes back if the shift gets long enough again
                var saved = toMinutes(select.value) !== null ? toMinutes(select.value) : toMinutes(select.getAttribute("data-saved") || "12:00");
                select.innerHTML = "";
                select.add(new Option(clock(saved), hhmm(saved), true, true));
                row.querySelector("[data-b-end]").textContent = clock(saved + BREAK);
                setWarning(row, "short", SHORT_WARN);
                return;
            }
            setWarning(row, "short", null);

            var choices = [];
            for (var m = start; m + BREAK <= end; m += STEP) choices.push(m);

            var wanted = toMinutes(select.value) !== null ? toMinutes(select.value) : toMinutes(select.getAttribute("data-saved") || "12:00");
            var keep = choices.indexOf(wanted) >= 0 ? wanted : choices.reduce(function (best, c) {
                return Math.abs(c - wanted) < Math.abs(best - wanted) ? c : best;
            }, choices[0]);

            select.innerHTML = "";
            choices.forEach(function (c) {
                var option = new Option(clock(c), hhmm(c), false, c === keep);
                option.defaultSelected = c === keep;
                select.add(option);
            });
            select.value = hhmm(keep);
            select.setAttribute("data-saved", hhmm(keep));
            row.querySelector("[data-b-end]").textContent = clock(keep + BREAK);
        });
    }

    function addBreak(li, start) {
        var node = breakTpl.content.firstElementChild.cloneNode(true);
        node.querySelector("[data-b-start]").setAttribute("data-saved", start || "12:00");
        li.querySelector("[data-breaks]").appendChild(node);
        renumber();
        refreshDay(li);
        return node;
    }

    // ---------- weekly schedule ----------
    function onHoursChanged(e) {
        var li = e.target.closest(".sv-av__day");
        if (!li) return;
        if (e.target.matches("[data-day-toggle]")) li.classList.toggle("is-off", !e.target.checked);
        if (e.target.matches("[data-start], [data-end], [data-day-toggle]")) refreshDay(li);
        if (e.target.matches("[data-b-start]")) {
            var row = e.target.closest("[data-break]");
            row.querySelector("[data-b-end]").textContent = clock(toMinutes(e.target.value) + BREAK);
            e.target.setAttribute("data-saved", e.target.value);
            setWarning(row, "moved", null);                        // the provider chose a time: the note is no longer needed
        }
    }
    daysList.addEventListener("change", onHoursChanged);
    daysList.addEventListener("input", onHoursChanged);

    daysList.addEventListener("click", function (e) {
        var add = e.target.closest("[data-add-break]");
        if (add) {
            if (add.disabled) return;
            var node = addBreak(add.closest(".sv-av__day"));
            node.querySelector("[data-b-start]").focus();
            return;
        }
        var del = e.target.closest("[data-remove-break]");
        if (del) {
            var li = del.closest(".sv-av__day");
            del.closest("[data-break]").remove();
            renumber();
            refreshDay(li);
        }
    });

    // Copy Mon -> Weekdays: Monday's switch, hours and break are copied to Tuesday to Friday
    document.getElementById("sv-av-copy").addEventListener("click", function () {
        var all = days();
        var monday = all.filter(function (li) { return li.dataset.dow === "1"; })[0];
        if (!monday) return;

        var on = monday.querySelector("[data-day-toggle]").checked;
        var start = monday.querySelector("[data-start]").value;
        var end = monday.querySelector("[data-end]").value;
        var breaks = Array.prototype.map.call(monday.querySelectorAll("[data-break]"), function (row) {
            return row.querySelector("[data-b-start]").value;
        });

        all.forEach(function (li) {
            var dow = Number(li.dataset.dow);
            if (dow < 2 || dow > 5) return;                       // Tuesday (2) to Friday (5)
            var toggle = li.querySelector("[data-day-toggle]");
            toggle.checked = on;
            li.classList.toggle("is-off", !on);
            li.querySelector("[data-start]").value = start;
            li.querySelector("[data-end]").value = end;
            li.querySelectorAll("[data-break]").forEach(function (row) { row.remove(); });
            breaks.forEach(function (b) { addBreak(li, b); });
            refreshDay(li);
        });
        renumber();
    });

    // ---------- booking settings ----------
    var notice = document.getElementById("sv-av-notice");
    var noticeHint = document.getElementById("sv-av-notice-hint");
    notice.addEventListener("change", function () {
        var n = Number(notice.value);
        noticeHint.textContent = n === 0
            ? "Customers can book for the same day."
            : "Customers must book at least " + n + (n === 1 ? " day" : " days") + " in advance.";
    });

    var max = document.getElementById("sv-av-max");
    var maxHint = document.getElementById("sv-av-max-hint");
    function updateMaxHint() {
        var n = Number(max.value) || 0;
        maxHint.textContent = "Max " + n + (n === 1 ? " job" : " jobs") + " will be accepted per day.";
    }
    form.querySelectorAll("[data-step]").forEach(function (button) {
        button.addEventListener("click", function () {
            var lo = Number(max.min) || 1, hi = Number(max.max) || 20;
            var next = (Number(max.value) || lo) + Number(button.dataset.step);
            max.value = Math.min(hi, Math.max(lo, next));
            updateMaxHint();
        });
    });
    max.addEventListener("input", updateMaxHint);

    // ---------- submit ----------
    var saveButton = document.getElementById("sv-av-save");
    form.addEventListener("submit", function () {
        renumber();
        saveButton.disabled = true;                                // no double submit
    });

    renumber();
    days().forEach(refreshDay);

    // ---------- "Remove" / "Cancel request": ask first ----------
    document.addEventListener("submit", function (e) {
        var button = e.submitter || e.target.querySelector("[data-confirm]");
        if (!button || !button.hasAttribute("data-confirm")) return;
        if (!window.confirm(button.getAttribute("data-confirm"))) { e.preventDefault(); return; }
        button.disabled = true;
    });

    // ---------- "Request Day Off" ----------
    // The dialog opens ONLY when the button is pressed (or when the server refused a request, see the end).
    // The Unavailable Dates section can be redrawn live, so its button is looked up at click time.
    var dialog = document.getElementById("sv-av-dayoff");
    var dayOff = document.getElementById("sv-av-dayoff-form");
    if (!dialog || !dayOff) return;

    var startBox = document.getElementById("sv-av-do-start");
    var endBox = document.getElementById("sv-av-do-end");
    var summary = document.getElementById("sv-av-do-sum");
    var problem = document.getElementById("sv-av-do-error");
    var sendButton = document.getElementById("sv-av-do-send");

    var today = dayOff.getAttribute("data-today");                     // "2026-11-12" (Philippine time)
    var lastDay = dayOff.getAttribute("data-last");                    // "2026-12-31"
    var maxDays = Number(dayOff.getAttribute("data-max-days")) || 7;
    var maxDaysMessage = dayOff.getAttribute("data-max-days-message"); // "Maximum Day Off is 1 week or 7 days"
    var sameDayMessage = dayOff.getAttribute("data-same-day-message");
    var takenMessage = dayOff.getAttribute("data-taken-message");

    // days ANOTHER provider already has off or asked for: only one provider can be off per day. They are read from
    // the section each time, because the live refresh brings the newest list.
    function takenDays() {
        var holder = document.getElementById("sv-av-daysoff") || dayOff;
        return (holder.getAttribute("data-taken") || "").split(",").filter(Boolean);
    }

    function dateLabel(value) {
        return new Date(value + "T00:00:00Z").toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
    }
    function addDays(value, n) {
        var d = new Date(value + "T00:00:00Z");
        d.setUTCDate(d.getUTCDate() + n);
        return d.toISOString().slice(0, 10);
    }
    function daysBetween(a, b) { return Math.round((new Date(b + "T00:00:00Z") - new Date(a + "T00:00:00Z")) / 86400000); }

    /// The first problem with the chosen dates ("" when there is none). The same rules as the server.
    function findProblem() {
        var start = startBox.value, end = endBox.value || start;
        if (!start) return "Please choose the first day off.";
        if (end < start) return "The last day off cannot be before the first day.";
        if (start < today) return dateLabel(start) + " is in the past. Choose tomorrow or a later date.";
        if (start === today) return sameDayMessage;
        if (end > lastDay) return dateLabel(end) + " is not in " + lastDay.slice(0, 4) + ". You can only request days off until December 31, " + lastDay.slice(0, 4) + ".";
        if (daysBetween(start, end) + 1 > maxDays) return maxDaysMessage;
        var taken = takenDays();
        for (var d = start; d <= end; d = addDays(d, 1)) {
            if (taken.indexOf(d) >= 0) return dateLabel(d) + ": " + takenMessage;
        }
        return "";
    }

    function markInvalid(box, wrong) {
        if (wrong) box.setAttribute("aria-invalid", "true"); else box.removeAttribute("aria-invalid");
    }

    function refresh(show) {
        var start = startBox.value, end = endBox.value || start;
        // the end date can be at most 6 days after the start (7 days in all), and not after December 31
        endBox.min = start || dayOff.getAttribute("data-first");
        endBox.max = start && addDays(start, maxDays - 1) < lastDay ? addDays(start, maxDays - 1) : lastDay;

        var text = findProblem();
        summary.textContent = !text && start
            ? (start === end ? "1 day: " + dateLabel(start) : (daysBetween(start, end) + 1) + " days: " + dateLabel(start) + " to " + dateLabel(end))
            : "";
        if (show) {
            problem.textContent = text;
            problem.hidden = !text;
            // the box to fix: the end date for "too many days", "before the start" or "after December 31", otherwise the start
            var endIsWrong = !!text && !!endBox.value && (text === maxDaysMessage || endBox.value < start || endBox.value > lastDay);
            markInvalid(startBox, !!text && !endIsWrong);
            markInvalid(endBox, endIsWrong);
        }
        return !text;
    }

    function openDialog(opener) {
        if (opener.getAttribute("aria-disabled") === "true") {
            var note = document.getElementById("sv-av-waiting");
            if (note) note.scrollIntoView({ block: "center" });
            return;
        }
        if (typeof dialog.showModal === "function") dialog.showModal(); else dialog.setAttribute("open", "");
        refresh(false);
        startBox.focus();
    }
    function closeDialog() { if (typeof dialog.close === "function") dialog.close(); else dialog.removeAttribute("open"); }

    document.addEventListener("click", function (e) {
        var opener = e.target.closest("#sv-av-request");
        if (opener) openDialog(opener);
    });
    dialog.addEventListener("click", function (e) {
        if (e.target === dialog || e.target.closest("[data-sv-close]")) closeDialog();   // the dark backdrop or "Cancel"
    });
    startBox.addEventListener("change", function () { refresh(true); });
    endBox.addEventListener("change", function () { refresh(true); });
    startBox.addEventListener("input", function () { refresh(false); });
    endBox.addEventListener("input", function () { refresh(false); });

    dayOff.addEventListener("submit", function (e) {
        if (!refresh(true)) { e.preventDefault(); return; }           // the server would refuse it: say so right away
        sendButton.disabled = true;                                   // no double submit
    });

    // the server refused the request: open the dialog again with what was typed. Only data-open="true" counts
    // (Razor always writes data-* attributes, so the attribute is there on every visit).
    if (dialog.getAttribute("data-open") === "true") {
        if (typeof dialog.showModal === "function") dialog.showModal(); else dialog.setAttribute("open", "");
        refresh(true);
    }

    // ---------- live (database v2.7): an administrator approved or rejected a request ----------
    // live-bookings.js turns the SignalR push into "sv:dayoff-changed"; the section is fetched again from the server.
    function refreshSection() {
        var section = document.getElementById("sv-av-daysoff");
        if (!section || !window.fetch || !window.DOMParser) return;
        fetch(section.getAttribute("data-refresh-url"), { credentials: "same-origin", cache: "no-store", headers: { "X-Requested-With": "fetch" } })
            .then(function (response) { return response.ok && !response.redirected ? response.text() : null; })
            .then(function (html) {
                if (!html) return;
                var fresh = new DOMParser().parseFromString(html, "text/html").getElementById("sv-av-daysoff");
                var current = document.getElementById("sv-av-daysoff");
                if (!fresh || !current || current.querySelector("button[disabled]")) return;   // a Cancel / Remove is being sent
                current.setAttribute("data-taken", fresh.getAttribute("data-taken") || "");
                if (fresh.innerHTML === current.innerHTML) return;
                current.innerHTML = fresh.innerHTML;
                current.classList.remove("sv-live-flash");
                void current.offsetWidth;                             // restart the highlight
                current.classList.add("sv-live-flash");
            })
            .catch(function () { });
    }
    document.addEventListener("sv:dayoff-changed", refreshSection);

    // If the live connection is not working (e.g. the SignalR script could not load), ask every 30 seconds instead.
    setInterval(function () {
        if (!document.hidden && document.documentElement.getAttribute("data-sv-live-connected") !== "true") refreshSection();
    }, 30000);
})();
