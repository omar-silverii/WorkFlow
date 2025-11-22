; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function checkbox(label, checked) {
        const wrap = el('div', 'section');
        const id = 'chk_' + Math.random().toString(36).slice(2);
        wrap.innerHTML = '<label><input type="checkbox" id="' + id + '"> ' + label + '</label>';
        const ck = wrap.querySelector('#' + id);
        ck.checked = !!checked;
        return { wrap, input: ck };
    }

    register('util.error', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'Error handler';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const ckCap = checkbox('Capturar errores (detener propagación)', p.capturar);
        const ckRetry = checkbox('Volver a intentar', p.volverAIntentar);
        const ckNotif = checkbox('Notificar', p.notificar);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;
            node.params = { capturar: !!ckCap.input.checked, volverAIntentar: !!ckRetry.input.checked, notificar: !!ckNotif.input.checked };
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

        body.appendChild(sLbl);
        body.appendChild(ckCap.wrap); body.appendChild(ckRetry.wrap); body.appendChild(ckNotif.wrap);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
