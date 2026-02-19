; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('file.read', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Archivo: Leer';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['file.read']) || {};

        // =======================
        // 1) Etiqueta
        // =======================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // =======================
        // 2) Ruta del archivo (textarea grande)
        // =======================
        const inpPath = el('textarea', 'input');
        inpPath.rows = 5;
        inpPath.style.resize = 'vertical';
        inpPath.style.fontFamily = 'monospace';
        inpPath.style.fontSize = '12px';
        inpPath.value = (p.path != null ? String(p.path) : (tpl.path != null ? String(tpl.path) : ''));
        inpPath.placeholder = 'Ej: \\\\SERVIDOR\\carpeta\\archivo.txt';
        const sPath = section('Ruta del archivo (servidor)', inpPath);

        // =======================
        // 3) Encoding
        // =======================
        const inpEnc = el('input', 'input');
        inpEnc.value = (p.encoding || tpl.encoding || 'utf-8');
        inpEnc.placeholder = 'utf-8 / latin1 / windows-1252';
        const sEnc = section('Encoding', inpEnc);

        // =======================
        // 4) Salida (key)
        // Compat: acepta p.output, guardamos siempre "salida"
        // =======================
        const inpSalida = el('input', 'input');
        inpSalida.value = (p.salida || p.output || 'archivo');
        inpSalida.placeholder = 'Ej: input.raw o file.text';

        const btnPickSalida = btn('Elegir…');
        btnPickSalida.style.marginTop = '6px';

        const salidaWrap = el('div');
        salidaWrap.appendChild(inpSalida);
        salidaWrap.appendChild(btnPickSalida);

        const sSalida = section('Salida (key en contexto)', salidaWrap);

        btnPickSalida.onclick = () => {
            if (!window.WF_FieldPicker) { alert('WF_FieldPicker no está cargado'); return; }
            window.WF_FieldPicker.open({
                ctx,
                title: 'Elegir campo (contexto)',
                onPick: (v) => { inpSalida.value = v; }
            });
        };


        // =======================
        // 5) asJson (checkbox)
        // =======================
        const inpAsJson = el('input', 'input');
        inpAsJson.type = 'checkbox';
        inpAsJson.checked = !!p.asJson;
        const sAsJson = section('Interpretar contenido como JSON (asJson)', inpAsJson);

        // =======================
        // Botones
        // =======================
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;

            node.params = {
                path: inpPath.value || '',
                encoding: (inpEnc.value || 'utf-8'),
                salida: (inpSalida.value || 'archivo'),
                asJson: !!inpAsJson.checked
            };

            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);

            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
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

        // =======================
        // Render
        // =======================
        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sEnc);
        body.appendChild(sSalida);
        body.appendChild(sAsJson);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
