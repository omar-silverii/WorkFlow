; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    const asBool = (value, fallback) => {
        if (value === undefined || value === null || value === '') return !!fallback;
        if (typeof value === 'boolean') return value;
        const text = String(value).trim().toLowerCase();
        if (text === 'true' || text === '1' || text === 'si' || text === 'sí') return true;
        if (text === 'false' || text === '0' || text === 'no') return false;
        return !!fallback;
    };

    const selectWithOptions = (items, value) => {
        const sel = el('select', 'input');
        items.forEach(item => {
            const opt = document.createElement('option');
            opt.value = item.value;
            opt.textContent = item.label;
            sel.appendChild(opt);
        });
        sel.value = value;
        return sel;
    };

    register('file.read', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Archivo: Leer';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['file.read']) || {};

        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const inpPath = el('textarea', 'input');
        inpPath.rows = 5;
        inpPath.style.resize = 'vertical';
        inpPath.style.fontFamily = 'monospace';
        inpPath.style.fontSize = '12px';
        inpPath.value = (p.path != null ? String(p.path) : (tpl.path != null ? String(tpl.path) : ''));
        inpPath.placeholder = 'Ej: \\SERVIDOR\\carpeta\\archivo.txt';
        const sPath = section('Ruta del archivo (servidor)', inpPath);

        const inpEnc = el('input', 'input');
        inpEnc.value = (p.encoding || tpl.encoding || 'utf-8');
        inpEnc.placeholder = 'utf-8 / latin1 / windows-1252';
        const sEnc = section('Encoding', inpEnc);

        // Compatibilidad: HFileRead acepta output, pero el Constructor D3 guarda canónicamente salida.
        const inpSalida = el('input', 'input');
        inpSalida.value = (p.salida || p.output || 'archivo');
        inpSalida.placeholder = 'Ej: archivo.texto o biz.archivo.texto';

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

        const inpAsJson = el('input', 'input');
        inpAsJson.type = 'checkbox';
        inpAsJson.checked = asBool(p.asJson, false);
        const sAsJson = section('Interpretar contenido como JSON (asJson)', inpAsJson);

        const inpZipMode = selectWithOptions([
            { value: 'auto', label: 'Auto' },
            { value: 'none', label: 'Sin compresión' },
            { value: 'zip', label: 'ZIP' },
            { value: 'gzip', label: 'GZIP' }
        ], String(p.zipMode || 'auto').toLowerCase());
        const sZipMode = section('Compresión', inpZipMode);

        const inpZipEntry = el('input', 'input');
        inpZipEntry.value = p.zipEntry || '';
        inpZipEntry.placeholder = 'Opcional. Ej: datos.json';
        const sZipEntry = section('Entrada ZIP', inpZipEntry);

        const inpUseCache = el('input', 'input');
        inpUseCache.type = 'checkbox';
        inpUseCache.checked = asBool(p.useCache, true);
        const sUseCache = section('Usar caché al reanudar', inpUseCache);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;

            node.params = {
                path: inpPath.value || '',
                salida: inpSalida.value || 'archivo',
                asJson: !!inpAsJson.checked,
                encoding: inpEnc.value || 'utf-8',
                zipMode: inpZipMode.value || 'auto',
                useCache: !!inpUseCache.checked
            };
            if ((inpZipEntry.value || '').trim()) node.params.zipEntry = inpZipEntry.value.trim();

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

        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sSalida);
        body.appendChild(sAsJson);
        body.appendChild(sEnc);
        body.appendChild(sZipMode);
        body.appendChild(sZipEntry);
        body.appendChild(sUseCache);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
