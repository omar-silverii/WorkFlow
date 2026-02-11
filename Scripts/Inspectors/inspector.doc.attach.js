// Scripts/Inspectors/inspector.doc.attach.js
(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function toBool(v, defVal) {
        if (v === true) return true;
        if (v === false) return false;
        if (v == null) return defVal;
        const s = String(v).trim().toLowerCase();
        if (s === "1" || s === "true" || s === "yes" || s === "si" || s === "sí" || s === "y") return true;
        if (s === "0" || s === "false" || s === "no" || s === "n") return false;
        return defVal;
    }

    function safeJsonParse(txt) {
        try { return JSON.parse(txt); } catch { return null; }
    }

    function setNodeTitle(ctx, node) {
        const elNode = ctx.nodeEl(node.id);
        if (elNode) {
            const t = elNode.querySelector('.node__title');
            if (t) t.textContent = node.label || '';
        }
    }

    register('doc.attach', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Documento: Adjuntar (DMS)';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        inpLbl.addEventListener('input', () => {
            node.label = inpLbl.value;
            ctx.refreshNode(node.id);
        });
        body.appendChild(section('Etiqueta (label)', inpLbl));

        // mode
        const selMode = el('select', 'input');
        ['attachment', 'root'].forEach(v => {
            const opt = el('option');
            opt.value = v;
            opt.textContent = (v === 'root' ? 'RootDoc (biz.case.rootDoc)' : 'Attachment (biz.case.attachments[])');
            selMode.appendChild(opt);
        });
        selMode.value = (p.mode || 'attachment');
        selMode.addEventListener('change', () => { p.mode = selMode.value; node.params = p; });
        body.appendChild(section('Modo (mode)', selMode));

        // doc: path or object
        const inpDocPath = el('input', 'input');
        inpDocPath.placeholder = 'Path en estado (ej: biz.doc.search.items.0) o ${...}';
        inpDocPath.value = (typeof p.doc === 'string') ? p.doc : '';
        inpDocPath.addEventListener('input', () => {
            const v = inpDocPath.value.trim();
            if (v) p.doc = v;
            node.params = p;
        });
        body.appendChild(section('Documento por path (doc = string)', inpDocPath));

        const taDoc = el('textarea', 'textarea');
        taDoc.placeholder = '{ "documentoId":"", "carpetaId":"", "ficheroId":"", "tipo":"", "indices":{}, "viewerUrl":"" }';
        taDoc.value = (p.doc && typeof p.doc === 'object') ? JSON.stringify(p.doc, null, 2) : '';
        taDoc.style.minHeight = '120px';
        taDoc.addEventListener('blur', () => {
            if (!taDoc.value.trim()) return;
            const obj = safeJsonParse(taDoc.value);
            if (obj) { p.doc = obj; node.params = p; taDoc.style.borderColor = ''; }
            else { taDoc.style.borderColor = '#dc3545'; }
        });
        body.appendChild(section('Documento inline (doc = object) — JSON', taDoc));

        // attachToCurrentTask
        const chkTask = el('input');
        chkTask.type = 'checkbox';
        chkTask.checked = toBool(p.attachToCurrentTask, false);
        chkTask.addEventListener('change', () => { p.attachToCurrentTask = chkTask.checked; node.params = p; });
        body.appendChild(section('Asociar a tarea actual (doc.tareaId = ${wf.tarea.id})', chkTask));

        // taskId override
        const inpTaskId = el('input', 'input');
        inpTaskId.placeholder = 'Opcional: fuerza doc.tareaId (si no, usa tarea actual)';
        inpTaskId.value = p.taskId || '';
        inpTaskId.addEventListener('input', () => {
            const v = inpTaskId.value.trim();
            if (!v) delete p.taskId; else p.taskId = v;
            node.params = p;
        });
        body.appendChild(section('taskId (override)', inpTaskId));

        // output
        const inpOut = el('input', 'input');
        inpOut.placeholder = 'Ej: biz.case.lastAttach';
        inpOut.value = p.output || '';
        inpOut.addEventListener('input', () => { p.output = inpOut.value; node.params = p; });
        body.appendChild(section('Salida ACK (output path)', inpOut));

        // persist
        node.params = p;

        // ===== Botones estándar =====
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = (inpLbl.value || '').trim() || node.label || 'Documento: Adjuntar (DMS)';
            node.params = p;

            ensurePosition(node);
            setNodeTitle(ctx, node);

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

        body.appendChild(rowButtons(bSave, bDel));
    });
})();
