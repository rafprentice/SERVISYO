/* Provider > Booking Requests: "Confirm Service Completion" (database v2.11).
   "Mark Completed" on an In Progress booking opens this window instead of completing at once. The provider picks a
   proof photo from the device's file manager (a preview is shown) and writes a completion message. Both are required:
   when one is missing the window shows a red message under it and nothing is sent.
   The server checks everything again (ProviderBookingsService.CompleteAsync) and answers with JSON.
   Listens on the whole document, so it keeps working after live-bookings.js redraws the cards. */
(function () {
    "use strict";
    if (window.__svCompletion) return;
    window.__svCompletion = true;

    var MAX_BYTES = 5 * 1024 * 1024;
    var MAX_CHARS = 1000;
    var TYPES = /^image\/(jpeg|png|webp)$/;
    var NAMES = /\.(jpe?g|png|webp)$/i;
    var TEXT = {
        message: "Completion message is required. Please input a message before submitting.",
        photo: "Please select a proof photo from your file manager.",
        type: "Please choose a JPG, PNG or WebP photo.",
        size: "That photo is too large. Please choose one under 5 MB.",
        tooLong: "Please keep the completion message to 1,000 characters or fewer.",
        offline: "Could not reach SERVISYO. Check your connection and try again.",
        failed: "Something went wrong. Refresh the page and try again."
    };

    var objectUrl = null;
    var opener = null;

    function dialog() { return document.querySelector("[data-complete-dialog]"); }
    function part(d, name) { return d.querySelector("[data-complete-" + name + "]"); }
    function errorBox(d, which) { return d.querySelector('[data-complete-error="' + which + '"]'); }

    function setError(d, which, text) {
        var box = errorBox(d, which);
        if (!box) return;
        box.textContent = text || "";
        box.hidden = !text;
        var field = which === "message" ? part(d, "message") : which === "photo" ? part(d, "pick") : null;
        if (field) {
            if (text) field.setAttribute("aria-invalid", "true"); else field.removeAttribute("aria-invalid");
        }
        if (which === "photo") part(d, "drop").classList.toggle("is-invalid", !!text);
    }

    function clearPhoto(d) {
        var input = part(d, "photo");
        input.value = "";
        if (objectUrl) { URL.revokeObjectURL(objectUrl); objectUrl = null; }
        var img = part(d, "preview");
        img.removeAttribute("src");
        img.hidden = true;
        part(d, "empty").hidden = false;
        part(d, "filename").textContent = "";
        part(d, "pick").textContent = "Choose Photo";
    }

    function setBusy(d, busy) {
        var submit = part(d, "submit");
        submit.disabled = busy;
        part(d, "submit-text").textContent = busy ? "Saving…" : "Confirm Completion";
        d.setAttribute("aria-busy", busy ? "true" : "false");
    }

    function count(d) {
        var n = part(d, "message").value.length;
        part(d, "count").textContent = n.toLocaleString("en-US");
    }

    function reset(d) {
        clearPhoto(d);
        part(d, "message").value = "";
        count(d);
        ["photo", "message", "general"].forEach(function (w) { setError(d, w, ""); });
        setBusy(d, false);
    }

    function open(button) {
        var d = dialog();
        if (!d) return;
        opener = button;
        reset(d);
        var form = part(d, "form");
        form.setAttribute("action", button.getAttribute("data-complete-url"));
        var id = button.getAttribute("data-complete-id");
        var title = button.getAttribute("data-complete-title") || "this booking";
        var customer = button.getAttribute("data-complete-customer");
        part(d, "sub").textContent = "Booking #" + id + " · " + title + (customer ? " for " + customer : "") +
            ". Add proof of the finished work: the customer and the administrators will see it.";
        if (typeof d.showModal === "function") d.showModal(); else d.setAttribute("open", "");
        part(d, "pick").focus();
    }

    function close(d) {
        if (part(d, "submit").disabled && d.getAttribute("aria-busy") === "true") return;   // sending: wait for the answer
        reset(d);
        if (d.open) d.close(); else d.removeAttribute("open");
        if (opener && document.body.contains(opener)) opener.focus();
        opener = null;
    }

    function showPhoto(d) {
        var input = part(d, "photo");
        var file = input.files && input.files[0];
        if (!file) return;
        if (!TYPES.test(file.type) || !NAMES.test(file.name)) { clearPhoto(d); setError(d, "photo", TEXT.type); return; }
        if (file.size > MAX_BYTES) { clearPhoto(d); setError(d, "photo", TEXT.size); return; }
        setError(d, "photo", "");
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = URL.createObjectURL(file);
        var img = part(d, "preview");
        img.src = objectUrl;
        img.hidden = false;
        part(d, "empty").hidden = true;
        part(d, "filename").textContent = file.name;
        part(d, "pick").textContent = "Change Photo";
    }

    // the same checks as the server, before anything is sent; returns true when the form may be sent
    function check(d) {
        var input = part(d, "photo");
        var message = part(d, "message").value.trim();
        var photoError = !(input.files && input.files[0]) ? TEXT.photo : null;
        var messageError = message.length === 0 ? TEXT.message : message.length > MAX_CHARS ? TEXT.tooLong : null;
        setError(d, "photo", photoError);
        setError(d, "message", messageError);
        setError(d, "general", "");
        if (messageError) part(d, "message").focus();
        else if (photoError) part(d, "pick").focus();
        return !photoError && !messageError;
    }

    function send(d) {
        var form = part(d, "form");
        setBusy(d, true);
        fetch(form.getAttribute("action"), {
            method: "POST",
            body: new FormData(form),        // the photo, the message, the anti-forgery token, the tab and page
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest", "Accept": "application/json" }
        })
            .then(function (response) {
                return response.text().then(function (text) {
                    var data = null;
                    try { data = text ? JSON.parse(text) : null; } catch (ignore) { data = null; }
                    return { status: response.status, data: data };
                });
            })
            .then(function (answer) {
                var data = answer.data || {};
                if (data.redirect && (answer.status === 200 || answer.status === 401 || answer.status === 403)) {
                    window.location.assign(data.redirect);       // completed: the page shows the green message
                    return;
                }
                setBusy(d, false);
                if (answer.status === 400 && data.errors) {
                    setError(d, "photo", data.errors.proofPhoto || "");
                    setError(d, "message", data.errors.completionMessage || "");
                    if (data.errors.completionMessage) part(d, "message").focus(); else part(d, "pick").focus();
                    return;
                }
                if (answer.status === 413) { clearPhoto(d); setError(d, "photo", TEXT.size); return; }
                setError(d, "general", data.message || TEXT.failed);
            })
            .catch(function () {
                setBusy(d, false);
                setError(d, "general", TEXT.offline);
            });
    }

    document.addEventListener("click", function (event) {
        var target = event.target;
        if (!(target instanceof Element)) return;
        var button = target.closest("[data-complete-open]");
        if (button) { event.preventDefault(); open(button); return; }

        var d = dialog();
        if (!d || !d.open) return;
        if (target.closest("[data-complete-cancel]")) { close(d); return; }
        if (target.closest("[data-complete-pick]")) { part(d, "photo").click(); return; }
        if (target === d) close(d);                          // a click on the dark backdrop
    });

    document.addEventListener("change", function (event) {
        var d = dialog();
        if (d && event.target === part(d, "photo")) showPhoto(d);
    });

    document.addEventListener("input", function (event) {
        var d = dialog();
        if (!d || event.target !== part(d, "message")) return;
        count(d);
        if (part(d, "message").value.trim().length > 0) setError(d, "message", "");
    });

    // Esc: close the same way as Cancel ("cancel" does not bubble, so it is caught on the way down)
    document.addEventListener("cancel", function (event) {
        var d = dialog();
        if (d && event.target === d) { event.preventDefault(); close(d); }
    }, true);

    document.addEventListener("submit", function (event) {
        var d = dialog();
        if (!d || event.target !== part(d, "form")) return;
        event.preventDefault();
        if (part(d, "submit").disabled) return;               // already sending
        if (check(d)) send(d);
    }, true);
})();
