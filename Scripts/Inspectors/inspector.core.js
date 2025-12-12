; (() => {
    const allowedConds = ["always", "true", "false", "error"];

    function $(id) { return document.getElementById(id); }
    function el(tag, cls) { const x = document.createElement(tag); if (cls) x.className = cls; return x; }

    function kvTable(obj) {
        obj = obj || {};
        const wrap = el('div');
        const tbl = el('table', 'kv');
        wrap.appendChild(tbl);

        function addRow(k, v) {
            const tr = el('tr');
            const kIn = el('input', 'input'); kIn.placeholder = 'nombre'; if (k != null) kIn.value = k;
            const vIn = el('input', 'input'); vIn.placeholder = 'valor'; if (v != null) vIn.value = v;
            tr.appendChild(kIn); tr.appendChild(vIn); tbl.appendChild(tr);
            function maybeAddAnother() {
                const rows = tbl.querySelectorAll('tr');
                const isLast = rows.length && rows[rows.length - 1] === tr;
                if (isLast && (kIn.value.trim() !== '' || vIn.value.trim() !== '')) addRow('', '');
            }
            kIn.addEventListener('input', maybeAddAnother);
            vIn.addEventListener('input', maybeAddAnother);
        }
        Object.keys(obj).forEach(k => addRow(k, obj[k]));
        addRow('', '');

        wrap.getValue = function () {
            const res = {};
            const rows = tbl.querySelectorAll('tr');
            rows.forEach(r => {
                const inputs = r.querySelectorAll('input');
                if (inputs.length < 2) return;
                const k = (inputs[0].value || '').trim();
                const v = inputs[1].value || '';
                if (k) res[k] = v;
            });
            return res;
        };
        return wrap;
    }

    function section(label, control) {
        const s = el('div', 'section');
        const l = el('div', 'label'); l.textContent = label; s.appendChild(l);
        s.appendChild(control);
        return s;
    }
    function rowButtons(...buttons) {
        const r = el('div', 'btn-row');
        buttons.forEach(b => r.appendChild(b));
        return r;
    }
    function btn(text) { const b = el('button', 'btn'); b.type = 'button'; b.textContent = text; return b; }

    // ========== REGISTRO ==========
    const registry = Object.create(null);
    function register(key, renderer) { registry[key] = renderer; }

    // ========== GENÉRICO (JSON) ==========
    function renderGeneric(node, ctx, dom) {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Nodo';
        if (sub) sub.textContent = node.key || '';

        const sLbl = section('Etiqueta (label)', (() => {
            const i = el('input', 'input'); i.value = node.label || ''; return i;
        })());

        const sTpl = (() => {
            const pack =
                (window.PARAM_TEMPLATES &&
                    (window.PARAM_TEMPLATES[node.key + '.templates'] || window.PARAM_TEMPLATES[node.key])) || null;
            if (!pack) return null;
            const sel = el('select', 'input');
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; sel.appendChild(opt0);
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); sel.appendChild(o);
            });
            const s = section('Plantilla', sel);
            const b = btn('Insertar plantilla');
            s.appendChild(rowButtons(b));
            b.onclick = () => {
                const name = sel.value;
                const obj = (name && pack[name]) ? pack[name] : ((window.PARAM_TEMPLATES && window.PARAM_TEMPLATES[node.key]) || {});
                ta.value = JSON.stringify(obj || {}, null, 2);
            };
            return s;
        })();

        const ta = el('textarea', 'textarea'); ta.value = JSON.stringify(node.params || {}, null, 2);
        const sPar = section('Parámetros (JSON)', ta);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = (sLbl.querySelector('input').value || node.label || '');
            try { node.params = ta.value.trim() ? JSON.parse(ta.value) : {}; }
            catch { alert('JSON inválido en parámetros'); return; }
            ensurePosition(node);
            const nel = nodeEl(node.id); if (nel) nel.querySelector('.node__title').textContent = node.label;
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom); // re-render
            // === FIX: redraw edges after save ===
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
        };
        bDel.onclick = () => {
            // Eliminar edges que salen o llegan a este nodo (mutando el array real)
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            // Eliminar el nodo del array real
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            // Quitar del DOM y refrescar canvas
            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            ctx.drawEdges();
            ctx.select(null);
        };


        body.appendChild(sLbl);
        if (sTpl) body.appendChild(sTpl);
        body.appendChild(sPar);
        body.appendChild(rowButtons(bSave, bDel));
    }

    // ========== EDGE INSPECTOR (combo condición) ==========
    function renderEdge(edge, ctx, dom) {
        const { body, title, sub } = dom;
        const { drawEdges, select } = ctx;
        body.innerHTML = '';
        if (title) title.textContent = 'Arista';
        if (sub) sub.textContent = edge.from + ' → ' + edge.to;

        const sel = el('select', 'input');
        allowedConds.forEach(c => {
            const o = document.createElement('option'); o.value = c; o.textContent = c;
            if ((edge.condition || 'always').toLowerCase() === c) o.selected = true;
            sel.appendChild(o);
        });

        const sCond = section('Condición', sel);
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar arista');

        bSave.onclick = () => { edge.condition = (sel.value || 'always'); drawEdges(); window.WF_Inspector.render({ type: 'edge', id: edge.id }, ctx, dom); };
        bDel.onclick = () => {
            const i = ctx.edges.findIndex(x => x.id === edge.id);
            if (i >= 0) ctx.edges.splice(i, 1);
            drawEdges(); select(null);
        };

        body.appendChild(sCond);
        body.appendChild(rowButtons(bSave, bDel));
    }

    // ========== RENDER ROOT ==========
    function render(selected, ctx, dom) {
        const { body, title, sub } = dom;
        if (!selected) {
            body.innerHTML = ''; if (title) title.textContent = 'Seleccioná un nodo o una arista'; if (sub) sub.textContent = ''; return;
        }

        if (selected.type === 'node') {
            const n = ctx.nodes.find(x => x.id === selected.id);
            if (!n) { body.innerHTML = ''; if (title) title.textContent = 'Nodo no encontrado'; if (sub) sub.textContent = ''; return; }
            const insp = registry[n.key] || renderGeneric;
            const util = (window.WF_Inspector && window.WF_Inspector.helpers) || {};
            insp(n, ctx, dom, util);   // pasa helpers/utildades como 4º parámetro
            return;
        }

        if (selected.type === 'edge') {
            const e = ctx.edges.find(x => x.id === selected.id);
            if (!e) { body.innerHTML = ''; if (title) title.textContent = 'Arista no encontrada'; if (sub) sub.textContent = ''; return; }
            renderEdge(e, ctx, dom);
            return;
        }
    }

    window.WF_Inspector = { register, render, helpers: { $, el, kvTable, section, rowButtons, btn } };
})();
