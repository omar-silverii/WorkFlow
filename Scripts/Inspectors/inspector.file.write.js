; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('file.write', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Archivo: Escribir';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['file.write']) || {};

        // 1) Etiqueta
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // 2) Ruta de salida
        const inpPath = el('input', 'input');
        inpPath.value = p.path || tpl.path || 'C:/data/salida.json';
        const sPath = section('Ruta del archivo (servidor)', inpPath);

        // 3) Encoding
        const inpEnc = el('input', 'input');
        inpEnc.value = p.encoding || tpl.encoding || 'utf-8';
        const sEnc = section('Encoding (ej: utf-8, latin1)', inpEnc);

        // 4) Overwrite
        const wrapOv = el('div', 'section');
        const idOv = 'chk_ov_' + Math.random().toString(36).slice(2);
        wrapOv.innerHTML = '<label><input type="checkbox" id="' + idOv + '"> Sobrescribir si existe</label>';
        const chkOv = wrapOv.querySelector('#' + idOv);
        chkOv.checked = (p.overwrite != null) ? !!p.overwrite : (tpl.overwrite != null ? !!tpl.overwrite : true);

        // 5) Origen en contexto (qué se guarda en el archivo)
        const inpOrigen = el('input', 'input');
        inpOrigen.value = p.origen || 'archivo';
        const sOrigen = section('Origen en contexto (key, ej: payload, archivo, solicitud)', inpOrigen);

        // Botones
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;

            node.params = {
                path: inpPath.value || '',
                encoding: inpEnc.value || 'utf-8',
                overwrite: !!chkOv.checked,
                origen: inpOrigen.value || 'archivo'
            };

            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

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
        body.appendChild(sPath);
        body.appendChild(sEnc);
        body.appendChild(wrapOv);
        body.appendChild(sOrigen);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
