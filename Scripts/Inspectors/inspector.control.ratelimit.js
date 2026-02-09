; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, btn } = helpers;

    register('control.ratelimit', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Rate Limit';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const inpKey = el('input', 'input');
        inpKey.value = p.key || '';
        const sKey = section('key (bucket id) - vacío = nodeId', inpKey);

        const inpRate = el('input', 'input');
        inpRate.type = 'number';
        inpRate.value = (p.maxPerMinute != null ? p.maxPerMinute : 60);
        const sRate = section('maxPerMinute', inpRate);

        const inpBurst = el('input', 'input');
        inpBurst.type = 'number';
        inpBurst.value = (p.burst != null ? p.burst : (p.maxPerMinute != null ? p.maxPerMinute : 60));
        const sBurst = section('burst (capacidad)', inpBurst);

        const selMode = el('select', 'input');
        selMode.innerHTML = `
      <option value="delay">delay (espera)</option>
      <option value="error">error (corta)</option>
    `;
        selMode.value = (p.mode || 'delay');
        const sMode = section('mode', selMode);

        const inpMaxWait = el('input', 'input');
        inpMaxWait.type = 'number';
        inpMaxWait.value = (p.maxWaitMs != null ? p.maxWaitMs : 60000);
        const sMaxWait = section('maxWaitMs (solo delay)', inpMaxWait);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Rate Limit';

            node.params = Object.assign({}, node.params, {
                key: (inpKey.value || '').trim(),
                maxPerMinute: parseInt(inpRate.value || '60', 10),
                burst: parseInt(inpBurst.value || inpRate.value || '60', 10),
                mode: (selMode.value || 'delay'),
                maxWaitMs: parseInt(inpMaxWait.value || '60000', 10)
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
        body.appendChild(sKey);
        body.appendChild(sRate);
        body.appendChild(sBurst);
        body.appendChild(sMode);
        body.appendChild(sMaxWait);
        body.appendChild(bSave);
        body.appendChild(bDel);
    });
})();
