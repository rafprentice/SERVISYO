/* Provider > Availability: switches, breaks, "Copy Mon -> Weekdays", unavailable dates and the settings.
   Everything is only a convenience: the server checks every value again when "Save Schedule" is pressed. */
(function () {
    "use strict";

    var form = document.getElementById("sv-av-form");
    if (!form) return;

    var daysList = document.getElementById("sv-av-days");
    var breakTpl = document.getElementById("sv-break-tpl");
    var dateTpl = document.getElementById("sv-date-tpl");
    var datesList = document.getElementById("sv-av-dates");
    var datesEmpty = document.getElementById("sv-av-dates-empty");

    function days() { return Array.prototype.slice.call(daysList.querySelectorAll(".sv-av__day")); }

    // ---------- names: the server reads Days[i].Breaks[j] and Dates[k], so they are renumbered after every change ----------
    function renumber() {
        days().forEach(function (li) {
            var i = li.dataset.index;
            li.querySelectorAll("[data-break]").forEach(function (row, j) {
                row.querySelector("[data-b-start]").name = "Days[" + i + "].Breaks[" + j + "].Start";
                row.querySelector("[data-b-end]").name = "Days[" + i + "].Breaks[" + j + "].End";
            });
        });
        datesList.querySelectorAll("[data-date-row]").forEach(function (row, k) {
            row.querySelector("[data-d-date]").name = "Dates[" + k + "].Date";
            row.querySelector("[data-d-reason]").name = "Dates[" + k + "].Reason";
        });
    }

    // ---------- weekly schedule ----------
    daysList.addEventListener("change", function (e) {
        if (e.target.matches("[data-day-toggle]")) {
            e.target.closest(".sv-av__day").classList.toggle("is-off", !e.target.checked);
        }
    });

    function addBreak(li, start, end) {
        var node = breakTpl.content.firstElementChild.cloneNode(true);
        if (start) node.querySelector("[data-b-start]").value = start;
        if (end) node.querySelector("[data-b-end]").value = end;
        li.querySelector("[data-breaks]").appendChild(node);
        renumber();
        return node;
    }

    daysList.addEventListener("click", function (e) {
        var add = e.target.closest("[data-add-break]");
        if (add) {
            var node = addBreak(add.closest(".sv-av__day"));
            node.querySelector("[data-b-start]").focus();
            return;
        }
        var del = e.target.closest("[data-remove-break]");
        if (del) {
            del.closest("[data-break]").remove();
            renumber();
        }
    });

    // Copy Mon -> Weekdays: Monday's switch, hours and breaks are copied to Tuesday to Friday
    document.getElementById("sv-av-copy").addEventListener("click", function () {
        var all = days();
        var monday = all.filter(function (li) { return li.dataset.dow === "1"; })[0];
        if (!monday) return;

        var on = monday.querySelector("[data-day-toggle]").checked;
        var start = monday.querySelector("[data-start]").value;
        var end = monday.querySelector("[data-end]").value;
        var breaks = Array.prototype.map.call(monday.querySelectorAll("[data-break]"), function (row) {
            return [row.querySelector("[data-b-start]").value, row.querySelector("[data-b-end]").value];
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
            breaks.forEach(function (b) { addBreak(li, b[0], b[1]); });
        });
        renumber();
    });

    // ---------- unavailable dates ----------
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

    datesList.addEventListener("click", function (e) {
        var del = e.target.closest("[data-remove-date]");
        if (del) {
            del.closest("[data-date-row]").remove();
            renumber();
            refreshEmpty();
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
    form.addEventListener("submit", function () {
        renumber();
        document.getElementById("sv-av-save").disabled = true;    // no double submit
    });

    renumber();
})();
