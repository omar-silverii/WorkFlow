; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('control.delay', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Demora (Delay)';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Etiqueta
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Segundos (más amigable que ms)
        const inpSeg = el('input', 'input');
        inpSeg.type = 'number';
        inpSeg.min = '0';
        inpSeg.value =
            p.segundos != null
                ? p.segundos
                : (p.ms != null ? Math.round(p.ms / 1000) : 1);
        const sSeg = section('Demora (segundos)', inpSeg);

        // Mensaje opcional
        const txtMsg = document.createElement('textarea');
        txtMsg.className = 'input';
        txtMsg.rows = 3;
        txtMsg.value = p.mensaje || 'Esperando antes de continuar...';
        const sMsg = section('Mensaje (opcional, admite ${...})', txtMsg);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            const seg = parseInt(inpSeg.value, 10);
            const segundos = isNaN(seg) || seg < 0 ? 0 : seg;

            node.label = inpLbl.value || node.label || 'Delay';
            node.params = Object.assign({}, node.params, {
                segundos: segundos,
                ms: segundos * 1000,
                mensaje: txtMsg.value || ''
            });

            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
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
        body.appendChild(sSeg);
        body.appendChild(sMsg);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
