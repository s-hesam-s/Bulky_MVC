// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Attach the MVC antiforgery token only to same-origin state-changing requests.
$(document).ajaxSend(function (event, xhr, options) {
    if (!/^(GET|HEAD|OPTIONS|TRACE)$/i.test(options.type) &&
        new URL(options.url, window.location.href).origin === window.location.origin) {
        const token = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (token) xhr.setRequestHeader('RequestVerificationToken', token);
    }
});
