/* Provider > Availability: switches, the 1-hour break, "Copy Mon -> Weekdays", unavailable dates and the settings.
   Everything is only a convenience: the server checks every value again when "Save Schedule" is pressed
   (BreakRules and UnavailableDateRules in ProviderAvailabilityViewModels.cs). */
(function () {
    "use strict";

    var form = document.getElementById("sv-av-form");
    if (!form) return;

    var daysList = document.getElementById("sv-av-days");
    var breakTpl = document.getElementById("sv-break-tpl");
    var dateTpl = document.getElementById("sv-date-tpl");
    var datesList = document.getElementById("sv-av-dates");
    var datesEmpty = document.getElementById("sv-av-dates-empty");
    var datesError = document.getElementById("sv-av-dates-error");

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

    // ---------- names: the server reads Days[i].Breaks[j].Start and Dates[k], so they are renumbered after every change ----------
    function renumber() {
        days().forEach(function (li) {
            var i = li.dataset.index;
            li.querySelectorAll("[data-break]").forEach(function (row, j) {
                row.querySelector("[data-b-start]").name = "Days[" + i + "].Breaks[" + j + "].Start";
            });
        });
        datesList.querySelectorAll("[data-date-row]").forEach(function (row, k) {
            row.querySelector("[data-d-date]").name = "Dates[" + k + "].Date";
            row.querySelector("[data-d-reason]").name = "Dates[" + k + "].Reason";
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

    // ---------- unavailable dates: this year only, at most 5 a month ----------
    var today = datesList.getAttribute("data-today");                  // "2026-11-11" (Philippine time)
    var lastDay = datesList.getAttribute("data-last");                 // "2026-12-31"
    var pastThisMonth = Number(datesList.getAttribute("data-past-this-month")) || 0;
    var maxPerMonth = Number(datesList.getAttribute("data-max-per-month")) || 5;
    var monthMessage = datesList.getAttribute("data-month-message");

    function dateLabel(value) {
        return new Date(value + "T00:00:00Z").toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
    }
    function monthLabel(key) {
        return new Date(key + "-01T00:00:00Z").toLocaleDateString("en-US", { month: "long", year: "numeric", timeZone: "UTC" });
    }

    /// Shows the problems with the listed dates (none: the message is hidden). Returns true when there are none.
    function checkDates() {
        var problems = [], perMonth = {}, year = lastDay.slice(0, 4);
        datesList.querySelectorAll("[data-d-date]").forEach(function (input) {
            input.removeAttribute("aria-invalid");
            var value = input.value;
            if (!value) return;
            if (value > lastDay) {
                problems.push(dateLabel(value) + " is not in " + year + ". You can only mark unavailable days until December 31, " + year + ".");
                input.setAttribute("aria-invalid", "true");
                return;
            }
            if (input.closest("[data-date-row]").hasAttribute("data-approved")) return;   // an extra day approved by an administrator
            var key = value.slice(0, 7);
            (perMonth[key] = perMonth[key] || []).push(input);
        });
        Object.keys(perMonth).sort().forEach(function (key) {
            var past = key === today.slice(0, 7) ? pastThisMonth : 0;
            var total = perMonth[key].length + past;
            if (total <= maxPerMonth) return;
            problems.push(monthMessage + " " + monthLabel(key) + " would have " + total +
                (past > 0 ? " (" + past + " of them already passed)." : "."));
            perMonth[key].forEach(function (input) { input.setAttribute("aria-invalid", "true"); });
        });
        datesError.textContent = problems.join(" ");
        datesError.hidden = problems.length === 0;
        return problems.length === 0;
    }

    function refreshEmpty() {
        var any = datesList.querySelector("[data-date-row]") !== null;
        datesEmpty.hidden = any;
    }

    document.getElementById("sv-av-add-date").addEventListener("click", function () {
        var node = dateTpl.content.firstElementChild.cloneNode(true);
        datesList.appendChild(node);
        renumber();
        refreshEmpty();
        node.querySelector("[data-d-date]").focus();
    });

    datesList.addEventListener("change", function (e) { if (e.target.matches("[data-d-date]")) checkDates(); });

    datesList.addEventListener("click", function (e) {
        var del = e.target.closest("[data-remove-date]");
        if (del) {
            del.closest("[data-date-row]").remove();
            renumber();
            refreshEmpty();
            checkDates();
        }
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
            var lo = Number(max.min) || 1, hi = Number(max.max) || 50;
            var next = (Number(max.value) || lo) + Number(button.dataset.step);
            max.value = Math.min(hi, Math.max(lo, next));
            updateMaxHint();
        });
    });
    max.addEventListener("input", updateMaxHint);

    // ---------- submit ----------
    var saveButton = document.getElementById("sv-av-save");
    form.addEventListener("submit", function (e) {
        if (!checkDates()) {                                       // the server would refuse it: say so right away
            e.preventDefault();
            datesError.scrollIntoView({ block: "center" });
            return;
        }
        renumber();
        saveButton.disabled = true;                                // no double submit
    });

    renumber();
    days().forEach(refreshDay);
    checkDates();
})();
