; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('control.if', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'If';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if.templates']) || {};
            const opt = document.createElement('option'); opt.value = ''; opt.textContent = '— Elegir —'; selTpl.appendChild(opt);
            Object.keys(pack).forEach(k => { const o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o); });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpExpr = el('input', 'input'); inpExpr.value = p.expression || '';
        const sExpr = section('Expresión (truthy)', inpExpr);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if.templates']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : ((window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if']) || {});
            inpExpr.value = tpl.expression || '${payload.status} == 200';
        };

        bSave.onclick = () => {
            const next = Object.assign({}, node.params || {});
            next.expression = (inpExpr.value || '').trim();
            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);
            const elNode = nodeEl(node.id); if (elNode) elNode.querySelector('.node__title').textContent = node.label;
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
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
        body.appendChild(sTpl);
        body.appendChild(sExpr);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
