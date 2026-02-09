; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, btn } = helpers;

    register('control.parallel', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Parallel';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const inpBranches = el('input', 'input');
        inpBranches.value = Array.isArray(p.branches) ? p.branches.join(',') : (p.branches || '');
        const sBranches = section('Ramas (nodeIds separados por coma)', inpBranches);

        const inpJoin = el('input', 'input');
        inpJoin.value = p.joinNodeId || '';
        const sJoin = section('joinNodeId (nodeId del control.join)', inpJoin);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Parallel';

            const raw = (inpBranches.value || '').trim();
            const branches = raw
                ? raw.split(',').map(x => (x || '').trim()).filter(x => !!x)
                : [];

            node.params = Object.assign({}, node.params, {
                branches: branches,
                joinNodeId: (inpJoin.value || '').trim()
            });

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
        body.appendChild(sBranches);
        body.appendChild(sJoin);
        body.appendChild(bSave);
        body.appendChild(bDel);
    });
})();
