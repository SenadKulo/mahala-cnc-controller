// The page in the local network: talks to the desktop program (CncController.App.LanServer) over a WebSocket with the
// same messages as the desktop window (desktop.js), offering machine.js the calls it makes on its SignalR connection.
"use strict";

(() => {
    const handlers = {};
    const pending = new Map();
    const closedHandlers = [];
    let socket = null;
    let nextId = 1;

    function receive(e) {
        let message;
        try {
            message = JSON.parse(e.data);
        } catch {
            return;
        }
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
    }

    function closed() {
        socket = null;
        for (const call of pending.values())
            call.reject(new Error("closed"));
        pending.clear();
        for (const handler of closedHandlers)
            handler();
    }

    const connection = {
        on(name, handler) {
            (handlers[name] ||= []).push(handler);
        },
        invoke(method, ...args) {
            if (!socket || socket.readyState !== WebSocket.OPEN)
                return Promise.reject(new Error("not connected"));
            const id = nextId++;
            return new Promise((resolve, reject) => {
                pending.set(id, { resolve, reject });
                socket.send(JSON.stringify({ type: "invoke", id, method, args }));
            });
        },
        // Asks first whether this device still has the code: without it (or after a new code) machine.js goes back to
        // the code page, as it goes to the login on the website.
        async start() {
            const ping = await fetch("ping", { cache: "no-store" });
            if (ping.status === 401)
                throw { statusCode: 401 };
            if (!ping.ok)
                throw new Error("ping " + ping.status);
            await new Promise((resolve, reject) => {
                const ws = new WebSocket((location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/socket");
                let open = false;
                ws.onopen = () => {
                    open = true;
                    socket = ws;
                    // The program sends everything again, as after a reload.
                    ws.send(JSON.stringify({ type: "ready" }));
                    resolve();
                };
                ws.onmessage = receive;
                ws.onclose = () => open ? closed() : reject(new Error("closed"));
            });
        },
        onreconnecting() { },
        onreconnected() { },
        // A phone's screen going off or leaving the WiFi closes the socket; machine.js then starts again.
        onclose(handler) {
            closedHandlers.push(handler);
        },
    };
    window.cncLan = { connection };

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
