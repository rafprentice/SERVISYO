/* Show / hide password: the eye button in a password box (Views/Shared/_PasswordToggle.cshtml).
   Used on the sign-in, registration and reset pages and on Customer > My Profile > Security.
   - Each box has its own button: showing New Password never shows Current Password.
   - When the form is sent, or reset (Cancel), every box in it is hidden again.
   - The buttons are type="button", so clicking one never sends the form.
   Showing a password only changes what the screen shows; nothing is sent or saved differently. */
(function () {
    "use strict";

    function boxOf(button) {
        return document.getElementById(button.getAttribute("data-toggle-password"));
    }

    function setShown(button, box, shown) {
        box.type = shown ? "text" : "password";
        button.setAttribute("aria-pressed", shown ? "true" : "false");
        button.setAttribute("title", shown ? "Hide password" : "Show password");
    }

    function hideAll(container) {
        if (!container || !container.querySelectorAll) return;
        container.querySelectorAll("[data-toggle-password]").forEach(function (button) {
            var box = boxOf(button);
            if (box) setShown(button, box, false);
        });
    }

    // one listener for the whole page, so boxes added later (e.g. a form opened by a script) work too
    document.addEventListener("click", function (event) {
        var button = event.target.closest ? event.target.closest("[data-toggle-password]") : null;
        if (!button) return;
        event.preventDefault();
        var box = boxOf(button);
        if (!box) return;
        setShown(button, box, box.type === "password");
    });

    // capture phase: runs before the page's own submit / reset handlers
    document.addEventListener("submit", function (event) { hideAll(event.target); }, true);
    document.addEventListener("reset", function (event) { hideAll(event.target); }, true);

    // coming back with the browser's Back button: start hidden again
    window.addEventListener("pageshow", function (event) {
        if (event.persisted) hideAll(document);
    });
})();
