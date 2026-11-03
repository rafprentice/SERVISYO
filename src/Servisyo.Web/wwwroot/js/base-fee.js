/* Base fee boxes (provider registration and Provider > My Profile): <input type="number" data-sv-basefee>.
   A base fee must be at least 1 PHP and at most 100,000 PHP, so 0 and negative fees can never be sent.
   The message appears under the box (the element named by aria-describedby). The server checks again
   (BaseFeeRules), because a request can skip this page. */
(function () {
    "use strict";

    var DEFAULTS = {
        required: "Please enter your base fee.",
        min: "Base fee must be at least 1 PHP.",
        max: "Base fee can be at most 100,000 PHP.",
        nan: "Enter the base fee as a number, for example 500."
    };

    function msg(box, kind) {
        return box.getAttribute("data-msg-" + kind) || DEFAULTS[kind];
    }

    // "" when the fee is fine, otherwise the message to show
    function problemOf(box) {
        if (box.validity && box.validity.badInput) return msg(box, "nan");   // e.g. "1..5": the browser gives value ""
        var raw = (box.value || "").trim();
        if (raw === "") return msg(box, "required");
        var fee = Number(raw);
        if (!isFinite(fee)) return msg(box, "nan");
        var min = Number(box.getAttribute("min") || 1);
        var max = Number(box.getAttribute("max") || 100000);
        if (fee <= 0 || fee < min) return msg(box, "min");
        if (fee > max) return msg(box, "max");
        return "";
    }

    function messageBox(box) {
        var id = box.getAttribute("aria-describedby");
        return id ? document.getElementById(id) : null;
    }

    function show(box, text) {
        var out = messageBox(box);
        if (text) box.setAttribute("aria-invalid", "true");
        else box.removeAttribute("aria-invalid");
        if (!out) return;
        out.textContent = text;
        out.hidden = !text;
        // the registration page's message box is also used by ASP.NET's jQuery validation
        if (out.hasAttribute("data-valmsg-for")) {
            out.classList.toggle("field-validation-error", !!text);
            out.classList.toggle("field-validation-valid", !text);
        }
    }

    document.querySelectorAll("input[data-sv-basefee]").forEach(function (box) {
        var form = box.form;

        // type=number still accepts "e", "+" and "-" (1e5, -500): block them
        box.addEventListener("keydown", function (e) {
            if (["e", "E", "+", "-"].indexOf(e.key) >= 0) e.preventDefault();
        });
        box.addEventListener("paste", function (e) {
            var text = (e.clipboardData || window.clipboardData).getData("text").trim();
            if (!/^\d+(\.\d+)?$/.test(text)) e.preventDefault();
        });

        // once a message is showing, update it while typing so it disappears as soon as the fee is fixed
        box.addEventListener("input", function () {
            if (box.getAttribute("aria-invalid") === "true") show(box, problemOf(box));
        });
        box.addEventListener("change", function () { show(box, problemOf(box)); });

        // forms without "novalidate" (My Profile): the browser's own min="1" check would show a bubble; show the message under the box instead
        box.addEventListener("invalid", function (e) {
            var problem = problemOf(box);
            if (!problem) return;
            e.preventDefault();
            show(box, problem);
            box.focus();
        });

        // forms with "novalidate" (registration, where jQuery validation runs): stop the submit here.
        // Registered in the capture phase, so it runs before the page's other submit handlers (they check event.defaultPrevented).
        if (form) {
            form.addEventListener("submit", function (e) {
                var problem = problemOf(box);
                show(box, problem);
                if (!problem) return;
                e.preventDefault();
                if (box.scrollIntoView) box.scrollIntoView({ behavior: "smooth", block: "center" });
                box.focus({ preventScroll: true });
            }, true);
        }

        // the server sent the page back with a base fee problem: keep its message, mark the box
        var out = messageBox(box);
        if (out && out.textContent.trim()) box.setAttribute("aria-invalid", "true");
    });
})();
