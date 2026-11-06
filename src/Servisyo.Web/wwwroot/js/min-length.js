/* Text boxes with data-sv-minlen="N": when something is typed it must be at least N characters
   (spaces at the ends do not count). Blank is left to the page's "required" check.
   Used by the Middle name box on provider registration: "C" or "M" is refused, "Castro" is fine.
   The message goes into the box's ASP.NET validation message (data-valmsg-for). The server checks again (AuthService). */
(function () {
    "use strict";

    document.querySelectorAll("input[data-sv-minlen]").forEach(function (box) {
        var min = parseInt(box.getAttribute("data-sv-minlen"), 10) || 0;
        var text = box.getAttribute("data-sv-minlen-msg") || ("Please enter at least " + min + " characters.");
        var form = box.form;
        var out = form ? form.querySelector('[data-valmsg-for="' + box.name + '"]') : null;

        function problem() {
            var value = (box.value || "").trim();
            return value.length > 0 && value.length < min ? text : "";
        }

        function show(message) {
            if (message) {
                box.setAttribute("aria-invalid", "true");
                if (out) {
                    out.textContent = message;
                    out.classList.add("field-validation-error");
                    out.classList.remove("field-validation-valid");
                }
            } else if (out && out.textContent === text) {
                // only clear OUR message; another problem (e.g. numbers in the name) is left for jQuery validation
                out.textContent = "";
                out.classList.remove("field-validation-error");
                out.classList.add("field-validation-valid");
                box.removeAttribute("aria-invalid");
            }
        }

        box.addEventListener("input", function () { if (out && out.textContent === text) show(problem()); });
        box.addEventListener("change", function () { show(problem()); });

        // capture phase: runs before the page's other submit handlers
        if (form) {
            form.addEventListener("submit", function (e) {
                var message = problem();
                if (!message) return;
                e.preventDefault();
                show(message);
                if (box.scrollIntoView) box.scrollIntoView({ behavior: "smooth", block: "center" });
                box.focus({ preventScroll: true });
            }, true);
        }
    });
})();
