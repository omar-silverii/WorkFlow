; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function toInt(v, def) {
        const n = parseInt(v, 10);
        return isNaN(n) ? def : n;
    }

    register('control.retry', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Reintentar';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Plantillas (control.retry.templates)
        const selTpl = el('select', 'input');
        function fillTemplatesSelect(sel) {
            sel.innerHTML = '';
            const opt0 = document.createElement('option');
            opt0.value = ''; opt0.textContent = '— Elegir —';
            sel.appendChild(opt0);

            const pack =
                (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.retry.templates']) || {};

            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                sel.appendChild(o);
            });

            sel._pack = pack;
        }
        fillTemplatesSelect(selTpl);
        window.addEventListener('wf-templates-ready', () => fillTemplatesSelect(selTpl), { once: true });
        const sTpl = section('Plantilla', selTpl);

        // Inputs
        const inpReint = el('input', 'input'); inpReint.type = 'number';
        inpReint.value = (p.reintentos != null ? p.reintentos : 3);

        const inpBack = el('input', 'input'); inpBack.type = 'number';
        inpBack.value = (p.backoffMs != null ? p.backoffMs : 500);

        const inpMsg = el('input', 'input');
        inpMsg.value = (p.message || '');

        const sReint = section('Reintentos', inpReint);
        const sBack = section('Backoff (ms)', inpBack);
        const sMsg = section('Mensaje (opcional)', inpMsg);

        // Botones
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // Insertar plantilla
        bTpl.onclick = () => {
            const pack = selTpl._pack || {};
            const tpl = (selTpl.value && pack[selTpl.value]) ? pack[selTpl.value] : null;
            if (!tpl) return;

            if (tpl.reintentos != null) inpReint.value = String(toInt(tpl.reintentos, 3));
            if (tpl.backoffMs != null) inpBack.value = String(toInt(tpl.backoffMs, 500));
            if (tpl.message != null) inpMsg.value = String(tpl.message || '');
        };

        // Guardar
        bSave.onclick = () => {
            const next = {
                reintentos: Math.max(0, Math.min(50, toInt(inpReint.value, 3))),
                backoffMs: Math.max(0, Math.min(600000, toInt(inpBack.value, 500))),
                message: inpMsg.value || ''
            };

            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
        };

        // Eliminar
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

        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sReint);
        body.appendChild(sBack);
        body.appendChild(sMsg);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
