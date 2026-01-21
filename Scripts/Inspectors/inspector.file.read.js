; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('file.read', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        // Título y subtítulo del inspector
        if (title) title.textContent = node.label || 'Archivo: Leer';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['file.read']) || {};

        // =======================
        // 1) Etiqueta del nodo
        // =======================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // =======================
        // 2) Ruta del archivo
        // =======================
        const inpPath = el('input', 'input');
        inpPath.value = p.path || tpl.path || '';
        const sPath = section('Ruta del archivo (servidor)', inpPath);

        // =======================
        // 3) Encoding
        // =======================
        const inpEnc = el('input', 'input');
        inpEnc.value = p.encoding || tpl.encoding || 'utf-8';
        const sEnc = section('Encoding (ej: utf-8, latin1)', inpEnc);

        // =======================
        // 4) Salida en contexto
        // =======================
        const inpSalida = el('input', 'input');
        // dónde se va a guardar el contenido en ctx.Estado
        inpSalida.value = p.salida || 'archivo';
        const sSalida = section('Salida (key en contexto)', inpSalida);

        // =======================
        // 4b) Parsear como JSON
        // =======================
        const inpAsJson = el('input', 'input');
        inpAsJson.type = 'checkbox';
        inpAsJson.checked = !!p.asJson;  // default false
        const wrapAsJson = el('div', 'section');
        wrapAsJson.innerHTML = `<div class="label">Interpretar contenido como JSON (asJson)</div>`;
        wrapAsJson.appendChild(inpAsJson);

        // =======================
        // 5) Botones
        // =======================
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            // Actualizar el label del nodo
            node.label = inpLbl.value || node.label;

            // Guardar parámetros
            node.params = {
                path: inpPath.value || '',
                encoding: inpEnc.value || 'utf-8',
                salida: inpSalida.value || 'archivo',
                asJson: !!inpAsJson.checked
            };

            // Asegurar que tenga posición persistida
            ensurePosition(node);

            // Refrescar título visual del nodo en el canvas
            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            // Re-render del inspector
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            // === FIX: redraw edges after save ===
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
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


        // =======================
        // 6) Armar DOM del inspector
        // =======================
        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sEnc);
        body.appendChild(sSalida);
        body.appendChild(wrapAsJson);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
