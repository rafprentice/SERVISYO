/* Book this Provider: shows the free start times for the chosen services and date, keeps the summary up to date,
   and sends the form once. The server checks everything again when the request is sent; this file only helps
   the customer choose. Times from the server are put on the page with textContent. */
(function () {
    "use strict";

    var form = document.querySelector("[data-bp-form]");
    if (!form) return;

    var slotsUrl = form.getAttribute("data-slots-url");
    var chosenTime = form.getAttribute("data-selected-time") || "";

    var services = form.querySelectorAll("[data-bp-service]");
    var dateInput = form.querySelector("[data-bp-date]");
    var slotBox = form.querySelector("[data-bp-slots]");
    var slotMsg = form.querySelector("[data-bp-slotmsg]");
    var submitBtn = form.querySelector("[data-bp-submit]");

    var sumCount = document.querySelector("[data-bp-count]");
    var sumDuration = document.querySelector("[data-bp-duration]");
    var sumTotal = document.querySelector("[data-bp-total]");

    var counter = 0;          // only the newest answer from the server is used
    var timer = null;

    function peso(n) {
        var whole = Math.round(n * 100) % 100 === 0;
        return "₱" + n.toLocaleString("en-PH", { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: 2 });
    }

    function durationText(min) {
        if (min <= 0) return "—";
        if (min < 60) return min + " min";
        var h = Math.floor(min / 60), m = min % 60;
        return h + (h === 1 ? " hr" : " hrs") + (m ? " " + m + " min" : "");
    }

    function selectedIds() {
        var ids = [];
        services.forEach(function (c) { if (c.checked) ids.push(c.value); });
        return ids;
    }

    function updateSummary() {
        var count = 0, minutes = 0, total = 0;
        services.forEach(function (c) {
            if (!c.checked) return;
            count++;
            minutes += parseInt(c.getAttribute("data-minutes"), 10) || 0;
            total += parseFloat(c.getAttribute("data-price")) || 0;
        });
        if (sumCount) sumCount.textContent = String(count);
        if (sumDuration) sumDuration.textContent = durationText(minutes);
        if (sumTotal) sumTotal.textContent = peso(total);
    }

    function currentTime() {
        var checked = slotBox.querySelector("input[type=radio]:checked");
        return checked ? checked.value : "";
    }

    function clearSlots(message) {
        slotBox.textContent = "";
        slotMsg.textContent = message;
        slotMsg.hidden = false;
    }

    function drawSlots(slots, keep) {
        slotBox.textContent = "";
        slots.forEach(function (s) {
            var label = document.createElement("label");
            label.className = "sv-bp__slot";
            var input = document.createElement("input");
            input.type = "radio";
            input.name = "Time";
            input.value = s.value;
            if (s.value === keep) input.checked = true;
            var span = document.createElement("span");
            span.textContent = s.text;
            label.appendChild(input);
            label.appendChild(span);
            slotBox.appendChild(label);
        });
        slotMsg.hidden = true;
    }

    function loadSlots() {
        var ids = selectedIds();
        var date = dateInput.value;

        if (ids.length === 0) { clearSlots("Choose a service and a date to see the available times."); return; }
        if (!date) { clearSlots("Choose a date to see the available times."); return; }

        var keep = currentTime() || chosenTime;
        var mine = ++counter;
        var params = new URLSearchParams();
        params.set("date", date);
        ids.forEach(function (id) { params.append("services", id); });

        slotMsg.textContent = "Checking available times…";
        slotMsg.hidden = false;

        fetch(slotsUrl + "?" + params.toString(), { headers: { "Accept": "application/json" }, credentials: "same-origin", cache: "no-store" })
            .then(function (res) {
                if (res.redirected || res.status === 401) { window.location.reload(); throw new Error("signed-out"); }
                if (!res.ok) throw new Error("http " + res.status);
                return res.json();
            })
            .then(function (data) {
                if (mine !== counter) return;                         // an older answer arrived late
                var list = data.slots || [];
                if (list.length === 0) { clearSlots(data.message || "No free times on that date."); return; }
                drawSlots(list, keep);
                chosenTime = "";
            })
            .catch(function (err) {
                if (mine !== counter || (err && err.message === "signed-out")) return;
                clearSlots("Could not load the available times. Check your connection and try again.");
            });
    }

    function scheduleLoad() {
        clearTimeout(timer);
        timer = setTimeout(loadSlots, 200);
    }

    services.forEach(function (c) {
        c.addEventListener("change", function () { updateSummary(); scheduleLoad(); });
    });
    dateInput.addEventListener("change", scheduleLoad);

    // send the form once; a missing time is caught here too (the server still checks)
    form.addEventListener("submit", function (event) {
        if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
        if (!currentTime()) {
            event.preventDefault();
            slotMsg.textContent = "Please choose a start time.";
            slotMsg.hidden = false;
            return;
        }
        form.setAttribute("data-busy", "1");
        form.classList.add("is-busy");
    });

    window.addEventListener("pageshow", function (event) {
        if (!event.persisted) return;
        form.removeAttribute("data-busy");
        form.classList.remove("is-busy");
    });

    updateSummary();
    if (selectedIds().length > 0 && dateInput.value) loadSlots();

    var firstBad = form.querySelector("[aria-invalid='true']");
    if (firstBad && typeof firstBad.focus === "function") firstBad.focus();
})();
