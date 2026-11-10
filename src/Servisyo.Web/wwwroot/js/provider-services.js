/* Provider > My Services: opens and closes the "Add New Service" and "Edit Service" dialogs, shows the lock tooltip
   and stops double-clicks. The real work (and every permission check, including the active-booking lock) happens
   on the server when a form is submitted. */
(function () {
    "use strict";

    // ---------- Activate / Deactivate: send each button press once ----------
    document.querySelectorAll("form[data-sv-once]").forEach(function (form) {
        form.addEventListener("submit", function (event) {
            if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
            form.setAttribute("data-busy", "1");
            form.classList.add("is-busy");
        });
    });

    // ---------- the lock tooltip: a tap (phones have no hover) shows it, a tap anywhere else hides it ----------
    function closeTips(except) {
        document.querySelectorAll(".sv-tip.is-open").forEach(function (tip) { if (tip !== except) tip.classList.remove("is-open"); });
    }
    document.addEventListener("click", function (event) {
        var locked = event.target.closest("[data-svc-locked]");
        if (!locked) { closeTips(null); return; }
        event.preventDefault();                                   // a locked button does nothing else
        var tip = locked.closest(".sv-tip");
        closeTips(tip);
        if (tip) tip.classList.toggle("is-open");
    });
    document.addEventListener("keydown", function (event) { if (event.key === "Escape") closeTips(null); });

    function clearProblems(form) {
        form.querySelectorAll(".sv-fld__err, .sv-formerr").forEach(function (note) { note.remove(); });
        form.querySelectorAll("[aria-invalid]").forEach(function (box) { box.removeAttribute("aria-invalid"); });
        form.removeAttribute("data-busy");
        form.classList.remove("is-busy");
    }

    function onceOnly(form) {
        form.addEventListener("submit", function (event) {
            if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
            form.setAttribute("data-busy", "1");
            form.classList.add("is-busy");
        });
    }

    function closeOnBackdrop(dialog) {
        dialog.querySelectorAll("[data-sv-close]").forEach(function (button) {
            button.addEventListener("click", function () { dialog.close(); });
        });
        dialog.addEventListener("click", function (event) {      // clicking the dark backdrop closes it
            if (event.target === dialog) dialog.close();
        });
    }

    // ---------- Edit Service dialog ----------
    var editDialog = document.getElementById("sv-svc-edit");
    if (editDialog && typeof editDialog.showModal === "function") {
        var editForm = editDialog.querySelector("form");
        var category = editForm.querySelector("[name=CategoryId]");

        // the card's values go into the dialog; the address says WHICH service (the server checks it is yours)
        function openEdit(button) {
            clearProblems(editForm);
            editForm.action = button.getAttribute("data-edit-url");
            editForm.querySelector("[name=ServiceName]").value = button.getAttribute("data-name") || "";
            editForm.querySelector("[name=Description]").value = button.getAttribute("data-description") || "";
            editForm.querySelector("[name=Price]").value = button.getAttribute("data-price") || "";

            // a category an administrator has switched off is not in the list: show it so it can be kept
            category.querySelectorAll("option[data-current]").forEach(function (o) { o.remove(); });
            var id = button.getAttribute("data-category-id") || "";
            var known = Array.prototype.some.call(category.options, function (o) { return o.value === id; });
            if (id && !known) {
                var option = document.createElement("option");
                option.value = id;
                option.textContent = button.getAttribute("data-category-name") || "Current category";
                option.setAttribute("data-current", "");
                category.appendChild(option);
            }
            category.value = id;

            if (!editDialog.open) editDialog.showModal();
            editForm.querySelector("[name=ServiceName]").focus();
        }

        document.querySelectorAll("[data-svc-edit]").forEach(function (button) {
            button.addEventListener("click", function () { openEdit(button); });
        });

        closeOnBackdrop(editDialog);
        editDialog.addEventListener("close", function () { clearProblems(editForm); });
        onceOnly(editForm);

        // the server sent the page back because something was wrong: show the dialog again right away
        if (editDialog.getAttribute("data-open") === "true") editDialog.showModal();
    }

    // ---------- Add New Service dialog ----------
    var dialog = document.getElementById("sv-svc-add");
    var openButton = document.getElementById("sv-svc-open");
    if (!dialog || !openButton || typeof dialog.showModal !== "function") return;

    var form = dialog.querySelector("form");

    function openDialog() { if (!dialog.open) dialog.showModal(); }

    // Cancel / x / Esc / the dark backdrop all leave an empty form behind. Done by hand because, after a server
    // error, the boxes hold the typed text as their starting value and a normal reset would bring it back.
    function resetForm() {
        form.querySelectorAll("input[type=text], input[type=number], textarea").forEach(function (box) { box.value = ""; });
        form.querySelectorAll("select").forEach(function (select) { select.value = select.getAttribute("data-default") || ""; });
        clearProblems(form);
    }

    openButton.addEventListener("click", openDialog);
    closeOnBackdrop(dialog);
    dialog.addEventListener("close", resetForm);
    onceOnly(form);

    // the server sent the page back because something was wrong: show the dialog again right away
    if (dialog.getAttribute("data-open") === "true") openDialog();
})();
