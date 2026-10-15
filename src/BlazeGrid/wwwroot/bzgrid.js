// BlazeGrid browser module: viewport tracking, event delegation, column resize / drag,
// text measurement, state storage and the Web Worker bridge.

const handles = new Map();
let nextHandle = 0;
let drag = null;

// ---------------------------------------------------------------- grid instance

export function init(root, scroller, dotnet, opts) {
    const h = {
        root, scroller, dotnet,
        rh: opts.rowHeight, overscan: opts.overscan,
        start: -1, end: -1, raf: 0, resizing: false
    };
    h.header = () => scroller.querySelector(':scope > .bz-header');

    // rAF-throttled, with a timer fallback because rAF is suspended in occluded windows.
    h.onScroll = () => {
        if (h.raf) return;
        const run = () => {
            if (!h.raf) return;
            cancelAnimationFrame(h.raf);
            clearTimeout(h.timer);
            h.raf = 0;
            update(h, false);
        };
        h.raf = requestAnimationFrame(run);
        h.timer = setTimeout(run, 50);
    };
    scroller.addEventListener('scroll', h.onScroll, { passive: true });
    h.ro = new ResizeObserver(() => update(h, true));
    h.ro.observe(scroller);

    h.onClick = e => rowEvent(h, e, 'click');
    h.onDbl = e => rowEvent(h, e, 'dblclick');
    h.onKey = e => keyEvent(h, e);
    scroller.addEventListener('click', h.onClick);
    scroller.addEventListener('dblclick', h.onDbl);
    scroller.addEventListener('keydown', h.onKey);

    setupResize(h);
    setupDrag(h);

    const id = ++nextHandle;
    handles.set(id, h);
    update(h, true);
    return id;
}

function update(h, force) {
    const st = h.scroller.scrollTop, vh = h.scroller.clientHeight;
    const hdr = h.header();
    const headerH = hdr ? hdr.offsetHeight : 0;
    const first = Math.floor(st / h.rh);
    const visible = Math.ceil(Math.max(0, vh - headerH) / h.rh) + 1;
    if (!force && first >= h.start && first + visible <= h.end) return;
    const s = Math.max(0, first - h.overscan), e = first + visible + h.overscan;
    if (s === h.start && e === h.end) return;
    h.start = s; h.end = e;
    h.dotnet.invokeMethodAsync('OnViewport', s, e);
}

export function refresh(id, rowHeight, resetScroll) {
    const h = handles.get(id);
    if (!h) return;
    h.rh = rowHeight;
    if (resetScroll) h.scroller.scrollTop = 0;
    h.start = h.end = -1;
    update(h, true);
}

export function dispose(id) {
    const h = handles.get(id);
    if (!h) return;
    handles.delete(id);
    h.ro.disconnect();
    h.scroller.removeEventListener('scroll', h.onScroll);
    h.scroller.removeEventListener('click', h.onClick);
    h.scroller.removeEventListener('dblclick', h.onDbl);
    h.scroller.removeEventListener('keydown', h.onKey);
    h.root.removeEventListener('pointerdown', h.onPointerDown);
    h.root.removeEventListener('dblclick', h.onRzDbl);
    for (const [t, f] of h.dragListeners) h.root.removeEventListener(t, f);
}

// ---------------------------------------------------------------- row events (delegated)

function blurActive() {
    const a = document.activeElement;
    if (a && a.blur) a.blur();
}

function rowEvent(h, e, type) {
    const t = e.target;
    if (!(t instanceof Element) || t.closest('.bz-header')) return;
    const row = t.closest('[data-r],[data-g]');
    if (!row || !h.scroller.contains(row)) return;
    if (row.hasAttribute('data-g')) {
        if (type === 'click') h.dotnet.invokeMethodAsync('OnGroupToggle', +row.dataset.g);
        return;
    }
    const r = +row.dataset.r;
    const act = t.closest('[data-act]');
    if (act) {
        if (type === 'click') {
            if (act.dataset.act === 'save') blurActive();
            h.dotnet.invokeMethodAsync('OnRowAction', act.dataset.act, r);
        }
        return;
    }
    if (t.closest('input,select,textarea,button,a,label')) return;
    h.dotnet.invokeMethodAsync('OnRowEvent', type, r, e.ctrlKey || e.metaKey, e.shiftKey);
}

function keyEvent(h, e) {
    const row = e.target instanceof Element ? e.target.closest('.bz-editing') : null;
    if (!row) return;
    if (e.key === 'Enter') {
        e.preventDefault();
        blurActive();
        h.dotnet.invokeMethodAsync('OnRowAction', 'save', +row.dataset.r);
    } else if (e.key === 'Escape') {
        e.preventDefault();
        h.dotnet.invokeMethodAsync('OnRowAction', 'cancel', +row.dataset.r);
    }
}

export function focusEditor(scroller) {
    const el = scroller.querySelector('.bz-editing input, .bz-editing select, .bz-editing textarea');
    if (el) { el.focus(); if (el.select) el.select(); }
}

// ---------------------------------------------------------------- column resize

function headerCells(h) {
    const hdr = h.header();
    return hdr ? Array.from(hdr.children).filter(c => c.classList.contains('bz-hcell')) : [];
}

function applyLayout(h) {
    const cells = headerCells(h);
    const widths = cells.map(c => parseFloat(c.dataset.w));
    const st = h.root.style;
    st.setProperty('--bz-cols', widths.map(w => w + 'px').join(' '));
    st.setProperty('--bz-tw', widths.reduce((a, b) => a + b, 0) + 'px');
    let left = 0;
    cells.forEach((c, i) => { if (c.dataset.lock === 'l') { st.setProperty('--bz-o' + i, left + 'px'); left += widths[i]; } });
    let right = 0;
    for (let i = cells.length - 1; i >= 0; i--) {
        if (cells[i].dataset.lock === 'r') { st.setProperty('--bz-o' + i, right + 'px'); right += widths[i]; }
    }
}

function setupResize(h) {
    h.onPointerDown = e => {
        const rz = e.target instanceof Element ? e.target.closest('.bz-resizer') : null;
        if (!rz || !h.root.contains(rz)) return;
        e.preventDefault();
        e.stopPropagation();
        const cell = rz.closest('.bz-hcell');
        const startX = e.clientX, startW = parseFloat(cell.dataset.w), minW = parseFloat(cell.dataset.minw || '40');
        let w = startW;
        h.resizing = true;
        rz.setPointerCapture(e.pointerId);
        h.root.classList.add('bz-is-resizing');
        const move = ev => {
            w = Math.max(minW, Math.round(startW + ev.clientX - startX));
            cell.dataset.w = w;
            applyLayout(h);
        };
        const up = () => {
            rz.removeEventListener('pointermove', move);
            rz.removeEventListener('pointerup', up);
            rz.removeEventListener('pointercancel', up);
            h.root.classList.remove('bz-is-resizing');
            setTimeout(() => { h.resizing = false; }, 0);
            if (w !== startW) h.dotnet.invokeMethodAsync('OnColumnResized', cell.dataset.key, w);
        };
        rz.addEventListener('pointermove', move);
        rz.addEventListener('pointerup', up);
        rz.addEventListener('pointercancel', up);
    };
    h.root.addEventListener('pointerdown', h.onPointerDown);

    h.onRzDbl = e => {
        const rz = e.target instanceof Element ? e.target.closest('.bz-resizer') : null;
        if (!rz) return;
        e.stopPropagation();
        h.dotnet.invokeMethodAsync('OnAutoFit', rz.closest('.bz-hcell').dataset.key);
    };
    h.root.addEventListener('dblclick', h.onRzDbl);
}

// ---------------------------------------------------------------- drag: reorder + group panel

function clearMarks(h) {
    h.root.querySelectorAll('.bz-drop-before,.bz-drop-after,.bz-drop').forEach(x =>
        x.classList.remove('bz-drop-before', 'bz-drop-after', 'bz-drop'));
}

function setupDrag(h) {
    const L = [];
    L.push(['dragstart', e => {
        const c = e.target instanceof Element ? e.target.closest('.bz-hcell') : null;
        if (!c || !h.root.contains(c) || c.getAttribute('draggable') !== 'true') return;
        if (h.resizing) { e.preventDefault(); return; }
        drag = { h, key: c.dataset.key, groupable: c.dataset.grp === '1' };
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', c.dataset.key);
        c.classList.add('bz-dragging');
    }]);
    L.push(['dragover', e => {
        if (!drag || drag.h !== h) return;
        const t = e.target instanceof Element ? e.target : null;
        if (!t) return;
        const gp = t.closest('[data-bz-groups]');
        if (gp) {
            if (!drag.groupable) return;
            e.preventDefault();
            clearMarks(h);
            gp.classList.add('bz-drop');
            return;
        }
        const c = t.closest('.bz-hcell');
        if (!c || c.dataset.key === drag.key) { clearMarks(h); return; }
        e.preventDefault();
        clearMarks(h);
        const r = c.getBoundingClientRect();
        c.classList.add(e.clientX < r.left + r.width / 2 ? 'bz-drop-before' : 'bz-drop-after');
    }]);
    L.push(['drop', e => {
        if (!drag || drag.h !== h) return;
        e.preventDefault();
        const key = drag.key, t = e.target instanceof Element ? e.target : null;
        clearMarks(h);
        if (!t) return;
        if (t.closest('[data-bz-groups]')) {
            if (drag.groupable) h.dotnet.invokeMethodAsync('OnGroupDrop', key);
            return;
        }
        const c = t.closest('.bz-hcell');
        if (c && c.dataset.key !== key) {
            const r = c.getBoundingClientRect();
            h.dotnet.invokeMethodAsync('OnColumnMoved', key, c.dataset.key, e.clientX < r.left + r.width / 2);
        }
    }]);
    L.push(['dragend', () => {
        clearMarks(h);
        h.root.querySelectorAll('.bz-dragging').forEach(x => x.classList.remove('bz-dragging'));
        drag = null;
    }]);
    for (const [t, f] of L) h.root.addEventListener(t, f);
    h.dragListeners = L;
}

// ---------------------------------------------------------------- measurement

let measureCtx = null;

function probe(root, cls) {
    const el = document.createElement('div');
    el.className = cls;
    el.style.cssText = 'position:absolute;visibility:hidden;left:-9999px;top:0;';
    el.textContent = 'X';
    root.appendChild(el);
    const cs = getComputedStyle(el);
    const r = {
        font: `${cs.fontStyle} ${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`,
        pad: (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0)
    };
    el.remove();
    return r;
}

/** samples[i] = candidate strings for column i; returns widths in px. */
export function measure(root, samples, headers) {
    if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d');
    const ctx = measureCtx, cell = probe(root, 'bz-cell'), head = probe(root, 'bz-hcell');
    const out = new Array(samples.length);
    for (let i = 0; i < samples.length; i++) {
        ctx.font = cell.font;
        let m = 0;
        for (const s of samples[i]) { const w = ctx.measureText(s).width; if (w > m) m = w; }
        ctx.font = head.font;
        const hw = ctx.measureText(headers[i] || '').width + head.pad + 40;
        out[i] = Math.ceil(Math.max(m + cell.pad + 2, hw));
    }
    return out;
}

// ---------------------------------------------------------------- misc

export function afterPaint() {
    // rAF is suspended in hidden / occluded windows; cap the wait so stats still resolve.
    return new Promise(r => {
        const t = setTimeout(r, 500);
        requestAnimationFrame(() => setTimeout(() => { clearTimeout(t); r(); }, 0));
    });
}

export function clampMenu(el) {
    if (!el) return;
    const r = el.getBoundingClientRect(), pad = 8;
    if (r.right > innerWidth - pad) el.style.left = Math.max(pad, innerWidth - r.width - pad) + 'px';
    if (r.bottom > innerHeight - pad) el.style.top = Math.max(pad, innerHeight - r.height - pad) + 'px';
}

export function loadState(key) {
    try { return localStorage.getItem('bzgrid:' + key); } catch { return null; }
}

export function saveState(key, json) {
    try { localStorage.setItem('bzgrid:' + key, json); } catch { /* storage unavailable */ }
}

export function clearState(key) {
    try { localStorage.removeItem('bzgrid:' + key); } catch { /* storage unavailable */ }
}

// ---------------------------------------------------------------- worker bridge

let worker = null, reqId = 0;
const pending = new Map();

function w() {
    if (!worker) {
        worker = new Worker(new URL('./bzgrid.worker.js', import.meta.url));
        worker.onmessage = e => {
            const { id, ok, res, err } = e.data, p = pending.get(id);
            pending.delete(id);
            if (p) ok ? p.resolve(res) : p.reject(new Error(err));
        };
    }
    return worker;
}

function call(op, a, transfer) {
    return new Promise((resolve, reject) => {
        const id = ++reqId;
        pending.set(id, { resolve, reject });
        w().postMessage({ id, op, a }, transfer || []);
    });
}

function ownBuffer(bytes) {
    return bytes.byteOffset === 0 && bytes.byteLength === bytes.buffer.byteLength ? bytes.buffer : bytes.slice().buffer;
}

export function wReset(g, n) { return call('reset', { g, n }); }

export function wLoad(g, c, isNum, numeric, bytes) {
    const buf = ownBuffer(bytes);
    return call('load', { g, c, isNum, numeric, buf }, [buf]);
}

export function wQuery(g, q) { return call('query', { g, q }); }

export function wDistinct(g, c, numeric, q) { return call('distinct', { g, c, numeric, q }); }

export function wDispose(g) { return worker ? call('dispose', { g }) : null; }
