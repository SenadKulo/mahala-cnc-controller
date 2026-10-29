// The desktop window's side of the machine page: talks to the program through WebView2 instead of through the
// website, offering machine.js the same calls it makes on its SignalR connection (on, invoke, start).
// The program's side is CncController.App.LocalPage.
"use strict";

(() => {
    const webview = window.chrome.webview;
    const handlers = {};
    const pending = new Map();
    let nextId = 1;

    webview.addEventListener("message", e => {
        const message = e.data;
        if (message.type === "event") {
            for (const handler of handlers[message.name] || [])
                handler(...message.args);
        } else if (message.type === "reply") {
            const call = pending.get(message.id);
            if (!call)
                return;
            pending.delete(message.id);
            if (message.ok)
                call.resolve(message.result);
            else
                call.reject(new Error(message.error));
        }
    });

    const connection = {
        on(name, handler) {
            (handlers[name] ||= []).push(handler);
        },
        invoke(method, ...args) {
            const id = nextId++;
            return new Promise((resolve, reject) => {
                pending.set(id, { resolve, reject });
                webview.postMessage({ type: "invoke", id, method, args });
            });
        },
        // The program is always there: "ready" makes it send everything.
        start() {
            webview.postMessage({ type: "ready" });
            return Promise.resolve();
        },
        onreconnecting() { },
        onreconnected() { },
        onclose() { },
    };
    window.cncDesktop = { connection };

    // The page itself is the website's; load it, then its script.
    fetch("ui/machine.html")
        .then(response => response.text())
        .then(html => {
            document.getElementById("app").innerHTML = html;
            const script = document.createElement("script");
            script.src = "js/machine.js";
            document.body.append(script);
        });
})();
