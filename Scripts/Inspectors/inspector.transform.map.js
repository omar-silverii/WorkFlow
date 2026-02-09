; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function safeJsonParse(s) {
        try { return JSON.parse(s); } catch (e) { return null; }
    }

    register('transform.map', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Transform Map';
        if (sub) sub.textContent = 'Construye un objeto (output) a partir de un map declarativo';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // output
        const inpOut = el('input', 'input');
        inpOut.placeholder = 'payload';
        inpOut.value = (p.output || 'payload');
        const sOut = section('output (path) - ej: payload.sql, biz.doc', inpOut);

        // overwrite
        const chk = el('input');
        chk.type = 'checkbox';
        chk.checked = (p.overwrite === undefined) ? true : !!p.overwrite;
        const sOv = section('overwrite (pisar output)', chk);

        // map json
        const taMap = el('textarea', 'input');
        taMap.rows = 10;
        taMap.placeholder = '{ "sql.params.Numero": "${input.ocNumero}", "sql.params.Importe": "${input.ocImporte}" }';

        let mapVal = p.map;
        if (typeof mapVal === 'string') taMap.value = mapVal;
        else if (mapVal && typeof mapVal === 'object') {
            try { taMap.value = JSON.stringify(mapVal, null, 2); } catch (e) { taMap.value = ''; }
        } else taMap.value = '';

        const sMap = section('map (JSON object)', taMap);

        // buttons
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Transform Map';

            const out = (inpOut.value || '').trim() || 'payload';

            const raw = (taMap.value || '').trim();
            const mapObj = raw ? safeJsonParse(raw) : null;
            if (!mapObj || typeof mapObj !== 'object' || Array.isArray(mapObj)) {
                alert('map debe ser un JSON objeto ({}).');
                return;
            }

            node.params = {
                output: out,
                overwrite: chk.checked,
                map: mapObj
            };

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
        body.appendChild(sOut);
        body.appendChild(sOv);
        body.appendChild(sMap);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
