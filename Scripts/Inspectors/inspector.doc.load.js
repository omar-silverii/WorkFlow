(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('doc.load', (node, ctx, dom) => {

        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';

        // TÍTULO Y SUBTÍTULO
        if (title) title.textContent = node.label || 'Cargar documento';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // ===============================
        // 1) Label del nodo
        // ===============================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || 'Cargar documento';
        const sLbl = section('Etiqueta', inpLbl);

        // ===============================
        // 2) Ruta del archivo
        // ===============================
        const inpPath = el('input', 'input');
        inpPath.value = p.path || '';
        const sPath = section('Ruta del archivo', inpPath);

        // ===============================
        // 3) Modo
        // ===============================
        const selModo = el('select', 'input');
        ['auto', 'pdf', 'word', 'image'].forEach(m => {
            const o = document.createElement('option');
            o.value = m;
            o.textContent = m;
            if ((p.mode || 'auto') === m) o.selected = true;
            selModo.appendChild(o);
        });
        const sModo = section('Modo', selModo);

        // ===============================
        // 4) Información fija (READ ONLY)
        // ===============================
        const info = document.createElement('div');
        info.style.fontSize = "12px";
        info.style.opacity = "0.7";
        info.style.marginTop = "4px";
        info.innerHTML = `
            <b>Salida fija:</b><br>
            input.filename<br>
            input.text
        `;
        const sInfo = section('Información', info);

        // ===============================
        // 5) Botones
        // ===============================
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // GUARDAR
        bSave.onclick = () => {
            node.label = inpLbl.value || 'Cargar documento';

            // ⚠️ YA NO SE GUARDA salidaPrefix
            node.params = {
                path: inpPath.value || '',
                mode: selModo.value || 'auto'
            };

            ensurePosition(node);

            // Actualizar título en el canvas
            const nd = nodeEl(node.id);
            if (nd) {
                const t = nd.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);

            // Redibujar edges después de guardar
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
        };

        // ELIMINAR NODO
        bDel.onclick = () => {

            // 1. Eliminar edges
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (e.from === node.id || e.to === node.id) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            // 2. Eliminar nodo
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    if (ctx.nodes[i].id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            // 3. Quitar del canvas
            const nd = nodeEl(node.id);
            if (nd) nd.remove();

            // 4. Redibujar edges
            ctx.drawEdges();

            // 5. Limpiar selección
            ctx.select(null);
        };

        // ===============================
        // 6) Armar DOM final
        // ===============================
        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sModo);
        body.appendChild(sInfo);
        body.appendChild(rowButtons(bSave, bDel));

    });
})();
