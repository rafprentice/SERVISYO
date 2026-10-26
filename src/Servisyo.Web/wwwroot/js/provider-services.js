/* Provider > My Services: opens and closes the "Add New Service" dialog and stops double-clicks.
   The real work (and every permission check) happens on the server when a form is submitted. */
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
        form.querySelectorAll(".sv-fld__err, .sv-formerr").forEach(function (note) { note.remove(); });
        form.querySelectorAll("[aria-invalid]").forEach(function (box) { box.removeAttribute("aria-invalid"); });
        form.removeAttribute("data-busy");
        form.classList.remove("is-busy");
    }

    openButton.addEventListener("click", openDialog);

    dialog.querySelectorAll("[data-sv-close]").forEach(function (button) {
        button.addEventListener("click", function () { dialog.close(); });
    });

    dialog.addEventListener("click", function (event) {          // clicking the dark backdrop closes it
        if (event.target === dialog) dialog.close();
    });

    dialog.addEventListener("close", resetForm);

    form.addEventListener("submit", function (event) {
        if (form.getAttribute("data-busy")) { event.preventDefault(); return; }
        form.setAttribute("data-busy", "1");
        form.classList.add("is-busy");
    });

    // the server sent the page back because something was wrong: show the dialog again right away
    if (dialog.getAttribute("data-open") === "true") openDialog();
})();
