(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function opt(sel, value, text) {
        const o = document.createElement('option');
        o.value = value;
        o.textContent = text;
        sel.appendChild(o);
    }

    function getDocTiposList() {
        // Intentamos soportar varios nombres sin romper nada
        // Esperado: [{ codigo:"NOTA_PEDIDO", nombre:"Nota..." }, ...]
        const a = window.WF_DocTipos;
        const b = window.DOC_TIPOS;
        const list = Array.isArray(a) ? a : (Array.isArray(b) ? b : null);
        if (!list) return null;

        return list
            .map(x => ({
                codigo: (x.codigo || x.Codigo || x.docTipoCodigo || x.DocTipoCodigo || '').toString().trim(),
                nombre: (x.nombre || x.Nombre || x.label || '').toString().trim()
            }))
            .filter(x => x.codigo);
    }

    register('doc.load', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Documento: Cargar';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // =======================
        // 1) Etiqueta
        // =======================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || 'Documento: Cargar';
        const sLbl = section('Etiqueta', inpLbl);

        // =======================
        // 2) Ruta (multilínea)
        // =======================
        const inpPath = el('textarea', 'input');
        inpPath.rows = 3;
        inpPath.style.resize = 'vertical';
        inpPath.value = (p.path != null ? String(p.path) : '');
        inpPath.placeholder = 'Ruta del archivo en servidor (puede usar ${...})';
        const sPath = section('Ruta del archivo (servidor)', inpPath);

        // =======================
        // 3) DocTipo (simple)
        // - si existe catálogo, lo damos como combo
        // - si no existe, input libre
        // =======================
        const list = getDocTiposList();
        let ctlDocTipo;

        if (list && list.length) {
            const sel = el('select', 'input');
            opt(sel, '', '— Auto / Sin seleccionar —');
            list.forEach(d => opt(sel, d.codigo, d.nombre ? `${d.codigo} — ${d.nombre}` : d.codigo));
            sel.value = (p.docTipoCodigo || '');
            ctlDocTipo = sel;
        } else {
            const inp = el('input', 'input');
            inp.value = (p.docTipoCodigo || '');
            inp.placeholder = 'DocTipo (opcional) ej: ORDEN_COMPRA';
            ctlDocTipo = inp;
        }

        const sDocTipo = section('Tipo de documento (opcional)', ctlDocTipo);

        // =======================
        // 4) Modo (auto por defecto)
        // =======================
        const selModo = el('select', 'input');
        ['auto', 'pdf', 'word', 'text', 'image'].forEach(m => opt(selModo, m, m));
        selModo.value = (p.mode || 'auto');
        const sModo = section('Modo', selModo);

        // =======================
        // 5) Info (convención)
        // =======================
        const info = document.createElement('div');
        info.style.fontSize = '12px';
        info.style.opacity = '0.8';
        info.innerHTML = `
      <b>Convención (modo simple):</b><br>
      El runtime guarda SIEMPRE en <code>biz.{ContextPrefix}.*</code> según el DocTipo.<br>
      Ej: <code>biz.np.numero</code>, <code>biz.oc.total</code>, etc.<br>
      <br>
      <b>Metadatos:</b> <code>wf.docTipoId</code>, <code>wf.docTipoCodigo</code>, <code>wf.contextPrefix</code>
    `;
        const sInfo = section('Información', info);

        // =======================
        // Botones
        // =======================
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;

            const docTipoCodigo = (ctlDocTipo.value || '').trim();

            // PARAMS MINIMOS (modo simple)
            node.params = {
                path: (inpPath.value || '').trim(),
                mode: (selModo.value || 'auto').trim(),
                // opcional: si viene vacío, el runtime puede resolver por otras vías
                docTipoCodigo: docTipoCodigo || ''
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
            // 1) borrar edges relacionados
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }

            // 2) borrar nodo del array
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) ctx.nodes.splice(i, 1);
                }
            }

            // 3) borrar del DOM
            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            // 4) refrescar edges y salir del inspector
            try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges after delete', e); }
            try { ctx.select(null); } catch (e) { console.warn('select(null) after delete', e); }
        };

        // Render
        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sDocTipo);
        body.appendChild(sModo);
        body.appendChild(sInfo);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
