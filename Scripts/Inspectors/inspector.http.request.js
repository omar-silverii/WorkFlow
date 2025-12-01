; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, kvTable, section, rowButtons, btn } = helpers;

    // utilidades
    function hasPlaceholders(obj) {
        if (!obj) return false;
        const chk = v => typeof v === 'string' && v.indexOf('${') >= 0;
        return Object.keys(obj).some(k => chk(obj[k]));
    }
    function buildUrl(base, q) {
        try {
            const u = new URL(base, window.location.origin);
            Object.keys(q || {}).forEach(k => u.searchParams.set(k, q[k] == null ? '' : String(q[k])));
            return u.toString();
        } catch {
            const qs = Object.keys(q || {})
                .map(k => encodeURIComponent(k) + '=' + encodeURIComponent(q[k] == null ? '' : String(q[k])))
                .join('&');
            return qs ? (base + (base.indexOf('?') >= 0 ? '&' : '?') + qs) : base;
        }
    }
    function pretty(x) {
        if (x == null) return '';
        if (typeof x === 'string') return x;
        try { return JSON.stringify(x, null, 2); } catch { return String(x); }
    }

    register('http.request', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Solicitud HTTP';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // --- Combo de plantillas ---
        
        const selTpl = el('select', 'input');
        function fillTemplatesSelect(sel) {
            sel.innerHTML = '';
            const opt0 = document.createElement('option');
            opt0.value = ''; opt0.textContent = '— Elegir —';
            sel.appendChild(opt0);

            // 1) pack global (si existe)
            const globalPack =
                (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request.templates']) || null;

            // 2) fallback mínimo (por si el pack global no llega)
            const fallbackPack = {
                ping_get: {
                    label: 'GET: /Api/Ping.ashx',
                    url: '/Api/Ping.ashx', method: 'GET', headers: {}, query: {}, body: null, contentType: ''
                },
                cliente_por_id: {
                    label: 'Cliente por id (GET)',
                    url: '/Api/Cliente.ashx', method: 'GET', headers: {}, query: { id: '${solicitud.clienteId}' }, body: null, contentType: ''
                },
                cliente_demo_777: {
                    label: 'Cliente DEMO id=777 (GET)',
                    url: '/Api/Cliente.ashx', method: 'GET', headers: {}, query: { id: 777 }, body: null, contentType: ''
                }
            }; // ← OJO: cerramos el objeto acá

            // 3) usá global si tiene claves; si no, el fallback
            const pack = (globalPack && Object.keys(globalPack).length) ? globalPack : fallbackPack;

            // guardo el pack “activo” en el select para que lo use el botón
            sel._pack = pack;

            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                sel.appendChild(o);
            });
        }

        // Cargar YA (usa pack global si existe o el fallback; sin espera)
        fillTemplatesSelect(selTpl);

        // Refrescar una sola vez cuando las plantillas anuncien que están listas
        window.addEventListener('wf-templates-ready', () => fillTemplatesSelect(selTpl), { once: true });

        // Si el usuario abre el combo y aún no tenía opciones, recargar on-demand
        selTpl.addEventListener('mousedown', () => {
            if (selTpl.options.length <= 1) fillTemplatesSelect(selTpl);
        });

        const sTpl = section('Plantilla', selTpl);

        // URL / Método (declaradas ANTES de usarlas en appendChild)
        const inpUrl = el('input', 'input'); inpUrl.value = p.url || '';
        const sUrl = section('URL', inpUrl);

        const selMethod = el('select', 'input');
        ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].forEach(m => {
            const o = document.createElement('option');
            o.value = m; o.textContent = m;
            if ((p.method || 'GET').toUpperCase() === m) o.selected = true;
            selMethod.appendChild(o);
        });
        const sMet = section('Método', selMethod);

        // headers / query
        let kvH = kvTable(p.headers || {}); const sH = section('Headers', kvH);
        let kvQ = kvTable(p.query || {}); const sQ = section('Query', kvQ);

        // Body + CT
        const taBody = el('textarea', 'textarea');
        taBody.value = (p.body == null ? '' : (typeof p.body === 'string' ? p.body : JSON.stringify(p.body, null, 2)));
        const sBody = section('Cuerpo', taBody);

        const inpCT = el('input', 'input'); inpCT.value = p.contentType || '';
        const sCT = section('Content-Type (opcional)', inpCT);

        const hint = el('div', 'hint'); hint.textContent = 'GET/HEAD ignoran body al enviar.';

        // Botones
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bTest = btn('Probar');
        const bDel = btn('Eliminar nodo');

        // === Insertar plantilla ===
        bTpl.onclick = () => {
            const base = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request']) || {};
            // usar el pack que quedó asociado al select
            const packFromSelect = selTpl._pack || {};
            // si por algún motivo no está, intento el global
            const packGlobal = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request.templates']) || {};

            let tpl = null;
            if (selTpl.value) {
                tpl = packFromSelect[selTpl.value] || packGlobal[selTpl.value] || null;
            }
            if (!tpl) tpl = base;

            // aplicar la plantilla
            inpUrl.value = tpl.url || '/Api/Ping.ashx';
            selMethod.value = ((tpl.method || 'GET') + '').toUpperCase();

            const newKvH = kvTable(tpl.headers || {});
            const newKvQ = kvTable(tpl.query || {});

            if (sH.children.length > 1) sH.replaceChild(newKvH, sH.children[1]); else sH.appendChild(newKvH);
            if (sQ.children.length > 1) sQ.replaceChild(newKvQ, sQ.children[1]); else sQ.appendChild(newKvQ);

            // referencias actualizadas para Guardar/Probar
            kvH = newKvH;
            kvQ = newKvQ;

            taBody.value = (tpl.body == null) ? '' : (typeof tpl.body === 'string' ? tpl.body : JSON.stringify(tpl.body, null, 2));
            inpCT.value = tpl.contentType || '';
        };


        // === Guardar ===
        bSave.onclick = () => {
            const next = {
                url: inpUrl.value.trim(),
                method: selMethod.value,
                headers: kvH.getValue(),
                query: kvQ.getValue(),
                timeoutMs: (p.timeoutMs || 10000)
            };
            const raw = taBody.value.trim();
            if (raw) { try { next.body = JSON.parse(raw); } catch { next.body = raw; } }
            if (inpCT.value.trim()) next.contentType = inpCT.value.trim();

            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        // === Probar ===
        bTest.onclick = async () => {
            const method = (selMethod.value || 'GET').toUpperCase();
            const headers = kvH.getValue();
            const query = kvQ.getValue();
            let url = inpUrl.value.trim();
            let body = taBody.value.trim();
            let contentType = (inpCT.value || '').trim();

            if (hasPlaceholders(query) || (typeof body === 'string' && body.indexOf('${') >= 0)) {
                window.showOutput('HTTP: placeholders detectados',
                    `Encontré placeholders \${...} en Query o Body.
Para probar:
- Usá la plantilla "cliente_demo_777", o
- Reemplazá manualmente los \${...} por valores reales y volvés a "Probar".`);
                return;
            }

            let fetchBody;
            if (body && method !== 'GET' && method !== 'HEAD') {
                try { fetchBody = JSON.parse(body); } catch { fetchBody = body; }
                if (typeof fetchBody !== 'string' && !contentType) contentType = 'application/json';
            }
            url = buildUrl(url, query);

            const hdrs = Object.assign({}, headers || {});
            if (contentType) hdrs['Content-Type'] = contentType;

            bTest.disabled = true;
            const oldTxt = bTest.textContent; bTest.textContent = 'Probando...';
            const t0 = performance.now();
            try {
                const opts = { method, headers: hdrs };
                if (fetchBody != null && method !== 'GET' && method !== 'HEAD') {
                    opts.body = (typeof fetchBody === 'string' ? fetchBody : JSON.stringify(fetchBody));
                }
                const res = await fetch(url, opts);
                const dt = Math.round(performance.now() - t0);
                let text, asJson = null;
                try { asJson = await res.clone().json(); } catch { }
                text = (asJson != null) ? pretty(asJson) : await res.text();

                window.showOutput(`HTTP ${method} → ${res.status} (${dt} ms)`, `URL: ${url}\n\n${text}`);
            } catch (err) {
                window.showOutput('HTTP Error', String(err && err.message ? err.message : err));
            } finally {
                bTest.disabled = false; bTest.textContent = oldTxt;
            }
        };

        // === Eliminar ===
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


        // Orden correcto de agregado al DOM
        body.appendChild(sLbl); body.appendChild(sTpl);
        body.appendChild(sUrl); body.appendChild(sMet);
        body.appendChild(sH); body.appendChild(sQ);
        body.appendChild(sBody); body.appendChild(sCT);
        body.appendChild(hint);
        body.appendChild(rowButtons(bTpl, bSave, bTest, bDel));
    });
})();
