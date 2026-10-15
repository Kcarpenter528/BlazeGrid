// BlazeGrid data worker. Must stay semantically identical to Internal/Engine/ManagedEngine.cs.
'use strict';

const grids = new Map();

function grid(id) {
    let g = grids.get(id);
    if (!g) { g = { n: 0, cols: [] }; grids.set(id, g); }
    return g;
}

function col(g, c) {
    return g.cols[c] || (g.cols[c] = { num: null, text: null, lower: null, tr: null, td: null, sr: null, src: 0, numeric: false });
}

function lower(cc) {
    if (!cc.lower) {
        const t = cc.text, n = t.length, l = new Array(n);
        for (let i = 0; i < n; i++) l[i] = t[i].toLowerCase();
        cc.lower = l;
    }
    return cc.lower;
}

function textRank(cc) {
    if (cc.tr) return cc;
    const t = cc.text, n = t.length, map = new Map(), ids = new Int32Array(n), list = [];
    for (let i = 0; i < n; i++) {
        const s = t[i];
        let id = map.get(s);
        if (id === undefined) { id = list.length; map.set(s, id); list.push(s); }
        ids[i] = id;
    }
    const d = list.length, low = new Array(d), order = new Int32Array(d);
    for (let k = 0; k < d; k++) { low[k] = list[k].toLowerCase(); order[k] = k; }
    order.sort((a, b) => {
        const la = low[a], lb = low[b];
        if (la < lb) return -1;
        if (la > lb) return 1;
        const sa = list[a], sb = list[b];
        return sa < sb ? -1 : sa > sb ? 1 : 0;
    });
    const rankOf = new Int32Array(d), sorted = new Array(d);
    for (let k = 0; k < d; k++) { rankOf[order[k]] = k; sorted[k] = list[order[k]]; }
    for (let i = 0; i < n; i++) ids[i] = rankOf[ids[i]];
    cc.tr = ids; cc.td = sorted;
    return cc;
}

function sortRank(cc, numeric) {
    if (cc.sr) return cc;
    if (!numeric) { textRank(cc); cc.sr = cc.tr; cc.src = cc.td.length; return cc; }
    const v = cc.num, n = v.length, vals = new Float64Array(n);
    let m = 0;
    for (let i = 0; i < n; i++) { const x = v[i]; if (x === x) vals[m++] = x; }
    const s = vals.subarray(0, m).sort();
    let u = 0;
    for (let i = 0; i < m; i++) if (i === 0 || s[i] !== s[i - 1]) s[u++] = s[i];
    const r = new Int32Array(n);
    for (let i = 0; i < n; i++) {
        const x = v[i];
        if (x !== x) { r[i] = 0; continue; }
        let lo = 0, hi = u - 1;
        while (lo <= hi) {
            const mid = (lo + hi) >> 1, y = s[mid];
            if (y < x) lo = mid + 1; else if (y > x) hi = mid - 1; else { lo = mid; break; }
        }
        r[i] = lo + 1;
    }
    cc.sr = r; cc.src = u + 1;
    return cc;
}

function keyRank(g, k) {
    const cc = sortRank(col(g, k.c), k.num);
    if (!k.ord || k.ord.length === 0) return [cc.sr, cc.src];
    textRank(cc);
    const L = k.ord.length, pos = new Map();
    for (let i = 0; i < L; i++) if (!pos.has(k.ord[i])) pos.set(k.ord[i], i);
    const map = new Int32Array(cc.td.length);
    for (let r = 0; r < map.length; r++) { const p = pos.get(cc.td[r]); map[r] = p === undefined ? -1 : p; }
    const n = g.n, res = new Int32Array(n), t = cc.tr, s = cc.sr;
    for (let i = 0; i < n; i++) { const p = map[t[i]]; res[i] = p >= 0 ? p : L + s[i]; }
    return [res, L + cc.src];
}

function bitsFor(count) { return count <= 1 ? 1 : 32 - Math.clz32(count - 1); }

function applyPred(g, p, m) {
    const cc = col(g, p.c), n = g.n, neg = !!p.neg;
    switch (p.k) {
        case 't': {
            const low = lower(cc), v = p.v || '';
            let f;
            switch (p.op) {
                case 'contains': f = s => s.includes(v); break;
                case 'eq': f = s => s === v; break;
                case 'sw': f = s => s.startsWith(v); break;
                case 'ew': f = s => s.endsWith(v); break;
                default: return;
            }
            for (let i = 0; i < n; i++) if (m[i] && f(low[i]) === neg) m[i] = 0;
            break;
        }
        case 'r': {
            const v = cc.num;
            const min = p.min == null ? -Infinity : p.min, max = p.max == null ? Infinity : p.max;
            const mi = !!p.minI, ma = !!p.maxI;
            for (let i = 0; i < n; i++) {
                if (!m[i]) continue;
                const x = v[i];
                const ok = x === x && (mi ? x >= min : x > min) && (ma ? x <= max : x < max);
                if (ok === neg) m[i] = 0;
            }
            break;
        }
        case 's': {
            textRank(cc);
            const set = new Set(p.vals || []), d = cc.td.length, allowed = new Uint8Array(d), tr = cc.tr;
            for (let r = 0; r < d; r++) allowed[r] = set.has(cc.td[r]) ? 1 : 0;
            for (let i = 0; i < n; i++) if (m[i] && (allowed[tr[i]] === 1) === neg) m[i] = 0;
            break;
        }
        case 'e': {
            if (p.num) {
                const v = cc.num;
                for (let i = 0; i < n; i++) if (m[i] && (v[i] !== v[i]) === neg) m[i] = 0;
            } else {
                const t = cc.text;
                for (let i = 0; i < n; i++) if (m[i] && (t[i].length === 0) === neg) m[i] = 0;
            }
            break;
        }
    }
}

function mask(g, q, skip) {
    let m = null;
    for (const p of q.p) {
        if (p.c === skip) continue;
        if (!m) m = new Uint8Array(g.n).fill(1);
        applyPred(g, p, m);
    }
    return m;
}

function sortView(g, view, keys) {
    const K = keys.length, ranks = new Array(K), rc = new Array(K), mul = new Array(K);
    const idxBits = bitsFor(g.n);
    let total = idxBits;
    for (let k = 0; k < K; k++) {
        const [r, c] = keyRank(g, keys[k]);
        ranks[k] = r; rc[k] = c;
        const b = bitsFor(c);
        mul[k] = Math.pow(2, b);
        total += b;
    }
    const cnt = view.length;
    if (total <= 53) {
        const idxMul = Math.pow(2, idxBits), packed = new Float64Array(cnt);
        for (let j = 0; j < cnt; j++) {
            const r = view[j];
            let v = 0;
            for (let k = 0; k < K; k++) {
                let rk = ranks[k][r];
                if (keys[k].d) rk = rc[k] - 1 - rk;
                v = v * mul[k] + rk;
            }
            packed[j] = v * idxMul + r;
        }
        packed.sort();
        for (let j = 0; j < cnt; j++) view[j] = packed[j] % idxMul;
    } else {
        view.sort((a, b) => {
            for (let k = 0; k < K; k++) {
                const d = ranks[k][a] - ranks[k][b];
                if (d !== 0) return keys[k].d ? -d : d;
            }
            return a - b;
        });
    }
}

function groupStarts(g, view, count, q) {
    const G = q.g, tr = new Array(G), lists = new Array(G);
    for (let i = 0; i < G; i++) { tr[i] = textRank(col(g, q.k[i].c)).tr; lists[i] = count > 0 ? [0] : []; }
    for (let j = 1; j < count; j++) {
        const r = view ? view[j] : j, prev = view ? view[j - 1] : j - 1;
        let changed = G;
        for (let i = 0; i < G; i++) if (tr[i][r] !== tr[i][prev]) { changed = i; break; }
        for (let L = changed; L < G; L++) lists[L].push(j);
    }
    return lists;
}

function query(g, q) {
    const n = g.n, m = mask(g, q, -1);
    let view = null, count = n;
    if (m) {
        count = 0;
        for (let i = 0; i < n; i++) count += m[i];
        view = new Int32Array(count);
        for (let i = 0, k = 0; i < n; i++) if (m[i]) view[k++] = i;
    }
    if (q.k.length) {
        if (!view) { view = new Int32Array(n); for (let i = 0; i < n; i++) view[i] = i; }
        sortView(g, view, q.k);
    }
    const starts = q.g > 0 ? groupStarts(g, view, count, q) : [];
    let len = 3 + (view ? count : 0);
    for (const a of starts) len += 1 + a.length;
    const out = new Int32Array(len);
    let o = 0;
    out[o++] = count;
    out[o++] = view ? 1 : 0;
    if (view) { out.set(view, o); o += count; }
    out[o++] = starts.length;
    for (const a of starts) { out[o++] = a.length; out.set(a, o); o += a.length; }
    return new Uint8Array(out.buffer);
}

function distinct(g, c, numeric, q) {
    const m = mask(g, q, c), cc = textRank(col(g, c)), d = cc.td.length, tr = cc.tr, n = g.n;
    const counts = new Int32Array(d), rep = new Int32Array(d).fill(-1);
    for (let i = 0; i < n; i++) {
        const r = tr[i];
        if (rep[r] < 0) rep[r] = i;
        if (!m || m[i]) counts[r]++;
    }
    const order = new Array(d);
    for (let k = 0; k < d; k++) order[k] = k;
    if (numeric && cc.num) {
        const v = cc.num;
        order.sort((a, b) => {
            const x = v[rep[a]], y = v[rep[b]], xn = x !== x, yn = y !== y;
            if (xn || yn) return xn === yn ? a - b : (xn ? -1 : 1);
            return x < y ? -1 : x > y ? 1 : a - b;
        });
    }
    const out = [];
    for (const r of order) {
        if (counts[r] === 0) continue;
        out.push({ t: cc.td[r], c: counts[r] });
        if (out.length >= 10000) break;
    }
    return out;
}

const decoder = new TextDecoder();

self.onmessage = (e) => {
    const { id, op, a } = e.data;
    try {
        let res = true, transfer = [];
        switch (op) {
            case 'reset': { const g = grid(a.g); g.n = a.n; g.cols = []; break; }
            case 'load': {
                const g = grid(a.g), cc = col(g, a.c);
                cc.numeric = a.numeric;
                if (a.isNum) {
                    cc.num = new Float64Array(a.buf);
                } else {
                    cc.text = g.n === 0 ? [] : decoder.decode(new Uint8Array(a.buf)).split('\u001f');
                    cc.lower = null; cc.tr = null; cc.td = null;
                }
                cc.sr = null; cc.src = 0;
                break;
            }
            case 'query': res = query(grid(a.g), a.q); transfer = [res.buffer]; break;
            case 'distinct': res = distinct(grid(a.g), a.c, a.numeric, a.q); break;
            case 'dispose': grids.delete(a.g); break;
        }
        self.postMessage({ id, ok: true, res }, transfer);
    } catch (err) {
        self.postMessage({ id, ok: false, err: String((err && err.stack) || err) });
    }
};
