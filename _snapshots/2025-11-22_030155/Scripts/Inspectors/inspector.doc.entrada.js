; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('doc.entrada', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'Documento de entrada';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['doc.entrada.templates']) || null;
        let selTpl = null, sTpl = null;
        if (pack) {
            selTpl = el('select', 'input');
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => { const o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o); });
            sTpl = section('Plantilla', selTpl);
        }

        const selModo = el('select', 'input');
        ['simulado', 'real'].forEach(m => { const o = document.createElement('option'); o.value = m; o.textContent = m; if ((p.modo || 'simulado') === m) o.selected = true; selModo.appendChild(o); });
        const sModo = section('Modo', selModo);

        const inpSalida = el('input', 'input'); inpSalida.value = p.salida || 'solicitud';
        const sSalida = section('Salida (key en contexto)', inpSalida);

        const inpExt = el('input', 'input'); inpExt.value = Array.isArray(p.extensiones) ? p.extensiones.join(',') : (p.extensiones || 'pdf,docx');
        const sExt = section('Extensiones permitidas (CSV)', inpExt);

        const inpMax = el('input', 'input'); inpMax.type = 'number'; inpMax.min = '1'; inpMax.value = (p.maxMB == null ? 10 : p.maxMB);
        const sMax = section('Tamaño máx (MB)', inpMax);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['doc.entrada']) || {};
            const tpl = (selTpl && selTpl.value && pack && pack[selTpl.value]) ? pack[selTpl.value] : def;
            selModo.value = tpl.modo || 'simulado';
            inpSalida.value = tpl.salida || 'solicitud';
            inpExt.value = Array.isArray(tpl.extensiones) ? tpl.extensiones.join(',') : 'pdf,docx';
            inpMax.value = (tpl.maxMB == null ? 10 : tpl.maxMB);
        };

        bSave.onclick = () => {
            const next = {
                modo: selModo.value,
                salida: inpSalida.value || 'solicitud',
                extensiones: (inpExt.value || '').split(',').map(s => s.trim()).filter(Boolean),
                maxMB: parseInt(inpMax.value, 10) || 10
            };
            node.label = inpLbl.value || node.label;
            node.params = next; ensurePosition(node);
            const elNode = nodeEl(node.id); if (elNode) elNode.querySelector('.node__title').textContent = node.label;
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            ctx.edges = ctx.edges.filter(e => e.from !== node.id && e.to !== node.id);
            ctx.nodes = ctx.nodes.filter(x => x.id !== node.id);
            const elNode = ctx.nodeEl(node.id); if (elNode) elNode.remove();
            ctx.drawEdges(); ctx.select(null);
        };

        body.appendChild(sLbl);
        if (sTpl) body.appendChild(sTpl);
        body.appendChild(sModo); body.appendChild(sSalida); body.appendChild(sExt); body.appendChild(sMax);
        body.appendChild(rowButtons(bSave, bDel, (pack ? bTpl : null)).childNodes[2] ? rowButtons(bTpl) : el('div'));
    });
})();
