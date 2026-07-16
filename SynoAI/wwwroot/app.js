"use strict";

// ---- Field schemas -------------------------------------------------------
const MAXINT = 2147483647;

const SETTINGS_FIELDS = [
    { k: "Url", t: "text", l: "Synology URL" },
    { k: "User", t: "text", l: "Username" },
    { k: "Password", t: "password", l: "Password (blank = unchanged)" },
    { k: "AllowInsecureUrl", t: "bool", l: "Allow insecure URL" },
    { k: "ApiVersionInfo", t: "number", l: "API version (auth)" },
    { k: "ApiVersionCamera", t: "number", l: "API version (camera)" },
    { k: "Quality", t: "select", l: "Quality", o: ["High", "Balanced", "Low"] },
    { k: "AI.Type", t: "select", l: "AI type", o: ["CodeProjectAIServer"] },
    { k: "AI.Url", t: "text", l: "AI URL" },
    { k: "AI.Path", t: "text", l: "AI path" },
    { k: "DrawMode", t: "select", l: "Draw mode", o: ["Off", "Matches", "All"] },
    { k: "DrawExclusions", t: "bool", l: "Draw exclusion zones" },
    { k: "StrokeWidth", t: "number", l: "Stroke width" },
    { k: "Font", t: "text", l: "Font" },
    { k: "FontSize", t: "number", l: "Font size" },
    { k: "BoxColor", t: "text", l: "Box colour (hex)" },
    { k: "FontColor", t: "text", l: "Font colour (hex)" },
    { k: "ExclusionBoxColor", t: "text", l: "Exclusion box colour (hex)" },
    { k: "TextBoxColor", t: "text", l: "Text box colour (hex)" },
    { k: "TextOffsetX", t: "number", l: "Text offset X" },
    { k: "TextOffsetY", t: "number", l: "Text offset Y" },
    { k: "MinSizeX", t: "number", l: "Default min size X" },
    { k: "MinSizeY", t: "number", l: "Default min size Y" },
    { k: "MaxSizeX", t: "number", l: "Default max size X (blank = none)", max: true },
    { k: "MaxSizeY", t: "number", l: "Default max size Y (blank = none)", max: true },
    { k: "AlternativeLabelling", t: "bool", l: "Alternative labelling" },
    { k: "LabelBelowBox", t: "bool", l: "Label below box" },
    { k: "MaxSnapshots", t: "number", l: "Max snapshots" },
    { k: "SaveOriginalSnapshot", t: "select", l: "Save original snapshot", o: ["Off", "Always", "WithPredictions", "WithValidPredictions"] },
    { k: "DaysToKeepCaptures", t: "number", l: "Days to keep captures" },
    { k: "Delay", t: "number", l: "Delay (ms)" },
    { k: "DelayAfterSuccess", t: "number", l: "Delay after success (ms)", nullable: true },
    { k: "SynoAIUrl", t: "text", l: "SynoAI URL (for notification image links)" },
    { k: "AdminApiKey", t: "password", l: "Admin API key (blank = unchanged)" },
];

const CAMERA_FIELDS = [
    { k: "Name", t: "text", l: "Name" },
    { k: "Types", t: "csv", l: "Types (comma separated)" },
    { k: "Threshold", t: "number", l: "Threshold %" },
    { k: "Wait", t: "number", l: "Wait (ms)" },
    { k: "Delay", t: "number", l: "Delay (ms)", nullable: true },
    { k: "DelayAfterSuccess", t: "number", l: "Delay after success (ms)", nullable: true },
    { k: "MinSizeX", t: "number", l: "Min size X", nullable: true },
    { k: "MinSizeY", t: "number", l: "Min size Y", nullable: true },
    { k: "MaxSizeX", t: "number", l: "Max size X", nullable: true },
    { k: "MaxSizeY", t: "number", l: "Max size Y", nullable: true },
    { k: "Rotate", t: "number", l: "Rotate (degrees)" },
    { k: "MaxSnapshots", t: "number", l: "Max snapshots", nullable: true },
];

// ---- State ---------------------------------------------------------------
const state = {
    apiKey: localStorage.getItem("synoai.apiKey") || "",
    settings: null,
    camera: null,        // the camera currently being edited (a working copy)
    isExisting: false,   // true when the camera already exists (name is fixed)
    image: null,         // HTMLImageElement of the loaded snapshot
    drag: null,          // { x0, y0, x1, y1 } while drawing
};

// ---- Small helpers -------------------------------------------------------
const $ = (id) => document.getElementById(id);
const getPath = (obj, path) => path.split(".").reduce((o, k) => (o == null ? undefined : o[k]), obj);
function setPath(obj, path, value) {
    const parts = path.split(".");
    let o = obj;
    for (let i = 0; i < parts.length - 1; i++) o = (o[parts[i]] ??= {});
    o[parts[parts.length - 1]] = value;
}
const currentTool = () => document.querySelector('input[name="tool"]:checked').value;

async function api(path, method = "GET", body) {
    const opts = { method, headers: { "X-Api-Key": state.apiKey } };
    if (body !== undefined) {
        opts.headers["Content-Type"] = "application/json";
        opts.body = JSON.stringify(body);
    }
    const res = await fetch(path, opts);
    return res;
}

function status(el, message, kind) {
    el.textContent = message;
    el.className = "status" + (kind ? " " + kind : "");
}

// ---- Connect -------------------------------------------------------------
async function connect() {
    state.apiKey = $("apiKey").value.trim();
    localStorage.setItem("synoai.apiKey", state.apiKey);
    status($("connStatus"), "Connecting…");
    try {
        const res = await api("/api/settings");
        if (res.status === 401) return status($("connStatus"), "Invalid API key.", "err");
        if (res.status === 403) return status($("connStatus"), "Admin API is disabled (set AdminApiKey).", "err");
        if (!res.ok) return status($("connStatus"), "Error " + res.status, "err");

        state.settings = await res.json();
        renderSettingsForm();
        await loadCameras();
        $("app").hidden = false;
        status($("connStatus"), "Connected.", "ok");
    } catch (e) {
        status($("connStatus"), "Network error.", "err");
    }
}

// ---- Generic form rendering ---------------------------------------------
function renderForm(container, fields, model) {
    container.innerHTML = "";
    for (const f of fields) {
        const wrap = document.createElement("div");
        wrap.className = "field";
        const id = "f_" + f.k.replace(/\./g, "_");
        const label = document.createElement("label");
        label.textContent = f.l;
        label.htmlFor = id;

        let input;
        const value = getPath(model, f.k);
        if (f.t === "bool") {
            input = document.createElement("input");
            input.type = "checkbox";
            input.checked = !!value;
        } else if (f.t === "select") {
            input = document.createElement("select");
            for (const opt of f.o) {
                const o = document.createElement("option");
                o.value = o.textContent = opt;
                input.appendChild(o);
            }
            input.value = value ?? f.o[0];
        } else {
            input = document.createElement("input");
            input.type = f.t === "password" ? "password" : (f.t === "number" ? "number" : "text");
            if (f.t === "csv") input.value = Array.isArray(value) ? value.join(", ") : "";
            else if (f.t === "number" && f.max && value === MAXINT) input.value = "";
            else if (value === null || value === undefined) input.value = "";
            else input.value = value;
        }
        input.id = id;
        input.dataset.key = f.k;
        input.dataset.type = f.t;
        if (f.max) input.dataset.max = "1";
        if (f.nullable) input.dataset.nullable = "1";

        wrap.appendChild(label);
        wrap.appendChild(input);
        container.appendChild(wrap);
    }
}

function collectForm(container, model) {
    for (const input of container.querySelectorAll("[data-key]")) {
        const key = input.dataset.key, type = input.dataset.type;
        let value;
        if (type === "bool") value = input.checked;
        else if (type === "csv") value = input.value.split(",").map(s => s.trim()).filter(Boolean);
        else if (type === "number") {
            const raw = input.value.trim();
            if (raw === "") value = input.dataset.max ? MAXINT : (input.dataset.nullable ? null : 0);
            else value = Number(raw);
        } else {
            value = input.value; // text / password / select
        }
        setPath(model, key, value);
    }
}

// ---- Global settings -----------------------------------------------------
const renderSettingsForm = () => renderForm($("settingsForm"), SETTINGS_FIELDS, state.settings);

async function saveSettings() {
    collectForm($("settingsForm"), state.settings);
    status($("settingsStatus"), "Saving…");
    const res = await api("/api/settings", "PUT", state.settings);
    if (res.ok) status($("settingsStatus"), "Saved.", "ok");
    else {
        const err = await res.json().catch(() => null);
        status($("settingsStatus"), "Failed: " + (err?.errors?.join("; ") || res.status), "err");
    }
}

// ---- Cameras -------------------------------------------------------------
async function loadCameras() {
    const res = await api("/api/cameras");
    const cameras = res.ok ? await res.json() : [];
    const sel = $("cameraSelect");
    sel.innerHTML = "";
    for (const c of cameras) {
        const o = document.createElement("option");
        o.value = o.textContent = c.Name;
        sel.appendChild(o);
    }
    if (cameras.length) selectCamera(cameras[0].Name);
    else { $("cameraEditor").hidden = true; state.camera = null; }
}

async function selectCamera(name) {
    const res = await api("/api/cameras/" + encodeURIComponent(name));
    if (!res.ok) return;
    const camera = await res.json();
    camera.Exclusions = camera.Exclusions || [];
    state.camera = camera;
    state.isExisting = true;
    $("cameraSelect").value = name;
    $("cameraEditor").hidden = false;
    renderForm($("cameraForm"), CAMERA_FIELDS, camera);
    $("cameraForm").querySelector('[data-key="Name"]').disabled = true; // name is fixed for existing cameras
    renderZoneList();
    redraw();
    status($("cameraStatus"), "");
}

function newCamera() {
    const name = prompt("New camera name (must match the Surveillance Station name):");
    if (!name) return;
    const camera = { Name: name.trim(), Types: [], Threshold: 50, Exclusions: [] };
    state.camera = camera;
    state.isExisting = false;
    state.image = null;
    $("cameraEditor").hidden = false;
    renderForm($("cameraForm"), CAMERA_FIELDS, camera);
    renderZoneList();
    redraw();
    status($("cameraStatus"), "New camera — Save to create it.", "ok");
}

async function saveCamera() {
    if (!state.camera) return;
    const original = state.camera.Name;
    collectForm($("cameraForm"), state.camera);
    state.camera.Exclusions = state.camera.Exclusions || [];
    status($("cameraStatus"), "Saving…");

    let res;
    if (state.isExisting) res = await api("/api/cameras/" + encodeURIComponent(original), "PUT", state.camera);
    else res = await api("/api/cameras", "POST", state.camera);

    if (res.ok) {
        status($("cameraStatus"), "Saved.", "ok");
        await loadCameras();
        selectCamera(state.camera.Name);
    } else {
        const err = await res.text().catch(() => "");
        status($("cameraStatus"), "Failed: " + (err || res.status), "err");
    }
}

async function deleteCamera() {
    if (!state.camera || !state.isExisting) return;
    if (!confirm("Delete camera '" + state.camera.Name + "'?")) return;
    const res = await api("/api/cameras/" + encodeURIComponent(state.camera.Name), "DELETE");
    if (res.ok) await loadCameras();
    else status($("cameraStatus"), "Delete failed: " + res.status, "err");
}

// ---- Snapshot + canvas ---------------------------------------------------
async function loadSnapshot() {
    if (!state.camera) return;
    status($("cameraStatus"), "Fetching snapshot…");
    const res = await api("/api/cameras/" + encodeURIComponent(state.camera.Name) + "/snapshot");
    if (!res.ok) return status($("cameraStatus"), "Snapshot failed (" + res.status + "). Is the camera connected?", "err");
    const blob = await res.blob();
    const img = new Image();
    img.onload = () => {
        state.image = img;
        const canvas = $("canvas");
        canvas.width = img.naturalWidth;
        canvas.height = img.naturalHeight;
        URL.revokeObjectURL(img.src);
        redraw();
        status($("cameraStatus"), "Snapshot loaded (" + img.naturalWidth + "×" + img.naturalHeight + ").", "ok");
    };
    img.src = URL.createObjectURL(blob);
}

function canvasPoint(e) {
    const canvas = $("canvas");
    const rect = canvas.getBoundingClientRect();
    const x = Math.round((e.clientX - rect.left) * (canvas.width / rect.width));
    const y = Math.round((e.clientY - rect.top) * (canvas.height / rect.height));
    return {
        x: Math.max(0, Math.min(canvas.width, x)),
        y: Math.max(0, Math.min(canvas.height, y)),
    };
}

function redraw() {
    const canvas = $("canvas");
    const ctx = canvas.getContext("2d");
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    if (state.image) ctx.drawImage(state.image, 0, 0);
    else { ctx.fillStyle = "#14161b"; ctx.fillRect(0, 0, canvas.width, canvas.height); }

    const cam = state.camera;
    if (!cam) return;

    // Exclusion zones
    ctx.lineWidth = Math.max(2, canvas.width / 400);
    ctx.font = `${Math.max(14, canvas.width / 70)}px system-ui`;
    (cam.Exclusions || []).forEach((z, i) => {
        const x = Math.min(z.Start.X, z.End.X), y = Math.min(z.Start.Y, z.End.Y);
        const w = Math.abs(z.End.X - z.Start.X), h = Math.abs(z.End.Y - z.Start.Y);
        ctx.fillStyle = "rgba(239,68,68,0.22)";
        ctx.fillRect(x, y, w, h);
        ctx.strokeStyle = "#ef4444";
        ctx.strokeRect(x, y, w, h);
        ctx.fillStyle = "#ef4444";
        ctx.fillText((i + 1) + " · " + z.Mode, x + 4, y + 4 + parseInt(ctx.font));
    });

    // Min / max size reference boxes, centred
    drawSizeBox(ctx, cam.MinSizeX, cam.MinSizeY, "#22c55e", "min");
    drawSizeBox(ctx, cam.MaxSizeX, cam.MaxSizeY, "#3b82f6", "max");

    // Live drag preview
    if (state.drag) {
        const d = state.drag;
        const x = Math.min(d.x0, d.x1), y = Math.min(d.y0, d.y1);
        ctx.setLineDash([6, 4]);
        ctx.strokeStyle = "#ffffff";
        ctx.strokeRect(x, y, Math.abs(d.x1 - d.x0), Math.abs(d.y1 - d.y0));
        ctx.setLineDash([]);
    }
}

function drawSizeBox(ctx, w, h, colour, label) {
    if (!w || !h || w >= MAXINT || h >= MAXINT) return;
    const canvas = ctx.canvas;
    const x = (canvas.width - w) / 2, y = (canvas.height - h) / 2;
    ctx.setLineDash([8, 5]);
    ctx.strokeStyle = colour;
    ctx.strokeRect(x, y, w, h);
    ctx.setLineDash([]);
    ctx.fillStyle = colour;
    ctx.fillText(`${label} ${w}×${h}`, x + 4, y - 4);
}

function onDown(e) {
    if (!state.camera) return;
    const p = canvasPoint(e);
    state.drag = { x0: p.x, y0: p.y, x1: p.x, y1: p.y };
    try { $("canvas").setPointerCapture(e.pointerId); } catch { /* capture is best-effort */ }
}
function onMove(e) {
    if (!state.drag) return;
    const p = canvasPoint(e);
    state.drag.x1 = p.x; state.drag.y1 = p.y;
    redraw();
}
function onUp() {
    if (!state.drag) return;
    const d = state.drag;
    state.drag = null;
    const x1 = Math.min(d.x0, d.x1), y1 = Math.min(d.y0, d.y1);
    const x2 = Math.max(d.x0, d.x1), y2 = Math.max(d.y0, d.y1);
    const w = x2 - x1, h = y2 - y1;
    if (w < 3 || h < 3) { redraw(); return; } // ignore tiny/accidental drags

    const tool = currentTool();
    if (tool === "exclude") {
        state.camera.Exclusions = state.camera.Exclusions || [];
        state.camera.Exclusions.push({ Start: { X: x1, Y: y1 }, End: { X: x2, Y: y2 }, Mode: $("zoneMode").value });
        renderZoneList();
    } else if (tool === "min") {
        setCameraField("MinSizeX", w); setCameraField("MinSizeY", h);
    } else if (tool === "max") {
        setCameraField("MaxSizeX", w); setCameraField("MaxSizeY", h);
    }
    redraw();
}

function setCameraField(key, value) {
    state.camera[key] = value;
    const input = $("cameraForm").querySelector(`[data-key="${key}"]`);
    if (input) input.value = value;
}

function renderZoneList() {
    const ul = $("zoneList");
    ul.innerHTML = "";
    const zones = state.camera?.Exclusions || [];
    if (!zones.length) {
        const li = document.createElement("li");
        li.className = "empty";
        li.textContent = "No exclusion zones. Draw one on the image.";
        ul.appendChild(li);
        return;
    }
    zones.forEach((z, i) => {
        const li = document.createElement("li");
        const span = document.createElement("span");
        span.textContent = `${i + 1}. ${z.Mode} — (${z.Start.X},${z.Start.Y}) → (${z.End.X},${z.End.Y})`;
        const btn = document.createElement("button");
        btn.className = "danger";
        btn.textContent = "Remove";
        btn.onclick = () => { zones.splice(i, 1); renderZoneList(); redraw(); };
        li.appendChild(span);
        li.appendChild(btn);
        ul.appendChild(li);
    });
}

// ---- Wire up -------------------------------------------------------------
$("apiKey").value = state.apiKey;
$("connectBtn").onclick = connect;
$("apiKey").addEventListener("keydown", (e) => { if (e.key === "Enter") connect(); });
$("saveSettingsBtn").onclick = saveSettings;
$("cameraSelect").onchange = (e) => selectCamera(e.target.value);
$("newCameraBtn").onclick = newCamera;
$("deleteCameraBtn").onclick = deleteCamera;
$("saveCameraBtn").onclick = saveCamera;
$("loadSnapshotBtn").onclick = loadSnapshot;

const canvas = $("canvas");
canvas.addEventListener("pointerdown", onDown);
canvas.addEventListener("pointermove", onMove);
canvas.addEventListener("pointerup", onUp);

if (state.apiKey) connect();
