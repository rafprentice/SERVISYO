/* Book this Provider: shows the free start times for the chosen services and date, keeps the summary up to date,
   and sends the form once. The server checks everything again when the request is sent; this file only helps
   the customer choose. Times from the server are put on the page with textContent.
   Start times: the provider's exact shift start, then every :00 / :30; today (when the provider needs no notice) the
   first button is the soonest, the next whole minute. The list for today is asked for again every minute.
   "Enter exact time" shows an <input type="time">: any whole minute, checked by the server as the customer types. */
(function () {
    "use strict";

    var form = document.querySelector("[data-bp-form]");
    if (!form) return;

    var slotsUrl = form.getAttribute("data-slots-url");
    var checkUrl = form.getAttribute("data-check-url");
    var today = form.getAttribute("data-today") || "";
    var chosenTime = form.getAttribute("data-selected-time") || "";

    var exactToggle = form.querySelector("[data-bp-exact-toggle]");
    var exactBox = form.querySelector("[data-bp-exact]");
    var exactInput = form.querySelector("[data-bp-exact-input]");
    var exactHours = form.querySelector("[data-bp-exact-hours]");
    var exactMsg = form.querySelector("[data-bp-exact-msg]");
    var exactOk = null;          // the server's answer for the typed time: true, false, or null (not asked yet)
    var exactCounter = 0;
    var exactTimer = null;

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

    // "16:05" -> "4:05 PM"
    function ampm(hhmm) {
        var p = (hhmm || "").split(":"), h = parseInt(p[0], 10), m = p[1] || "00";
        if (isNaN(h)) return hhmm;
        return ((h % 12) || 12) + ":" + m + (h < 12 ? " AM" : " PM");
    }

    function exactMode() { return !!exactInput && !exactInput.disabled; }

    function currentTime() {
        if (exactMode()) return exactInput.value || "";
        var checked = slotBox.querySelector("input[type=radio]:checked");
        return checked ? checked.value : "";
    }

    // ---- "Enter exact time"
    function setExactMessage(text, kind) {
        if (!exactMsg) return;
        exactMsg.textContent = text || "";
        exactMsg.classList.toggle("is-error", kind === "error");
        exactMsg.classList.toggle("is-ok", kind === "ok");
        if (kind === "error") exactInput.setAttribute("aria-invalid", "true"); else exactInput.removeAttribute("aria-invalid");
    }

    function openExact(value, focus) {
        if (!exactBox) return;
        exactBox.hidden = false;
        exactInput.disabled = false;                         // now it is the one "Time" that is sent
        exactToggle.setAttribute("aria-expanded", "true");
        exactToggle.textContent = "Use the listed times";
        slotBox.querySelectorAll("input[type=radio]").forEach(function (r) { r.checked = false; });
        if (value) exactInput.value = value;
        if (focus) exactInput.focus();
        if (exactInput.value) checkExact(); else setExactMessage("", null);
    }

    function closeExact() {
        if (!exactBox) return;
        exactBox.hidden = true;
        exactInput.disabled = true;                          // a disabled field is not sent
        exactToggle.setAttribute("aria-expanded", "false");
        exactToggle.textContent = "Enter exact time";
        exactOk = null;
        setExactMessage("", null);
    }

    function checkExact() {
        clearTimeout(exactTimer);
        var value = exactInput.value, date = dateInput.value, ids = selectedIds();
        exactOk = null;
        if (!value) { setExactMessage("", null); return; }
        if (!date || ids.length === 0) { setExactMessage("Choose a service and a date first.", "error"); return; }
        var mine = ++exactCounter;
        exactTimer = setTimeout(function () {
            var params = new URLSearchParams();
            params.set("date", date);
            params.set("time", value);
            ids.forEach(function (id) { params.append("services", id); });
            setExactMessage("Checking…", null);
            fetch(checkUrl + "?" + params.toString(), { headers: { "Accept": "application/json" }, credentials: "same-origin", cache: "no-store" })
                .then(function (res) {
                    if (res.redirected || res.status === 401) { window.location.reload(); throw new Error("signed-out"); }
                    if (!res.ok) throw new Error("http " + res.status);
                    return res.json();
                })
                .then(function (data) {
                    if (mine !== exactCounter) return;
                    exactOk = !!data.ok;
                    setExactMessage(data.ok ? "✓ This time is available." : (data.message || "That time can't be booked."), data.ok ? "ok" : "error");
                })
                .catch(function (err) {
                    if (mine !== exactCounter || (err && err.message === "signed-out")) return;
                    exactOk = null;
                    setExactMessage("Could not check that time. It will be checked when you send the request.", null);
                });
        }, 300);
    }

    if (exactToggle) {
        exactToggle.addEventListener("click", function () {
            if (exactMode()) closeExact(); else openExact("", true);
        });
        exactInput.addEventListener("input", checkExact);
        exactInput.addEventListener("change", checkExact);
        // picking a button leaves "exact time"
        slotBox.addEventListener("change", function (e) {
            if (e.target && e.target.name === "Time" && e.target.type === "radio" && exactMode()) closeExact();
        });
    }

    // why there are no times right now (the server's reason, e.g. "The provider does not work on that day of the week.")
    var noTimesReason = "";

    // the working hours of the day under "Enter exact time"; the toggle only shows on a day the provider works
    function showDay(data) {
        if (!exactToggle) return;
        var works = !!(data && data.hours);
        exactToggle.hidden = !works;
        exactHours.textContent = works ? "Working hours that day: " + data.hours + ". Any whole minute that fits your services." : "";
        exactInput.min = data && data.earliest ? data.earliest : "";
        if (!works && exactMode()) closeExact();
        if (works && exactMode() && exactInput.value) checkExact();     // the date or the services changed
    }

    function clearSlots(message, data) {
        slotBox.textContent = "";
        slotMsg.textContent = message;
        slotMsg.classList.remove("is-error");
        slotMsg.hidden = false;
        noTimesReason = message;
        showDay(data);
    }

    // returns true when "keep" was one of the buttons (and is now checked again)
    function drawSlots(slots, keep, data) {
        slotBox.textContent = "";
        var found = false;
        slots.forEach(function (s) {
            var label = document.createElement("label");
            label.className = "sv-bp__slot";
            var input = document.createElement("input");
            input.type = "radio";
            input.name = "Time";
            input.value = s.value;
            if (s.value === keep && !exactMode()) { input.checked = true; found = true; }
            var span = document.createElement("span");
            span.textContent = s.text;
            if (s.soon) {
                var badge = document.createElement("small");
                badge.className = "sv-bp__soon";
                badge.textContent = "Soonest";
                span.appendChild(badge);
                input.setAttribute("aria-label", s.text + ", the soonest start");
            }
            label.appendChild(input);
            label.appendChild(span);
            slotBox.appendChild(label);
        });
        slotMsg.classList.remove("is-error");
        slotMsg.hidden = true;
        noTimesReason = "";
        showDay(data);
        return found;
    }

    function loadSlots(quiet) {
        var ids = selectedIds();
        var date = dateInput.value;

        if (ids.length === 0) { clearSlots("Choose a service and a date to see the available times."); return; }
        if (!date) { clearSlots("Choose a date to see the available times."); return; }

        var restoring = !!chosenTime;                         // the time sent before (the form came back with a problem)
        var picked = exactMode() ? "" : currentTime();
        var keep = picked || chosenTime;
        var mine = ++counter;
        var params = new URLSearchParams();
        params.set("date", date);
        ids.forEach(function (id) { params.append("services", id); });

        if (!quiet) {
            slotMsg.textContent = "Checking available times…";
            slotMsg.hidden = false;
        }

        fetch(slotsUrl + "?" + params.toString(), { headers: { "Accept": "application/json" }, credentials: "same-origin", cache: "no-store" })
            .then(function (res) {
                if (res.redirected || res.status === 401) { window.location.reload(); throw new Error("signed-out"); }
                if (!res.ok) throw new Error("http " + res.status);
                return res.json();
            })
            .then(function (data) {
                if (mine !== counter) return;                         // an older answer arrived late
                var list = data.slots || [];
                chosenTime = "";
                if (list.length === 0) {
                    clearSlots(data.message || "No free times on that date.", data);
                    if (restoring && keep && data.hours) openExact(keep, false);
                } else if (!drawSlots(list, keep, data) && keep) {
                    if (restoring) {
                        openExact(keep, false);                   // an exact time was sent: show it again
                    } else if (picked) {
                        slotMsg.textContent = "The time you picked (" + ampm(picked) + ") is no longer free or has passed. Please choose another start time.";
                        slotMsg.classList.add("is-error");
                        slotMsg.hidden = false;
                    }
                }
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

    // today the soonest start moves on every minute: ask for the list again (the customer's choice is kept)
    setInterval(function () {
        if (document.hidden || form.getAttribute("data-busy")) return;
        if (dateInput.value && dateInput.value === today && selectedIds().length > 0) loadSlots(true);
    }, 60000);

    // the service address: the browser's own "required" check, with SERVISYO's wording
    var address = form.querySelector("[data-bp-address]");
    if (address) {
        var addressMsg = address.getAttribute("data-required-msg") || "Please select a service address.";
        var checkAddress = function () { address.setCustomValidity(address.value ? "" : addressMsg); };
        address.addEventListener("change", checkAddress);
        checkAddress();
    }
    dateInput.addEventListener("change", scheduleLoad);

    // send the form once; a missing time is caught here too (the server still checks)
    form.addEventListener("submit", function (event) {
        if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
        if (exactMode() && (!exactInput.value || exactOk === false)) {
            event.preventDefault();
            if (!exactInput.value) setExactMessage("Enter a start time, or choose one of the listed times.", "error");
            exactInput.focus();
            return;
        }
        if (!currentTime()) {
            event.preventDefault();
            var hasTimes = !!slotBox.querySelector("input[type=radio]");
            // keep the reason when there are no times at all, so the customer knows to pick another date
            slotMsg.textContent = hasTimes
                ? "Please choose a start time."
                : (noTimesReason ? noTimesReason + " " : "") + "There is no free start time for this date. Please choose another date.";
            slotMsg.classList.add("is-error");
            slotMsg.hidden = false;
            var target = hasTimes ? slotBox : dateInput;
            if (target.scrollIntoView) target.scrollIntoView({ behavior: "smooth", block: "center" });
            if (!hasTimes && dateInput.focus) dateInput.focus({ preventScroll: true });
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
