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

    register('file.write', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Archivo: Escribir';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // === Etiqueta del nodo ===
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // === Path del archivo destino ===
        const inpPath = el('input', 'input');
        inpPath.value = p.path || 'C:/temp/salida.txt';
        const sPath = section('Ruta del archivo destino (path)', inpPath);

        // === Origen en el contexto ===
        const inpOrigen = el('input', 'input');
        inpOrigen.value = p.origen || 'archivo';
        const sOrigen = section('Origen en contexto (key)', inpOrigen);

        // === Encoding ===
        const inpEnc = el('input', 'input');
        inpEnc.value = p.encoding || 'utf-8';
        const sEnc = section('Encoding (ej: utf-8)', inpEnc);

        // === Overwrite ===
        const ckOverwrite = checkbox('Sobrescribir si ya existe (overwrite)',
            (p.overwrite == null ? true : !!p.overwrite));
        const sOverwrite = ckOverwrite.wrap;

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;

            const next = {
                path: inpPath.value || '',
                origen: inpOrigen.value || 'archivo',
                encoding: inpEnc.value || 'utf-8',
                overwrite: !!ckOverwrite.input.checked
            };

            const pos = (p.position && typeof p.position === 'object')
                ? p.position
                : { x: node.x | 0, y: node.y | 0 };
            next.position = pos;

            node.params = next;
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

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


        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sOrigen);
        body.appendChild(sEnc);
        body.appendChild(sOverwrite);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
