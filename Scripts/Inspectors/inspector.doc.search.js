// Scripts/Inspectors/inspector.doc.search.js
(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function toBool(v, defVal) {
        if (v === true) return true;
        if (v === false) return false;
        if (v == null) return defVal;
        const s = String(v).trim().toLowerCase();
        if (s === "1" || s === "true" || s === "yes" || s === "si" || s === "sí" || s === "y") return true;
        if (s === "0" || s === "false" || s === "no" || s === "n") return false;
        return defVal;
    }

    function safeJsonParse(txt) {
        try { return JSON.parse(txt); } catch { return null; }
    }

    function setNodeTitle(ctx, node) {
        const elNode = ctx.nodeEl(node.id);
        if (elNode) {
            const t = elNode.querySelector('.node__title');
            if (t) t.textContent = node.label || '';
        }
    }

    register('doc.search', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Documento: Buscar (DMS)';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        inpLbl.addEventListener('input', () => {
            node.label = inpLbl.value;
            ctx.refreshNode(node.id);
        });
        body.appendChild(section('Etiqueta (label)', inpLbl));

        // searchUrl
        const inpUrl = el('input', 'input');
        inpUrl.placeholder = 'Ej: ${wf.dms.searchUrl} o /Api/DmsSearch.ashx';
        inpUrl.value = p.searchUrl || '';
        inpUrl.addEventListener('input', () => { p.searchUrl = inpUrl.value; node.params = p; });
        body.appendChild(section('Endpoint de búsqueda (searchUrl)', inpUrl));

        // useIntranetCredentials
        const chkIntra = el('input');
        chkIntra.type = 'checkbox';
        chkIntra.checked = toBool(p.useIntranetCredentials, true);
        chkIntra.addEventListener('change', () => { p.useIntranetCredentials = chkIntra.checked; node.params = p; });
        body.appendChild(section('Usar credenciales Windows (intranet)', chkIntra));

        // max
        const inpMax = el('input', 'input');
        inpMax.type = 'number';
        inpMax.min = '1';
        inpMax.placeholder = 'Opcional';
        inpMax.value = (p.max == null ? '' : String(p.max));
        inpMax.addEventListener('input', () => {
            const v = inpMax.value.trim();
            if (!v) delete p.max; else p.max = parseInt(v, 10);
            node.params = p;
        });
        body.appendChild(section('Máximo de resultados (max)', inpMax));

        // criteria (JSON)
        const taCrit = el('textarea', 'textarea');
        taCrit.placeholder = '{ "tipo": "POLIZA", "indices": { "Numero": "${biz.poliza.numero}" } }';
        taCrit.value = p.criteria ? JSON.stringify(p.criteria, null, 2) : '{\n  "tipo": "",\n  "indices": {}\n}';
        taCrit.style.minHeight = '140px';
        taCrit.addEventListener('blur', () => {
            const obj = safeJsonParse(taCrit.value);
            if (obj) { p.criteria = obj; node.params = p; taCrit.style.borderColor = ''; }
            else { taCrit.style.borderColor = '#dc3545'; }
        });
        body.appendChild(section('Criterios (criteria) — JSON', taCrit));

        // viewerUrlTemplate
        const inpViewer = el('input', 'input');
        inpViewer.placeholder = 'Ej: ${wf.dms.viewerUrlTemplate} o https://dms/visor?doc={documentoId}';
        inpViewer.value = p.viewerUrlTemplate || '';
        inpViewer.addEventListener('input', () => { p.viewerUrlTemplate = inpViewer.value; node.params = p; });
        body.appendChild(section('Template visor (viewerUrlTemplate)', inpViewer));

        // output
        const inpOut = el('input', 'input');
        inpOut.placeholder = 'Ej: biz.doc.search';
        inpOut.value = p.output || 'biz.doc.search';
        inpOut.addEventListener('input', () => { p.output = inpOut.value; node.params = p; });
        body.appendChild(section('Salida (output path)', inpOut));

        // ===== Botones estándar =====
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = (inpLbl.value || '').trim() || node.label || 'Documento: Buscar (DMS)';
            node.params = p;

            ensurePosition(node);
            setNodeTitle(ctx, node);

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
        };

        bDel.onclick = () => {
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) ctx.nodes.splice(i, 1);
                }
            }
            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            ctx.drawEdges();
            ctx.select(null);
        };

        body.appendChild(rowButtons(bSave, bDel));
    });
})();
