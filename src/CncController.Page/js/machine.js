// The machine page (ui/machine.html): shows the desktop program's state and passes the buttons back to it.
// On the website it talks to the desktop through the website (SignalR); in the desktop window directly
// (window.cncDesktop, set by the window's desktop.js). The desktop checks every command against its own state,
// so a button enabled here a moment too long does no harm.
"use strict";

(() => {
    const $ = id => document.getElementById(id);
    // In the desktop window: its own connection and parts; on the website: null.
    const host = window.cncDesktop || null;
    // In the local network, served by the desktop program itself (lan.js): like the website, without login and notifications.
    const lan = window.cncLan || null;
    for (const el of document.querySelectorAll(host ? ".desktop-only" : ".web-only"))
        el.classList.remove("d-none");
    for (const el of document.querySelectorAll(host ? ".web-only" : ".desktop-only"))
        el.classList.add("d-none");
    if (lan) {
        for (const el of document.querySelectorAll(".account-only"))
            el.classList.add("d-none");
    }
    // Same order as the Tone enum in CncController.Remote.
    const tones = ["muted", "grey", "green", "blue", "orange", "red"];
    const maxLogLines = 300;
    // The drawing, in the page's palette (css/site.css).
    const colors = {
        grid: "#17212c", rapid: "#4d5866", move: "#6b7686", cut: "#5b7fa8", done: "#e0a060", doneGlow: "rgba(224, 160, 96, .55)",
        laser: "#ff4d57", zero: "#3ccf6e", breakMark: "#f59e0b", restartMark: "#f04b55", markOutline: "#0c1218",
    };
    const messageSeconds = 6;

    // ----- Language -----

    // The program's language comes with every snapshot; until the first one, the last one this browser saw.
    const langKey = "cnc.lang";
    let lang = "bs";
    try { lang = localStorage.getItem(langKey) === "en" ? "en" : "bs"; } catch { }
    /// The Bosnian or the English text.
    const t = (bs, en) => lang === "en" ? en : bs;
    /// What is out of reach when commands fail: the website, or in the local network the desktop program itself.
    const noLink = () => lan ? t("nema veze sa desktop programom", "no connection to the desktop program") : t("nema veze sa sajtom", "no connection to the website");
    const capital = text => text.charAt(0).toUpperCase() + text.slice(1);

    /// Switches the page's own texts: data-en on an element replaces its text, data-en-title (…) an attribute.
    /// The Bosnian original is kept in data-orig (data-orig-title …) to switch back.
    function applyLanguage(code) {
        lang = code === "en" ? "en" : "bs";
        try { localStorage.setItem(langKey, lang); } catch { }
        document.documentElement.lang = lang;
        for (const el of document.querySelectorAll("[data-en]")) {
            if (el.dataset.orig === undefined)
                el.dataset.orig = el.textContent;
            el.textContent = lang === "en" ? el.dataset.en : el.dataset.orig;
        }
        for (const attr of ["title", "placeholder", "aria-label", "confirm"]) {
            const name = attr === "confirm" ? "data-confirm" : attr;
            for (const el of document.querySelectorAll(`[data-en-${attr}]`)) {
                if (!el.hasAttribute("data-orig-" + attr))
                    el.setAttribute("data-orig-" + attr, el.getAttribute(name) || "");
                el.setAttribute(name, el.getAttribute(lang === "en" ? "data-en-" + attr : "data-orig-" + attr));
            }
        }
    }

    applyLanguage(lang);

    let snapshot = null;
    let desktop = { online: false, lastSeen: null };
    let siteConnected = false;
    let busy = false;

    // ----- Connection to the website -----

    const connection = host ? host.connection : lan ? lan.connection : new signalR.HubConnectionBuilder()
        .withUrl("/hubs/browser")
        // Forever: the page may stay open on a phone all night.
        .withAutomaticReconnect({ nextRetryDelayInMilliseconds: c => Math.min(10000, 1000 * c.previousRetryCount) })
        .build();

    connection.on("Desktop", status => { desktop = status; showDesktop(); updateControls(); });
    connection.on("Snapshot", s => { snapshot = s; showSnapshot(); });
    connection.on("Log", (lines, replace) => showLog(lines, replace));
    connection.on("Toolpath", data => { drawing.setToolpath(data); closeResume(); });

    connection.onreconnecting(() => setSite(false));
    connection.onreconnected(() => setSite(true));
    connection.onclose(() => { setSite(false); start(); });

    async function start() {
        for (;;) {
            try {
                await connection.start();
                setSite(true);
                if (!host && !lan)
                    push.refresh();
                return;
            } catch (e) {
                // The login expired: back to the login page, then here again.
                if (e && e.statusCode === 401) {
                    goToLogin();
                    return;
                }
                setSite(false);
                await new Promise(r => setTimeout(r, 5000));
            }
        }
    }

    function goToLogin() {
        // In the local network the code page instead.
        location.href = lan ? "/" : "/Prijava?ReturnUrl=%2F";
    }

    function setSite(connected) {
        siteConnected = connected;
        const el = $("siteStatus");
        el.textContent = capital(noLink()) + t(", spajam se ponovo...", ", reconnecting...");
        el.classList.toggle("d-none", connected);
        updateControls();
    }

    function showDesktop() {
        const el = $("desktopStatus");
        el.classList.toggle("d-none", desktop.online);
        const seen = desktop.lastSeen ? formatDateTime(desktop.lastSeen) : null;
        el.textContent = t("Desktop program nije spojen na sajt", "The desktop program isn't connected to the website") +
            (seen ? t(` (zadnji put viđen ${seen}).`, ` (last seen ${seen}).`) : ".") +
            t(" Prikaz ispod je zadnje stanje koje je poslao.", " Below is the last state it sent.");
    }

    // ----- State -----

    function showSnapshot() {
        const s = snapshot;
        if (s.language && s.language !== lang) {
            applyLanguage(s.language);
            // What this script wrote itself, in the new language.
            setSite(siteConnected);
            showDesktop();
            drawing.redraw();
            if (!host && !lan)
                push.refresh();
        }
        setTone($("connection"), s.connectionText, s.connectionTone);

        // The status bar: state, laser, progress.
        $("state").textContent = s.stateText;
        $("statePill").className = "pill tone-" + tones[s.stateTone];
        $("laserText").textContent = s.laserOn ? t("LASER UPALJEN", "LASER ON") : t("laser ugašen", "laser off");
        $("laser").className = "pill" + (s.laserOn ? " laser-on" : "");

        for (const axis of ["x", "y", "z"]) {
            const name = axis.toUpperCase();
            $("work" + name).textContent = formatAxis(s.work?.[axis]);
            const machine = $("machine" + name);
            machine.replaceChildren();
            if (s.machine)
                machine.append(Object.assign(document.createElement("span"), { className: "dro-machine-label", textContent: t("apsolutna ", "absolute ") }), formatAxis(s.machine[axis]));
        }
        $("dro").classList.toggle("unconfirmed", !!s.work && !s.positionConfirmed);
        // Needed only in an alarm, so shown only then.
        $("unlockButton").classList.toggle("d-none", !s.alarm);
        setTone($("rememberedText"), s.rememberedText, s.rememberedTone);

        $("laserButtonText").textContent = s.laserOn ? t("Ugasi laser", "Laser off") : t("Upali laser", "Laser on");
        $("laserButton").className = "btn btn-icon " + (s.laserOn ? "btn-laser-on" : "btn-stop");
        $("laserPowerRow").classList.toggle("d-none", s.laserPower == null);
        if (s.laserPower != null && document.activeElement !== $("laserPower"))
            $("laserPower").value = s.laserPower;
        if (s.laserPowerMax != null)
            $("laserPower").max = s.laserPowerMax;
        $("laserPowerMax").textContent = s.laserPowerMax != null ? t("od ", "of ") + `${s.laserPowerMax} ($30)` : "";

        $("file").textContent = s.fileName || t("Nema učitanog fajla", "No file loaded");
        $("fileInfo").textContent = s.fileInfo || "";
        $("closeFile").classList.toggle("d-none", !s.fileName);
        showProgress(s);
        showTimeline(s);

        for (const box of document.querySelectorAll("[data-setting]")) {
            // Not while its own click is on the way, or it would flick back for a moment.
            if (!box.dataset.pending)
                box.checked = box.dataset.setting === "AutoResume" ? s.autoResume : s.compressorPause;
        }
        $("compressorText").textContent = s.compressorText;
        showPorts(s);

        showJogSteps(s.jogSteps);
        showQuestion(s);
        showAlert(s);
        drawing.setProgress(s.jobActive || s.jobState ? s.completed : 0, s.work, s.jobEvents || []);
        updateControls();
    }

    const ringLength = 2 * Math.PI * 52;

    /// The status bar's progress, and the ring with the numbers in Posao.
    function showProgress(s) {
        const hasJob = !!s.jobState;
        const percent = hasJob && s.total > 0 ? Math.min(100, 100 * s.completed / s.total) : 0;
        $("topProgress").style.width = percent + "%";
        $("topProgressText").innerHTML = "";
        if (hasJob) {
            const text = $("topProgressText");
            const b = t => Object.assign(document.createElement("b"), { textContent: t });
            text.append(b(Math.floor(percent) + "%"), " · " + (s.finishText ? t("gotovo ≈ ", "done ≈ ") : s.jobState.toLowerCase()), s.finishText ? b(s.finishText) : "");
        }
        $("ringDone").setAttribute("stroke-dasharray", `${(ringLength * percent / 100).toFixed(1)} ${ringLength.toFixed(1)}`);
        // Its round end would show a dot at 0%.
        $("ringDone").style.opacity = percent > 0 ? "1" : "0";
        $("ringPercent").textContent = hasJob ? Math.floor(percent) + "%" : "—";
        $("ringCount").textContent = hasJob ? `${s.completed} / ${s.total}` : (s.total ? `${s.total} ` + t("linija", "lines") : "");
        $("remaining").textContent = s.remainingText ? "≈ " + s.remainingText : "—";
        $("finish").textContent = s.finishText || "—";
        $("elapsed").textContent = s.elapsedText || "—";
        $("currentLine").textContent = s.currentLine || "—";
        $("currentLine").title = s.currentLine || "";
    }

    /// The job's timeline: start, now, expected end; compressor breaks and restarts on it.
    function showTimeline(s) {
        const block = $("timelineBlock");
        const start = s.jobStart && Date.parse(s.jobStart);
        const now = Date.parse(s.time);
        const end = s.jobEnd ? Date.parse(s.jobEnd) : now;
        block.classList.toggle("d-none", !start);
        if (!start)
            return;
        const span = Math.max(end - start, 1);
        const at = t => Math.min(100, Math.max(0, 100 * (t - start) / span));
        $("timelineStart").textContent = formatClock(s.jobStart);
        $("timelineNow").textContent = s.jobEnd ? t("sada ", "now ") + formatClock(s.time) : "";
        $("timelineEnd").textContent = formatClock(s.jobEnd || s.time);
        $("timelineDone").style.width = at(now) + "%";
        $("timelineCursor").style.left = `calc(${at(now)}% - 1px)`;
        const events = s.jobEvents || [];
        $("timelineMarks").replaceChildren(...events.map(e => {
            const mark = document.createElement("div");
            mark.className = "timeline-mark " + e.kind;
            const from = at(Date.parse(e.time));
            mark.style.left = from + "%";
            mark.style.width = e.until ? Math.max(0.6, at(Date.parse(e.until)) - from) + "%" : "3px";
            mark.title = (e.kind === "break" ? t("Planirana pauza ", "Scheduled break ") : "Restart ") + formatClock(e.time);
            return mark;
        }));
        $("breakCount").textContent = events.filter(e => e.kind === "break").length;
        $("restartCount").textContent = events.filter(e => e.kind === "restart").length;
    }

    /// Enables what the desktop allows right now, and nothing while it or the website is out of reach.
    function updateControls() {
        const online = siteConnected && desktop.online && snapshot !== null;
        // "always": whenever the desktop can be reached.
        for (const el of document.querySelectorAll("[data-can]"))
            el.disabled = busy || !(online && (el.dataset.can === "always" || snapshot[el.dataset.can]));
        $("connectButton").disabled = busy || !(online && (snapshot.canDisconnect || (snapshot.canConnect && $("port").value)));
        $("port").disabled = $("baud").disabled = busy || !(online && snapshot.canConnect);
        $("convertStart").disabled = busy || !(online && snapshot.canLoadFile);
        for (const box of document.querySelectorAll("[data-setting]"))
            box.disabled = busy || !online;
        $("uploadButton").disabled = busy || !(online && snapshot.canLoadFile && $("uploadFile").files.length > 0);
        $("resumeStart").disabled = busy || !(online && snapshot.canResumeFrom && resume.startIndex >= 0);
        $("questionYes").disabled = $("questionNo").disabled = busy || !online;
        if (online && !snapshot.canResumeFrom)
            closeResume();
    }

    // ----- Commands -----

    /// Sends a command to the desktop and shows its answer; true if it was done.
    async function send(command) {
        busy = true;
        updateControls();
        try {
            const reply = await connection.invoke("Command", command);
            if (!reply.done)
                showMessage(reply.text || t("Komanda nije izvršena.", "The command was not done."), "warning");
            return reply.done;
        } catch {
            showMessage(t("Komanda nije poslana: ", "The command was not sent: ") + noLink() + ".", "danger");
            return false;
        } finally {
            busy = false;
            updateControls();
        }
    }

    for (const button of document.querySelectorAll("[data-command]")) {
        button.addEventListener("click", () => {
            const question = button.dataset.confirm;
            if (question && !confirm(question))
                return;
            send({ name: button.dataset.command });
        });
    }

    $("laserButton").addEventListener("click", () => send({ name: "Laser", value: !snapshot.laserOn }));
    // The desktop switches, and its next snapshot brings the page along.
    $("language").addEventListener("click", () => send({ name: "Language", text: lang === "en" ? "bs" : "en" }));
    $("laserPower").addEventListener("change", () => {
        const power = Math.round(Number($("laserPower").value));
        if (power >= 1)
            send({ name: "LaserPower", number: power });
    });

    for (const box of document.querySelectorAll("[data-setting]")) {
        box.addEventListener("change", async () => {
            box.dataset.pending = "1";
            const done = await send({ name: box.dataset.setting, value: box.checked });
            delete box.dataset.pending;
            if (!done && snapshot)
                showSnapshot();
        });
    }

    $("sendForm").addEventListener("submit", async e => {
        e.preventDefault();
        const text = $("sendText").value.trim();
        if (text && await send({ name: "Send", text }))
            $("sendText").value = "";
    });

    // ----- Position question -----

    function showQuestion(s) {
        const box = $("question");
        box.classList.toggle("d-none", s.questionId == null);
        $("questionText").textContent = s.questionText || "";
    }

    for (const [id, value] of [["questionYes", true], ["questionNo", false]]) {
        $(id).addEventListener("click", () => {
            if (snapshot && snapshot.questionId != null)
                send({ name: "Answer", id: snapshot.questionId, value });
        });
    }

    // ----- Warnings from the desktop -----

    const alertKey = "cnc.alertSeen";

    function showAlert(s) {
        let seen = null;
        try { seen = localStorage.getItem(alertKey); } catch { }
        const show = !!s.alertText && s.alertTime !== seen;
        $("alert").classList.toggle("d-none", !show);
        if (show) {
            $("alertTime").textContent = t("Upozorenje na desktopu, ", "Warning on the desktop, ") + formatDateTime(s.alertTime);
            $("alertText").textContent = s.alertText;
        }
    }

    $("alertClose").addEventListener("click", () => {
        try { localStorage.setItem(alertKey, snapshot.alertTime); } catch { }
        $("alert").classList.add("d-none");
    });

    // ----- Jog -----

    const stepKey = "cnc.jogStep";
    let jogStep = 1;
    try { jogStep = Number(localStorage.getItem(stepKey)) || 1; } catch { }
    let shownSteps = "";

    function showJogSteps(steps) {
        if (!steps || steps.join() === shownSteps)
            return;
        shownSteps = steps.join();
        if (!steps.includes(jogStep))
            jogStep = steps.includes(1) ? 1 : steps[0];
        const group = $("jogSteps");
        $("jogStepShown").textContent = jogStep;
        group.replaceChildren(...steps.map(step => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn btn-sm" + (step === jogStep ? " active" : "");
            button.textContent = step;
            button.addEventListener("click", () => {
                jogStep = step;
                $("jogStepShown").textContent = step;
                try { localStorage.setItem(stepKey, String(step)); } catch { }
                for (const b of group.children)
                    b.classList.toggle("active", b === button);
            });
            return button;
        }));
    }

    // One click, one step, like on the desktop: nothing keeps moving if the phone loses the connection.
    for (const button of document.querySelectorAll("[data-jog]")) {
        button.addEventListener("click", () =>
            send({ name: "Jog", axis: button.dataset.jog, distance: Number(button.dataset.sign) * jogStep }));
    }

    // ----- Upload -----

    $("uploadFile").addEventListener("change", updateControls);

    $("uploadForm").addEventListener("submit", async e => {
        e.preventDefault();
        const form = $("uploadForm");
        busy = true;
        updateControls();
        showMessage(t("Šaljem fajl...", "Sending the file..."), "info", 0);
        try {
            const body = new FormData(form);
            // The page is shared with the desktop window, so the website's token sits outside the form.
            body.append("__RequestVerificationToken", document.querySelector("input[name=__RequestVerificationToken]")?.value || "");
            const response = await fetch(form.action, { method: "POST", body, credentials: "same-origin" });
            // An expired login answers with the login page instead.
            if (!response.ok || !(response.headers.get("content-type") || "").includes("json")) {
                if (response.redirected || response.status === 401) {
                    goToLogin();
                    return;
                }
                showMessage(response.status === 413 ? t("Fajl je prevelik.", "The file is too large.")
                    : t("Fajl nije poslan (greška na sajtu).", "The file was not sent (error on the website)."), "danger");
                return;
            }
            const reply = await response.json();
            if (reply.done) {
                closeConvert();
                showMessage(t("Fajl je učitan.", "The file is loaded."), "success");
                $("uploadFile").value = "";
            } else if (reply.toolpaths) {
                showConvert(reply);
                showMessage(reply.text, "info");
                $("uploadFile").value = "";
            } else {
                showMessage(reply.text || t("Fajl nije učitan.", "The file was not loaded."), "warning");
            }
        } catch {
            showMessage(t("Fajl nije poslan: ", "The file was not sent: ") + noLink() + ".", "danger");
        } finally {
            busy = false;
            updateControls();
        }
    });

    // ----- "Nastavi od..." -----

    const resume = { open: false, line: null, startIndex: -1, timer: 0 };

    $("resumeOpen").addEventListener("click", () => previewResume(null));

    $("resumeLine").addEventListener("input", () => {
        clearTimeout(resume.timer);
        const line = Number($("resumeLine").value);
        if (Number.isInteger(line) && line > 0)
            resume.timer = setTimeout(() => previewResume(line), 300);
    });

    $("resumeCancel").addEventListener("click", closeResume);

    // The panel itself is the question, like the desktop's dialog.
    $("resumeStart").addEventListener("click", async () => {
        if (await send({ name: "ResumeFrom", line: resume.line }))
            closeResume();
    });

    /// Asks the desktop what continuing from this line (null: the suggested one) would do, and shows it on the drawing.
    async function previewResume(line) {
        let preview;
        try {
            preview = await connection.invoke("PreviewResume", line);
        } catch {
            showMessage(capital(noLink()) + ".", "danger");
            return;
        }
        if (!preview) {
            showMessage(t("Desktop program nije odgovorio ili nema učitanog fajla.", "The desktop program didn't answer, or no file is loaded."), "warning");
            return;
        }
        // An older answer than what is typed now is no use.
        if (line !== null && Number($("resumeLine").value) !== line)
            return;
        resume.open = true;
        resume.line = preview.line;
        resume.startIndex = preview.startIndex;
        $("resumePanel").classList.remove("d-none");
        $("resumeStopped").textContent = preview.stoppedText || "";
        const input = $("resumeLine");
        input.max = preview.maxLine;
        if (line === null)
            input.value = preview.line;
        $("resumePreview").textContent = preview.text;
        drawing.setPreview(Math.max(0, preview.startIndex));
        updateControls();
    }

    function closeResume() {
        if (!resume.open)
            return;
        resume.open = false;
        resume.startIndex = -1;
        clearTimeout(resume.timer);
        $("resumePanel").classList.add("d-none");
        drawing.setPreview(null);
    }

    // ----- Connecting to the machine -----

    let shownPorts = "";

    function showPorts(s) {
        const select = $("port");
        const ports = s.ports || [];
        // Rebuilt only when the list changes, so a choice being made isn't reset several times a second.
        if (ports.join() !== shownPorts) {
            shownPorts = ports.join();
            const chosen = select.value || s.portName;
            select.replaceChildren(...ports.map(p => new Option(p, p)));
            if (ports.includes(chosen))
                select.value = chosen;
        }
        // While connected, the port in use.
        if (s.canDisconnect && s.portName && ports.includes(s.portName))
            select.value = s.portName;
        const button = $("connectButton");
        button.textContent = s.canDisconnect ? t("Odspoji", "Disconnect") : t("Spoji", "Connect");
        button.className = "btn btn-sm text-nowrap " + (s.canDisconnect ? "btn-ghost-wood" : "btn-wood");

        const baud = $("baud");
        const rates = (s.baudRates || []).join();
        if (baud.dataset.rates !== rates) {
            baud.dataset.rates = rates;
            baud.replaceChildren(...(s.baudRates || []).map(r => new Option(r, r)));
        }
        if (document.activeElement !== baud)
            baud.value = s.baud;
    }

    $("port").addEventListener("change", updateControls);
    $("baud").addEventListener("change", () => send({ name: "Baud", number: Number($("baud").value) }));

    $("connectButton").addEventListener("click", () => {
        if (snapshot.canDisconnect) {
            if (snapshot.jobActive && !confirm(t("Posao je u toku. Ako odspojiš, slanje se prekida. Odspojiti?",
                "A job is running. Disconnecting stops sending it. Disconnect?")))
                return;
            send({ name: "Disconnect" });
        } else {
            send({ name: "Connect", text: $("port").value });
        }
    });

    // ----- ArtCAM conversion -----

    let convertFile = null;

    /// An uploaded original ArtCAM file: a feed per toolpath, as on the desktop.
    function showConvert(reply) {
        convertFile = reply.fileName;
        $("convertFile").textContent = reply.fileName;
        $("convertRows").replaceChildren(...reply.toolpaths.map(p => {
            const row = document.createElement("tr");
            for (const text of [p.number, p.tool || "-", p.contours, p.passes]) {
                const cell = document.createElement("td");
                cell.textContent = text;
                row.append(cell);
            }
            const feed = document.createElement("input");
            feed.type = "number";
            feed.min = 1;
            feed.max = 10000;
            feed.step = 1;
            feed.value = p.feed;
            feed.className = "form-control form-control-sm feed-input";
            feed.setAttribute("aria-label", t(`Feed za putanju ${p.number}`, `Feed for toolpath ${p.number}`));
            const cell = document.createElement("td");
            cell.append(feed);
            row.append(cell);
            return row;
        }));
        $("convertPanel").classList.remove("d-none");
    }

    function closeConvert() {
        convertFile = null;
        $("convertPanel").classList.add("d-none");
    }

    $("convertCancel").addEventListener("click", closeConvert);

    $("convertStart").addEventListener("click", async () => {
        const feeds = [...$("convertRows").querySelectorAll("input")].map(i => Number(i.value));
        if (feeds.some(f => !Number.isInteger(f) || f < 1 || f > 10000)) {
            showMessage(t("Feed mora biti cijeli broj od 1 do 10000.", "The feed must be a whole number from 1 to 10000."), "warning");
            return;
        }
        if (await send({ name: "Convert", text: convertFile, feeds })) {
            closeConvert();
            showMessage(t("Fajl je konvertovan i učitan.", "The file is converted and loaded."), "success");
        }
    });

    // ----- Compressor rhythm -----

    $("compressorText").addEventListener("click", e => {
        e.preventDefault();
        const form = $("compressorForm");
        if (!form.classList.contains("d-none")) {
            form.classList.add("d-none");
            return;
        }
        // The known start, or now: the desktop counts whole periods from it.
        $("compressorStart").value = snapshot?.compressorStart || new Date().toTimeString().substring(0, 8);
        $("compressorPeriod").value = snapshot?.compressorPeriodMinutes ?? 15;
        form.classList.remove("d-none");
    });

    $("compressorCancel").addEventListener("click", () => $("compressorForm").classList.add("d-none"));

    $("compressorForm").addEventListener("submit", async e => {
        e.preventDefault();
        let time = $("compressorStart").value;
        if (time.length === 5)
            time += ":00";
        if (await send({ name: "Compressors", text: time, number: Number($("compressorPeriod").value), value: true }))
            $("compressorForm").classList.add("d-none");
    });

    $("compressorClear").addEventListener("click", async () => {
        if (await send({ name: "Compressors", value: false }))
            $("compressorForm").classList.add("d-none");
    });

    // ----- Grbl settings -----

    let settings = [];

    $("settingsClose").addEventListener("click", () => {
        const changes = changedSettings();
        if ((changes !== null && !changes.length) || confirm(t("Izmjene nisu spremljene u Grbl. Zatvoriti svejedno?", "The changes aren't saved to Grbl. Close anyway?")))
            $("settingsBody").classList.add("d-none");
    });

    $("settingsRead").addEventListener("click", () => {
        const changes = changedSettings();
        if ((changes !== null && !changes.length) || confirm(t("Izmjene koje nisu spremljene će se izgubiti. Učitati ponovo?", "Unsaved changes will be lost. Read again?")))
            askSettings("ReadGrblSettings");
    });

    $("settingsSave").addEventListener("click", () => {
        const changes = changedSettings();
        if (changes === null)
            return;
        if (!changes.length) {
            showMessage(t("Nema izmjena.", "No changes."), "info");
            return;
        }
        // Grbl stores these in its EEPROM: the same question as on the desktop.
        if (confirm(t("Upisati u Grbl:\n", "Write to Grbl:\n") + changes.map(c => `  $${c.number}=${c.value}`).join("\n")))
            askSettings("WriteGrblSettings", changes);
    });

    // Values as Grbl has them, not unsaved edits; each line can be sent back as a command.
    $("settingsExport").addEventListener("click", () => {
        if (!settings.length)
            return;
        const now = new Date();
        const pad = n => String(n).padStart(2, "0");
        const lines = [`; Grbl ${t("postavke", "settings")}, ${pad(now.getDate())}.${pad(now.getMonth() + 1)}.${now.getFullYear()} ${pad(now.getHours())}:${pad(now.getMinutes())}`,
            ...settings.map(s => {
                const meaning = s.meaning || s.grblText;
                return meaning ? `$${s.number}=${s.value} (${meaning})` : `$${s.number}=${s.value}`;
            })];
        const link = document.createElement("a");
        link.href = URL.createObjectURL(new Blob([lines.join("\r\n") + "\r\n"], { type: "text/plain" }));
        link.download = `grbl-${t("postavke", "settings")}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.txt`;
        link.click();
        setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    });

    /// Reads or writes on the desktop and shows the settings Grbl answered with.
    async function askSettings(method, ...args) {
        const reading = method === "ReadGrblSettings";
        busy = true;
        updateControls();
        showMessage(reading ? t("Čitam postavke...", "Reading the settings...") : t("Upisujem u Grbl...", "Writing to Grbl..."), "info", 0);
        try {
            const reply = await connection.invoke(method, ...args);
            if (reply.done) {
                showSettings(reply.settings);
                showMessage(reading ? t(`Učitano ${reply.settings.length} postavki.`, `Read ${reply.settings.length} settings.`)
                    : t("Upisano i ponovo pročitano.", "Written and read back."), "success");
            } else {
                showMessage(reply.text || t("Nije urađeno.", "Not done."), "warning");
            }
        } catch {
            showMessage(capital(noLink()) + ".", "danger");
        } finally {
            busy = false;
            updateControls();
        }
    }

    function showSettings(list) {
        settings = list;
        $("settingsRows").replaceChildren(...list.map(s => {
            const row = document.createElement("tr");
            const number = document.createElement("td");
            number.className = "text-nowrap font-monospace";
            number.textContent = "$" + s.number;
            const value = document.createElement("td");
            const input = document.createElement("input");
            input.className = "form-control form-control-sm setting-input";
            input.inputMode = "decimal";
            input.value = s.value;
            input.dataset.number = s.number;
            input.setAttribute("aria-label", "$" + s.number);
            input.addEventListener("input", () => input.classList.toggle("setting-changed", input.value.trim() !== s.value));
            value.append(input);
            const meaning = document.createElement("td");
            meaning.textContent = s.meaning || s.grblText || "";
            row.append(number, value, meaning);
            return row;
        }));
        $("settingsBody").classList.remove("d-none");
    }

    /// Changed values, checked like on the desktop; null (and a message) if one isn't a valid number.
    function changedSettings() {
        const changes = [];
        for (const input of $("settingsRows").querySelectorAll("input")) {
            const setting = settings.find(s => s.number === Number(input.dataset.number));
            const text = input.value.trim().replace(",", ".");
            if (text === setting.value)
                continue;
            const value = Number(text);
            if (text === "" || !Number.isFinite(value) || value < 0 || (setting.isInteger && !Number.isInteger(value))) {
                showMessage(`$${setting.number}: ` + (setting.isInteger ? t("upiši cijeli broj (0 ili veći).", "enter a whole number (0 or more).")
                    : t("upiši broj (0 ili veći).", "enter a number (0 or more).")), "warning");
                input.focus();
                return null;
            }
            changes.push({ number: setting.number, value: text });
        }
        return changes;
    }

    // ----- Notifications on this device (web push) -----

    const push = (() => {
        const supported = "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
        let registration = null;
        let subscription = null;

        function show(text, on) {
            $("pushStatus").textContent = text;
            $("pushOn").classList.toggle("d-none", !supported || on !== false);
            $("pushTest").classList.toggle("d-none", on !== true);
            $("pushOff").classList.toggle("d-none", on !== true);
        }

        /// Shows whether this device gets notifications; called once the site connection is up.
        async function refresh() {
            if (!supported) {
                const apple = /iPhone|iPad|iPod/.test(navigator.userAgent);
                show(apple
                    ? t("Na iPhoneu obavještenja rade samo sa početnog ekrana: u Safariju Dijeli → Dodaj na početni ekran, pa otvori CNC odatle i uključi ih ovdje.",
                        "On an iPhone, notifications work only from the home screen: in Safari, Share → Add to Home Screen, then open CNC from there and turn them on here.")
                    : t("Ovaj preglednik ne podržava obavještenja.", "This browser doesn't support notifications."), null);
                return;
            }
            try {
                registration ??= await navigator.serviceWorker.register("/sw.js");
                subscription = await registration.pushManager.getSubscription();
                if (Notification.permission === "denied") {
                    show(t("Obavještenja su blokirana za ovu stranicu u postavkama preglednika.", "Notifications are blocked for this page in the browser's settings."), null);
                    return;
                }
                // The site may have forgotten it (its data was reset): tell it again.
                if (subscription && !await connection.invoke("PushSubscribed", subscription.endpoint))
                    await register(subscription);
                show(subscription
                    ? t("Uključena: ovdje stižu poruke kad posao završi ili stane, alarm, ili kad se izgubi veza.",
                        "On: messages arrive here when a job finishes or stops, on an alarm, or when the connection is lost.")
                    : t("Isključena na ovom uređaju.", "Off on this device."), !!subscription);
            } catch {
                show(t("Obavještenja se trenutno ne mogu provjeriti.", "Notifications can't be checked right now."), null);
            }
        }

        async function register(sub) {
            const json = sub.toJSON();
            if (!await connection.invoke("PushSubscribe", { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, device: deviceName() }))
                throw new Error("refused");
        }

        function deviceName() {
            const ua = navigator.userAgent;
            const system = /Android/.test(ua) ? "Android" : /iPhone|iPad|iPod/.test(ua) ? "iPhone" : /Windows/.test(ua) ? "Windows" : /Mac/.test(ua) ? "Mac" : t("Uređaj", "Device");
            const browser = /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : "";
            return [system, browser].filter(Boolean).join(", ");
        }

        $("pushOn").addEventListener("click", async () => {
            // Asked on the click itself: browsers refuse a permission request without one.
            if (await Notification.requestPermission() !== "granted") {
                show(t("Bez dozvole preglednika obavještenja ne mogu stići.", "Without the browser's permission, notifications can't arrive."), false);
                return;
            }
            try {
                const key = Uint8Array.from(atob((await connection.invoke("PushKey")).replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
                const options = { userVisibleOnly: true, applicationServerKey: key };
                try {
                    subscription = await registration.pushManager.subscribe(options);
                } catch (e) {
                    // Subscribed earlier with another key of the site: start over.
                    if (e?.name !== "InvalidStateError")
                        throw e;
                    await (await registration.pushManager.getSubscription())?.unsubscribe();
                    subscription = await registration.pushManager.subscribe(options);
                }
                await register(subscription);
                showMessage(t("Obavještenja su uključena.", "Notifications are on."), "success");
            } catch {
                showMessage(t("Obavještenja se nisu mogla uključiti.", "Notifications couldn't be turned on."), "danger");
            }
            refresh();
        });

        $("pushOff").addEventListener("click", async () => {
            if (subscription) {
                const endpoint = subscription.endpoint;
                await subscription.unsubscribe().catch(() => { });
                await connection.invoke("PushUnsubscribe", endpoint).catch(() => { });
            }
            refresh();
        });

        $("pushTest").addEventListener("click", async () => {
            let ok = false;
            try {
                ok = subscription && await connection.invoke("PushTest", subscription.endpoint);
            } catch { }
            showMessage(ok ? t("Probna poruka je poslana; trebala bi stići za nekoliko sekundi.", "The test message was sent; it should arrive in a few seconds.")
                : t("Probna poruka nije poslana.", "The test message was not sent."), ok ? "success" : "warning");
        });

        return { refresh };
    })();

    // ----- The desktop window's own parts -----

    if (host) {
        connection.on("Host", info => {
            $("remoteSettings").textContent = info.remoteText;
            // Only where the window was built with the owner's website (not in the public version).
            $("remoteSettings").className = info.remoteAvailable ? "tone-" + tones[info.remoteTone] : "d-none";
            $("lanSettings").textContent = info.lanText;
            $("lanSettings").className = "tone-" + tones[info.lanTone];
            $("versionText").textContent = info.versionText;
            // Only when a new version is there (or downloading).
            $("updateLink").textContent = info.updateText || "";
            $("updateLink").className = info.updateText ? "fw-semibold tone-" + tones[info.updateTone] : "d-none";
        });

        const hostAction = (method) => async e => {
            e?.preventDefault();
            try {
                const reply = await connection.invoke(method);
                if (reply && reply.done === false && reply.text)
                    showMessage(reply.text, "warning");
            } catch {
                showMessage(t("Nije urađeno.", "Not done."), "danger");
            }
        };
        // Its own dialogs, as before: choosing the file, converting an ArtCAM file, remote access, the local network.
        $("openFile").addEventListener("click", hostAction("OpenFile"));
        $("remoteSettings").addEventListener("click", hostAction("RemoteSettings"));
        $("lanSettings").addEventListener("click", hostAction("LanSettings"));
        $("updateLink").addEventListener("click", hostAction("Update"));

        const keyboardKey = "cnc.keyboardJog";
        try { $("keyboardJog").checked = localStorage.getItem(keyboardKey) === "1"; } catch { }
        $("keyboardJog").addEventListener("change", () => {
            try { localStorage.setItem(keyboardKey, $("keyboardJog").checked ? "1" : "0"); } catch { }
        });

        const jogKeys = { ArrowLeft: ["X", -1], ArrowRight: ["X", 1], ArrowUp: ["Y", 1], ArrowDown: ["Y", -1], PageUp: ["Z", 1], PageDown: ["Z", -1] };

        // The old window's keys. One press, one action: a held key repeats, and each repeat would queue another move.
        document.addEventListener("keydown", e => {
            const typing = e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement || e.target instanceof HTMLTextAreaElement;
            const can = name => snapshot && desktop.online && snapshot[name];
            let command = null;
            if (e.key === "Escape")
                command = can("canReset") && { name: "Reset" };
            else if (e.key === "F5")
                command = can("canResume") && { name: "Resume" };
            else if (e.key.toLowerCase() === "o" && e.ctrlKey) {
                e.preventDefault();
                if (!e.repeat && can("canLoadFile"))
                    hostAction("OpenFile")();
                return;
            } else if (typing)
                return;
            else if (e.key === " " && !(e.target instanceof HTMLButtonElement))
                command = can("canPause") && { name: "Pause" };
            else if (jogKeys[e.key] && $("keyboardJog").checked) {
                const [axis, sign] = jogKeys[e.key];
                command = can("canMove") && { name: "Jog", axis, distance: sign * jogStep };
            } else
                return;
            e.preventDefault();
            if (!e.repeat && command)
                send(command);
        });
    }

    // ----- Messages -----

    let messageTimer = 0;

    /// Shows a short answer at the bottom of the screen; seconds 0 keeps it until the next one.
    function showMessage(text, kind, seconds = messageSeconds) {
        const el = $("message");
        el.textContent = text;
        el.className = "message alert alert-" + kind;
        clearTimeout(messageTimer);
        if (seconds > 0)
            messageTimer = setTimeout(() => el.classList.add("d-none"), seconds * 1000);
    }

    $("message").addEventListener("click", () => $("message").classList.add("d-none"));

    // ----- Log -----

    /// What kind of line it is, for its colour: compressor breaks, restarts and errors, resuming, the end.
    /// The log is in either language (a line keeps the language it was written in).
    function logKind(text) {
        if (/planirana pauza|scheduled break|raspored pauze|break schedule|kompresor|compressor/i.test(text))
            return "break";
        if (/restartovao|restarted|restart\b/i.test(text))
            return "restart";
        if (/^!!|greška|alarm|prekinut|odbio|nije (poslano|moguć|izvršen|postavljen|učitan)|error|aborted|refused|not (sent|possible|done|set|loaded|started)|lost/i.test(text))
            return "error";
        if (/nastav|resum|continu/i.test(text))
            return "resume";
        if (/završen|finished/i.test(text))
            return "done";
        if (/^(<<|>>)/.test(text))
            return "grbl";
        return "";
    }

    function showLog(lines, replace) {
        const log = $("log");
        const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 30;
        if (replace)
            log.replaceChildren();
        for (const line of lines) {
            const row = document.createElement("div");
            row.className = "log-line kind-" + logKind(line.text);
            const time = document.createElement("span");
            time.className = "log-time";
            time.textContent = formatTime(line.time);
            const text = document.createElement("span");
            text.className = "log-text";
            text.textContent = line.text;
            row.append(time, text);
            log.append(row);
        }
        while (log.childElementCount > maxLogLines)
            log.firstElementChild.remove();
        if (atBottom || replace)
            log.scrollTop = log.scrollHeight;
    }

    // ----- Drawing -----

    // Three layers, so a status update (several a second) only redraws what changed: the grid and the whole path
    // once, the done part a piece at a time, the laser with its trail and the markers every time.
    const drawing = (() => {
        const box = $("drawing");
        const base = $("layerBase"), done = $("layerDone"), laser = $("layerLaser");
        const trailLength = 14;
        let data = null;
        let view = null;
        let doneSegments = 0;
        let doneLines = 0;
        let completed = 0;
        // "Nastavi od...": shows as done what would be skipped, instead of the job's progress.
        let preview = null;
        let laserAt = null;
        let trail = [];
        let events = [];

        function setToolpath(toolpath) {
            data = toolpath && toolpath.lines.length > 0 ? toolpath : null;
            trail = [];
            showCutSize();
            redraw();
        }

        function setProgress(lines, position, jobEvents) {
            completed = lines;
            if (position && (!laserAt || position.x !== laserAt.x || position.y !== laserAt.y)) {
                trail.push(position);
                if (trail.length > trailLength)
                    trail.shift();
            }
            laserAt = position;
            events = jobEvents;
            drawDone();
            drawLaser();
        }

        function setPreview(lines) {
            preview = lines;
            drawDone();
        }

        function showCutSize() {
            $("cutSize").textContent = "";
            if (!data)
                return;
            let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
            const c = data.coordinates;
            for (let i = 0; i < data.kinds.length; i++) {
                if (data.kinds[i] !== 2)
                    continue;
                minX = Math.min(minX, c[4 * i], c[4 * i + 2]); maxX = Math.max(maxX, c[4 * i], c[4 * i + 2]);
                minY = Math.min(minY, c[4 * i + 1], c[4 * i + 3]); maxY = Math.max(maxY, c[4 * i + 1], c[4 * i + 3]);
            }
            if (minX <= maxX)
                $("cutSize").textContent = t("rez ", "cut ") + `${(maxX - minX).toFixed(1)} × ${(maxY - minY).toFixed(1)} mm`;
        }

        function redraw() {
            const ratio = window.devicePixelRatio || 1;
            for (const canvas of [base, done, laser]) {
                canvas.width = Math.round(box.clientWidth * ratio);
                canvas.height = Math.round(box.clientHeight * ratio);
            }
            // Hidden (another tab on a phone): nothing to fit into; drawn again when shown.
            view = data && base.width > 60 * ratio && base.height > 60 * ratio ? fit(ratio) : null;
            doneSegments = doneLines = 0;
            [base, done, laser].forEach(clear);
            $("gridText").textContent = "";
            if (view) {
                drawGrid();
                drawKind(base, 0, colors.rapid, [6 * ratio, 4 * ratio], view.ratio);
                drawKind(base, 1, colors.move, [], view.ratio);
                drawKind(base, 2, colors.cut, [], 1.6 * view.ratio);
                drawZero();
            }
            drawDone();
            drawLaser();
        }

        function fit(ratio) {
            const c = data.coordinates;
            // The zero is part of the view, so where the drawing lies on the material shows.
            let minX = 0, minY = 0, maxX = 0, maxY = 0;
            for (let i = 0; i < c.length; i += 2) {
                minX = Math.min(minX, c[i]); maxX = Math.max(maxX, c[i]);
                minY = Math.min(minY, c[i + 1]); maxY = Math.max(maxY, c[i + 1]);
            }
            const pad = 28 * ratio;
            const scale = Math.min((base.width - 2 * pad) / Math.max(maxX - minX, 1e-3), (base.height - 2 * pad) / Math.max(maxY - minY, 1e-3));
            // Centered; Y grows upwards like on the machine.
            const offX = (base.width - (maxX - minX) * scale) / 2;
            const offY = (base.height - (maxY - minY) * scale) / 2;
            return { x: v => offX + (v - minX) * scale, y: v => base.height - offY - (v - minY) * scale, ratio, scale };
        }

        /// Millimetre grid, as fine as still reads: 1, 5, 10, 50 or 100 mm.
        function drawGrid() {
            const step = [1, 5, 10, 50, 100, 500].find(s => s * view.scale >= 22 * view.ratio) || 500;
            $("gridText").textContent = t("mreža ", "grid ") + `${step} mm`;
            const g = base.getContext("2d");
            g.strokeStyle = colors.grid;
            g.lineWidth = 1;
            g.beginPath();
            // Grid lines through whole millimetres from the zero, across the whole canvas.
            const toMm = (px, axis) => axis === "x" ? (px - view.x(0)) / view.scale : (view.y(0) - px) / view.scale;
            // At most a few hundred lines, whatever the numbers say.
            for (let mm = Math.ceil(toMm(0, "x") / step) * step, n = 0; view.x(mm) <= base.width && n < 400; mm += step, n++) {
                g.moveTo(Math.round(view.x(mm)) + .5, 0);
                g.lineTo(Math.round(view.x(mm)) + .5, base.height);
            }
            for (let mm = Math.ceil(toMm(base.height, "y") / step) * step, n = 0; view.y(mm) >= 0 && n < 400; mm += step, n++) {
                g.moveTo(0, Math.round(view.y(mm)) + .5);
                g.lineTo(base.width, Math.round(view.y(mm)) + .5);
            }
            g.stroke();
        }

        function drawZero() {
            const g = base.getContext("2d");
            const x = view.x(0), y = view.y(0), arm = 16 * view.ratio;
            g.strokeStyle = colors.zero;
            g.lineWidth = 2 * view.ratio;
            g.setLineDash([]);
            g.beginPath();
            g.moveTo(x + arm, y);
            g.lineTo(x, y);
            g.lineTo(x, y - arm);
            g.stroke();
            g.fillStyle = colors.zero;
            g.font = `${11 * view.ratio}px ${getComputedStyle(document.body).getPropertyValue("--font-mono")}`;
            g.fillText("0,0", x + 4 * view.ratio, y + 14 * view.ratio);
        }

        function drawKind(canvas, kind, color, dash, width) {
            const g = canvas.getContext("2d");
            const c = data.coordinates;
            g.strokeStyle = color;
            g.lineWidth = width;
            g.setLineDash(dash);
            g.beginPath();
            for (let i = 0; i < data.kinds.length; i++) {
                if (data.kinds[i] !== kind)
                    continue;
                g.moveTo(view.x(c[4 * i]), view.y(c[4 * i + 1]));
                g.lineTo(view.x(c[4 * i + 2]), view.y(c[4 * i + 3]));
            }
            g.stroke();
            g.setLineDash([]);
        }

        // Segments are ordered by line, so the done ones are a prefix: going forward only adds to it.
        function drawDone() {
            if (!view)
                return;
            const target = preview ?? completed;
            if (target < doneLines) {
                clear(done);
                doneSegments = 0;
            }
            doneLines = target;
            let end = doneSegments;
            while (end < data.lines.length && data.lines[end] < target)
                end++;
            if (end === doneSegments)
                return;
            const g = done.getContext("2d");
            const c = data.coordinates;
            g.strokeStyle = colors.done;
            g.lineWidth = 2.6 * view.ratio;
            g.lineCap = "round";
            g.shadowColor = colors.doneGlow;
            g.shadowBlur = 6 * view.ratio;
            g.beginPath();
            for (let i = doneSegments; i < end; i++) {
                if (data.kinds[i] !== 2)
                    continue;
                g.moveTo(view.x(c[4 * i]), view.y(c[4 * i + 1]));
                g.lineTo(view.x(c[4 * i + 2]), view.y(c[4 * i + 3]));
            }
            g.stroke();
            doneSegments = end;
        }

        /// Where the job stood when an event happened: the end of the last segment of the lines done by then.
        function pointOfLine(line) {
            let lo = 0, hi = data.lines.length - 1, found = -1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1;
                if (data.lines[mid] < line) {
                    found = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            if (found < 0)
                return null;
            const c = data.coordinates;
            return { x: c[4 * found + 2], y: c[4 * found + 3] };
        }

        function drawLaser() {
            clear(laser);
            if (!view)
                return;
            const g = laser.getContext("2d");
            const r = view.ratio;

            for (const e of events) {
                const p = pointOfLine(e.line);
                if (!p)
                    continue;
                g.beginPath();
                g.arc(view.x(p.x), view.y(p.y), 5.5 * r, 0, 2 * Math.PI);
                g.fillStyle = e.kind === "break" ? colors.breakMark : colors.restartMark;
                g.fill();
                g.lineWidth = 2 * r;
                g.strokeStyle = colors.markOutline;
                g.stroke();
            }

            if (!laserAt)
                return;
            // A short fading trail shows which way the laser moves.
            for (let i = 1; i < trail.length; i++) {
                g.strokeStyle = `rgba(255, 77, 87, ${(i / trail.length * .6).toFixed(2)})`;
                g.lineWidth = 3 * r;
                g.lineCap = "round";
                g.beginPath();
                g.moveTo(view.x(trail[i - 1].x), view.y(trail[i - 1].y));
                g.lineTo(view.x(trail[i].x), view.y(trail[i].y));
                g.stroke();
            }
            const x = view.x(laserAt.x), y = view.y(laserAt.y);
            const halo = g.createRadialGradient(x, y, 0, x, y, 16 * r);
            halo.addColorStop(0, "rgba(255, 77, 87, .85)");
            halo.addColorStop(1, "rgba(255, 77, 87, 0)");
            g.fillStyle = halo;
            g.beginPath();
            g.arc(x, y, 16 * r, 0, 2 * Math.PI);
            g.fill();
            g.fillStyle = colors.laser;
            g.beginPath();
            g.arc(x, y, 5 * r, 0, 2 * Math.PI);
            g.fill();
        }

        function clear(canvas) {
            canvas.getContext("2d").clearRect(0, 0, canvas.width, canvas.height);
        }

        new ResizeObserver(() => redraw()).observe(box);
        return { setToolpath, setProgress, setPreview, redraw };
    })();

    // ----- Formatting -----

    function setTone(el, text, tone) {
        el.textContent = text;
        el.className = el.className.replace(/\btone-\w+/g, "").trim() + " tone-" + tones[tone];
    }

    // As on the desktop: three decimals, and -0.000 shows as 0.000.
    function formatAxis(value) {
        return typeof value === "number" ? (Math.round(value * 1000) / 1000 + 0).toFixed(3).replace(/^-0\.000$/, "0.000") : "—";
    }

    // Times are the desktop's clock, shown as written (the phone may be in another time zone setting).
    function formatTime(iso) {
        return typeof iso === "string" && iso.length >= 19 ? iso.substring(11, 19) : "";
    }

    function formatClock(iso) {
        return typeof iso === "string" && iso.length >= 16 ? iso.substring(11, 16) : "";
    }

    function formatDateTime(iso) {
        return typeof iso === "string" && iso.length >= 19
            ? `${iso.substring(8, 10)}.${iso.substring(5, 7)}. ${iso.substring(11, 16)}`
            : "";
    }

    // ----- Phone: tabs -----

    const tabKey = "cnc.tab";

    function showTab(tab) {
        document.body.dataset.tab = tab;
        for (const button of document.querySelectorAll("[data-tab-button]"))
            button.setAttribute("aria-pressed", String(button.dataset.tabButton === tab));
        try { localStorage.setItem(tabKey, tab); } catch { }
        // The drawing measured itself while hidden.
        if (tab === "drawing")
            drawing.redraw();
    }

    for (const button of document.querySelectorAll("[data-tab-button]"))
        button.addEventListener("click", () => showTab(button.dataset.tabButton));
    let savedTab = null;
    try { savedTab = localStorage.getItem(tabKey); } catch { }
    showTab(["control", "job", "drawing", "log"].includes(savedTab) ? savedTab : "control");

    // ----- Website: logging out -----

    // The page is shared with the desktop window, so the logout form is made here, with the website's token.
    $("logout").addEventListener("click", () => {
        const form = document.createElement("form");
        form.method = "post";
        form.action = "/Odjava";
        const token = document.querySelector("input[name=__RequestVerificationToken]");
        if (token)
            form.append(Object.assign(document.createElement("input"), { type: "hidden", name: token.name, value: token.value }));
        document.body.append(form);
        form.submit();
    });

    start();
})();
