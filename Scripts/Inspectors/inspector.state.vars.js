; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function safeJsonParse(s) {
        try { return JSON.parse(s); } catch (e) { return null; }
    }

    register('state.vars', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'State Vars';
        if (sub) sub.textContent = 'Set / Remove variables en ctx.Estado (incluye biz.*)';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // SET JSON
        const taSet = el('textarea', 'input');
        taSet.rows = 8;
        taSet.placeholder = '{ "biz.oc.numero": "OC-${input.ocNro}", "wf.demo": true }';

        let setVal = p.set;
        if (typeof setVal === 'string') {
            taSet.value = setVal;
        } else if (setVal && typeof setVal === 'object') {
            try { taSet.value = JSON.stringify(setVal, null, 2); } catch (e) { taSet.value = ''; }
        } else {
            taSet.value = '';
        }
        const sSet = section('set (JSON object)', taSet);

        // REMOVE CSV
        const inpRemove = el('input', 'input');
        if (Array.isArray(p.remove)) inpRemove.value = p.remove.join(',');
        else inpRemove.value = (p.remove || '');
        const sRemove = section('remove (CSV o array) ej: payload, temp.var', inpRemove);

        // Buttons
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'State Vars';

            // set
            const rawSet = (taSet.value || '').trim();
            let setObj = null;
            if (rawSet) {
                setObj = safeJsonParse(rawSet);
                if (!setObj || typeof setObj !== 'object' || Array.isArray(setObj)) {
                    alert('El campo set debe ser un JSON objeto ({}).');
                    return;
                }
            }

            // remove
            const rawRem = (inpRemove.value || '').trim();
            const remArr = rawRem
                ? rawRem.split(',').map(x => (x || '').trim()).filter(x => !!x)
                : [];

            const next = {};
            if (setObj) next.set = setObj;
            if (remArr.length) next.remove = remArr;

            node.params = next;
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
        body.appendChild(sSet);
        body.appendChild(sRemove);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
