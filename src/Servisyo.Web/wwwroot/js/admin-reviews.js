/* Admin > Reviews: opens the confirmation dialog for Keep / Hide / Restore.
   The real work (and every permission check) happens on the server when the form is submitted. */
(function () {
    "use strict";

    var dialog = document.getElementById("sv-rev-confirm");
    if (!dialog || typeof dialog.showModal !== "function") return;

    var form = dialog.querySelector("form");
    var title = dialog.querySelector(".sv-dialog__title");
    var who = dialog.querySelector(".sv-dialog__who");
    var text = dialog.querySelector(".sv-dialog__text");
    var reason = dialog.querySelector("#sv-rev-reason");
    var ok = dialog.querySelector("#sv-rev-ok");

    document.querySelectorAll("[data-rev-action]").forEach(function (button) {
        button.addEventListener("click", function () {
            form.elements["ReviewId"].value = button.dataset.reviewId;
            form.elements["Action"].value = button.dataset.revAction;
            title.textContent = button.dataset.title;
            who.textContent = "Review #" + button.dataset.reviewId;
            text.textContent = button.dataset.text;
            ok.textContent = button.dataset.label;
            ok.className = "sv-btn sv-btn--sm sv-btn--" + button.dataset.tone;
            reason.value = "";
            dialog.showModal();
        });
    });

    dialog.querySelectorAll("[data-sv-close]").forEach(function (b) {
        b.addEventListener("click", function () { dialog.close(); });
    });

    // clicking the dark backdrop closes the dialog
    dialog.addEventListener("click", function (event) {
        if (event.target === dialog) dialog.close();
    });

    // stop double-clicks from sending the form twice
    form.addEventListener("submit", function () { ok.disabled = true; });
})();
