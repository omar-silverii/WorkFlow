; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('chat.notify', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'Chat';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['chat.notify.templates']) || {};
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => { const o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o); });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpCanal = el('input', 'input'); inpCanal.value = p.canal || '';
        const sCanal = section('Canal', inpCanal);

        const taMsg = el('textarea', 'textarea'); taMsg.value = p.mensaje || '';
        const sMsg = section('Mensaje', taMsg);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['chat.notify.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['chat.notify']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;
            inpCanal.value = tpl.canal || '';
            taMsg.value = tpl.mensaje || '';
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;
            node.params = { canal: inpCanal.value || '', mensaje: taMsg.value || '' };
            ensurePosition(node);
            const elNode = nodeEl(node.id); if (elNode) elNode.querySelector('.node__title').textContent = node.label;
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            ctx.edges = ctx.edges.filter(e => e.from !== node.id && e.to !== node.id);
            ctx.nodes = ctx.nodes.filter(x => x.id !== node.id);
            const elNode = ctx.nodeEl(node.id); if (elNode) elNode.remove();
            ctx.drawEdges(); ctx.select(null);
        };

        body.appendChild(sLbl); body.appendChild(sTpl);
        body.appendChild(sCanal); body.appendChild(sMsg);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
