/* Admin > Users: opens the confirmation dialog for Suspend / Deactivate / Reactivate / Restore.
   The real work (and every permission check) happens on the server when the form is submitted. */
(function () {
    "use strict";

    var dialog = document.getElementById("sv-confirm");
    if (!dialog || typeof dialog.showModal !== "function") return;

    var form = dialog.querySelector("form");
    var title = dialog.querySelector(".sv-dialog__title");
    var who = dialog.querySelector(".sv-dialog__who");
    var text = dialog.querySelector(".sv-dialog__text");
    var reasonBox = dialog.querySelector(".sv-dialog__reason");
    var reason = dialog.querySelector("#sv-reason");
    var ok = dialog.querySelector("#sv-confirm-ok");

    function open(button) {
        form.elements["UserId"].value = button.dataset.userId;
        form.elements["Action"].value = button.dataset.svAction;
        title.textContent = button.dataset.title;
        who.textContent = button.dataset.userName;
        text.textContent = button.dataset.text;
        ok.textContent = button.dataset.label;
        ok.className = "sv-btn sv-btn--sm sv-btn--" + button.dataset.tone;
        reasonBox.hidden = button.dataset.asksReason !== "true";
        reason.value = "";
        dialog.showModal();
    }

    document.querySelectorAll("[data-sv-action]").forEach(function (button) {
        button.addEventListener("click", function () { open(button); });
    });

    dialog.querySelectorAll("[data-sv-close]").forEach(function (button) {
        button.addEventListener("click", function () { dialog.close(); });
    });

    // clicking the dark backdrop closes the dialog
    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) dialog.close();
    });

    // stop double-clicks from sending the form twice
    form.addEventListener("submit", function () { ok.disabled = true; });
})();
