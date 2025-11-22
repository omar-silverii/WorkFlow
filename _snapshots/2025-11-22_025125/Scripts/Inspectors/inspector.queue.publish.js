; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('queue.publish', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'Queue';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish.templates']) || {};
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => { const o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o); });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpBroker = el('input', 'input'); inpBroker.value = p.broker || '';
        const sBroker = section('Broker', inpBroker);

        const inpQueue = el('input', 'input'); inpQueue.value = p.queue || '';
        const sQueue = section('Queue', inpQueue);

        const taPayload = el('textarea', 'textarea'); taPayload.value = p.payload ? JSON.stringify(p.payload, null, 2) : '';
        const sPayload = section('Payload (JSON)', taPayload);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;
            inpBroker.value = tpl.broker || '';
            inpQueue.value = tpl.queue || '';
            taPayload.value = tpl.payload ? JSON.stringify(tpl.payload, null, 2) : '';
        };

        bSave.onclick = () => {
            const next = { broker: inpBroker.value || '', queue: inpQueue.value || '' };
            const raw = taPayload.value.trim();
            if (raw) { try { next.payload = JSON.parse(raw); } catch { alert('Payload JSON inválido'); return; } }
            node.label = inpLbl.value || node.label;
            node.params = next;
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
        body.appendChild(sBroker); body.appendChild(sQueue); body.appendChild(sPayload);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
