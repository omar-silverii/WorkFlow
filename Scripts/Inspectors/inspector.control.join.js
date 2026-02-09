; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, btn } = helpers;

    register('control.join', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Join';
        if (sub) sub.textContent = 'Punto de sincronización para control.parallel';

        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const info = el('div', 'section');
        info.innerHTML =
            '<div style="font-size:12px; opacity:.85;">' +
            'Este nodo es el punto donde las ramas se “unen”. ' +
            'La lógica de sincronización la realiza el handler de <b>control.parallel</b>.' +
            '</div>';

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Join';
            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

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
            if (ctx.nodes && ctx.nodes[node.id]) delete ctx.nodes[node.id];
            if (ctx.removeNode) ctx.removeNode(node.id);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
            body.innerHTML = '';
        };

        body.appendChild(sLbl);
        body.appendChild(info);
        body.appendChild(bSave);
        body.appendChild(bDel);
    });
})();
