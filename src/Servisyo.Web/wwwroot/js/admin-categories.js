/* Admin > Categories: the "+ Add Category" panel, the Edit dialog and the Disable / Enable confirmation.
   The real work (and every permission check) happens on the server when a form is submitted. */
(function () {
    "use strict";

    // ---------- "+ Add Category" panel ----------
    var toggle = document.getElementById("sv-add-toggle");
    var panel = document.getElementById("sv-add-panel");
    var addForm = document.getElementById("sv-add-form");
    var cancel = document.getElementById("sv-add-cancel");

    function setPanel(open) {
        if (!panel || !toggle) return;
        panel.hidden = !open;
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        if (open) {
            var name = document.getElementById("sv-add-name");
            if (name) name.focus();
        }
    }

    if (toggle && panel) {
        toggle.addEventListener("click", function () { setPanel(panel.hidden); });
    }

    if (cancel && addForm) {
        cancel.addEventListener("click", function () {
            // clear by hand: after a server error the inputs have the typed text as their default value
            addForm.querySelectorAll("input[type=text]").forEach(function (input) { input.value = ""; });
            var err = addForm.querySelector(".sv-formerr");
            if (err) err.remove();
            setPanel(false);
            toggle.focus();
        });
    }

    if (addForm) {
        addForm.addEventListener("submit", function () {
            var btn = addForm.querySelector("button[type=submit]");
            if (btn) btn.disabled = true;           // no double submit
        });
    }

    // ---------- dialogs ----------
    function wireClose(dialog) {
        dialog.querySelectorAll("[data-sv-close]").forEach(function (b) {
            b.addEventListener("click", function () { dialog.close(); });
        });
        dialog.addEventListener("click", function (event) {      // clicking the dark backdrop closes it
            if (event.target === dialog) dialog.close();
        });
        var form = dialog.querySelector("form");
        if (form) {
            form.addEventListener("submit", function () {
                var btn = form.querySelector("button[type=submit]");
                if (btn) btn.disabled = true;
            });
        }
    }

    // Edit
    var editDialog = document.getElementById("sv-cat-edit");
    if (editDialog && typeof editDialog.showModal === "function") {
        wireClose(editDialog);
        var editForm = editDialog.querySelector("form");
        document.querySelectorAll("[data-cat-edit]").forEach(function (button) {
            button.addEventListener("click", function () {
                editForm.elements["CategoryId"].value = button.dataset.catId;
                editForm.elements["Name"].value = button.dataset.catName || "";
                editForm.elements["Description"].value = button.dataset.catDescription || "";
                editDialog.showModal();
                editForm.elements["Name"].focus();
            });
        });
    }

    // Disable / Enable
    var toggleDialog = document.getElementById("sv-cat-toggle");
    if (toggleDialog && typeof toggleDialog.showModal === "function") {
        wireClose(toggleDialog);
        var form = toggleDialog.querySelector("form");
        var title = toggleDialog.querySelector(".sv-dialog__title");
        var who = toggleDialog.querySelector(".sv-dialog__who");
        var text = toggleDialog.querySelector(".sv-dialog__text");
        var ok = document.getElementById("sv-cat-toggle-ok");

        document.querySelectorAll("[data-cat-toggle]").forEach(function (button) {
            button.addEventListener("click", function () {
                form.elements["CategoryId"].value = button.dataset.catId;
                form.elements["Enable"].value = button.dataset.enable;
                title.textContent = button.dataset.title;
                who.textContent = button.dataset.catName;
                text.textContent = button.dataset.text;
                ok.textContent = button.dataset.label;
                ok.className = "sv-btn sv-btn--sm sv-btn--" + button.dataset.tone;
                toggleDialog.showModal();
            });
        });
    }
})();
