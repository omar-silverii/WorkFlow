; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('doc.entrada', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        // Título y sub
        if (title) title.textContent = node.label || 'Documento de entrada';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // =======================
        // 1) Etiqueta del nodo
        // =======================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // =======================
        // 2) Plantillas (opcional)
        // =======================
        const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['doc.entrada.templates']) || null;
        let selTpl = null, sTpl = null;

        if (pack) {
            selTpl = el('select', 'input');

            const opt0 = document.createElement('option');
            opt0.value = '';
            opt0.textContent = '— Elegir —';
            selTpl.appendChild(opt0);

            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                selTpl.appendChild(o);
            });

            sTpl = section('Plantilla', selTpl);
        }

        // =======================
        // 3) Modo: simulado / real
        // =======================
        const selModo = el('select', 'input');
        ['simulado', 'real'].forEach(m => {
            const o = document.createElement('option');
            o.value = m;
            o.textContent = m;
            if ((p.modo || 'simulado') === m) o.selected = true;
            selModo.appendChild(o);
        });
        const sModo = section('Modo', selModo);

        // =======================
        // 4) Salida en contexto
        // =======================
        const inpSalida = el('input', 'input');
        inpSalida.value = p.salida || 'solicitud';
        const sSalida = section('Salida (key en contexto)', inpSalida);

        // =======================
        // 5) Extensiones permitidas
        //    CSV: "pdf,docx,jpg"
        //    Vacío = cualquier tipo
        // =======================
        const inpExt = el('input', 'input');
        const currentExt = Array.isArray(p.extensiones)
            ? p.extensiones.join(',')
            : (p.extensiones || 'pdf,docx');  // default amigable; si querés cualquier tipo, borrás esto.
        inpExt.value = currentExt;
        const sExt = section('Extensiones permitidas (CSV, vacío = cualquier)', inpExt);

        // =======================
        // 6) Tamaño máximo en MB
        // =======================
        const inpMax = el('input', 'input');
        inpMax.type = 'number';
        inpMax.min = '1';
        inpMax.value = (p.maxMB == null ? 10 : p.maxMB);
        const sMax = section('Tamaño máx (MB)', inpMax);

        // =======================
        // 7) Botones
        // =======================
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // Aplicar plantilla
        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['doc.entrada']) || {};
            const tplKey = selTpl && selTpl.value;
            const tpl = (tplKey && pack && pack[tplKey]) ? pack[tplKey] : def;

            selModo.value = tpl.modo || 'simulado';
            inpSalida.value = tpl.salida || 'solicitud';

            if (Array.isArray(tpl.extensiones)) {
                inpExt.value = tpl.extensiones.join(',');
            } else {
                // si la plantilla no define extensiones, dejamos vacío
                // (equivale a "cualquier tipo")
                inpExt.value = '';
            }

            inpMax.value = (tpl.maxMB == null ? 10 : tpl.maxMB);
        };

        // Guardar cambios en el nodo
        bSave.onclick = () => {
            const extensiones = (inpExt.value || '')
                .split(',')
                .map(s => s.trim())
                .filter(Boolean);   // si queda vacío => lista vacía (cualquier tipo)

            const next = {
                modo: selModo.value,
                salida: inpSalida.value || 'solicitud',
                extensiones,
                maxMB: parseInt(inpMax.value, 10) || 10
            };

            node.label = inpLbl.value || node.label;
            node.params = next;

            // Muy importante: asegurar que tenga position
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            // Re-render del inspector para mostrar lo actualizado
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            // Eliminar edges que salen o llegan a este nodo (MUTANDO el array)
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            // Eliminar el nodo del array global
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            // Quitar del DOM y refrescar
            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            ctx.drawEdges();
            ctx.select(null);
        };


        // =======================
        // 8) Armar el DOM
        // =======================
        body.appendChild(sLbl);
        if (sTpl) body.appendChild(sTpl);
        body.appendChild(sModo);
        body.appendChild(sSalida);
        body.appendChild(sExt);
        body.appendChild(sMax);

        // Fila de botones: si hay plantillas, mostramos Guardar / Eliminar / Insertar plantilla
        const buttonsRow = (pack)
            ? rowButtons(bSave, bDel, bTpl)
            : rowButtons(bSave, bDel);

        body.appendChild(buttonsRow);
    });
})();
