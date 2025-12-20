// Scripts/inspectors/inspector.doc.extract.js
(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function toBool(v) {
        if (v === true) return true;
        if (v === false) return false;
        if (v == null) return false;
        const s = String(v).trim().toLowerCase();
        return (s === "1" || s === "true" || s === "yes" || s === "si" || s === "sí" || s === "y");
    }

    function toInt(v, defVal) {
        if (v == null) return defVal;
        const n = parseInt(String(v), 10);
        return isFinite(n) ? n : defVal;
    }

    function removeNodeAndEdges(node, ctx) {
        // Eliminar edges
        if (Array.isArray(ctx.edges)) {
            for (let i = ctx.edges.length - 1; i >= 0; i--) {
                const e = ctx.edges[i];
                if (!e) continue;
                if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
            }
        }
        // Eliminar nodo
        if (Array.isArray(ctx.nodes)) {
            for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                const n = ctx.nodes[i];
                if (n && n.id === node.id) ctx.nodes.splice(i, 1);
            }
        }
        // Quitar del DOM
        const elNode = ctx.nodeEl(node.id);
        if (elNode) elNode.remove();

        ctx.drawEdges();
        ctx.select(null);
    }

    function setNodeTitle(ctx, node) {
        const elNode = ctx.nodeEl(node.id);
        if (elNode) {
            const t = elNode.querySelector('.node__title');
            if (t) t.textContent = node.label || '';
        }
    }

    function setEnabled(inputEl, enabled) {
        if (!inputEl) return;
        inputEl.disabled = !enabled;
        inputEl.style.opacity = enabled ? "" : ".65";
    }

    // Inspector mínimo para nodos tipo "doc.extract"
    register('doc.extract', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Extraer de texto';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // === Label visual del nodo ===
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // === Origen / Destino (esencial) ===
        const inpOrigen = el('input', 'input');
        inpOrigen.value = (p.origen !== undefined && p.origen !== null) ? String(p.origen) : 'input.text';
        const sOrigen = section('Origen (ctx key)', inpOrigen);

        const inpDestino = el('input', 'input');
        inpDestino.value = (p.destino !== undefined && p.destino !== null) ? String(p.destino) : '';
        const sDestino = section('Destino (opcional)', inpDestino);

        const hintOrigen = el('div', 'hint');
        hintOrigen.innerHTML =
            'En runtime, el handler toma este texto desde el contexto. ' +
            'Ejemplo típico: <b>input.text</b>.';
        sOrigen.appendChild(hintOrigen);

        // ============================================================
        // NUEVO: Modo BD (useDbRules + docTipoId) — SIN tocar rulesJson
        // ============================================================
        const chkUseDb = el('input');
        chkUseDb.type = 'checkbox';
        chkUseDb.checked = toBool(p.useDbRules);

        const lblUseDb = el('label');
        lblUseDb.style.display = 'inline-flex';
        lblUseDb.style.alignItems = 'center';
        lblUseDb.style.gap = '8px';
        lblUseDb.appendChild(chkUseDb);
        lblUseDb.appendChild(document.createTextNode('Usar reglas desde BD (useDbRules)'));

        const sDb = section('Modo BD', lblUseDb);

        const inpDocTipoId = el('input', 'input');
        inpDocTipoId.type = 'number';
        inpDocTipoId.min = '0';
        inpDocTipoId.placeholder = 'Ej: 1';
        inpDocTipoId.value = String(toInt(p.docTipoId, 0) || '');
        const sDocTipo = section('DocTipoId (solo si Modo BD)', inpDocTipoId);

        const hintDb = el('div', 'hint');
        hintDb.innerHTML =
            'Si activás <b>Modo BD</b>, el nodo carga reglas desde <code>WF_DocTipoReglaExtract</code> ' +
            'por <code>DocTipoId</code>. En este modo <b>NO</b> se usa <code>rulesJson</code>.';
        sDb.appendChild(hintDb);

        // === rulesJson (legacy) ===
        const taRules = el('textarea', 'textarea');
        taRules.rows = 12;
        taRules.wrap = 'off';
        taRules.spellcheck = false;
        taRules.style.fontFamily = 'Consolas, Monaco, monospace';
        taRules.style.fontSize = '12px';
        taRules.value = (p.rulesJson !== undefined && p.rulesJson !== null) ? String(p.rulesJson) : '';

        // Sección rulesJson
        const sRules = section('Reglas JSON (rulesJson) — LEGACY', taRules);

        // Botón "Formatear JSON" (NO estándar)
        const topRowRules = el('div', '');
        topRowRules.style.display = 'flex';
        topRowRules.style.justifyContent = 'flex-end';
        topRowRules.style.gap = '8px';

        const bFormatJson = btn('Formatear JSON');
        bFormatJson.title = 'Formatea el JSON del textarea (pretty print)';
        topRowRules.appendChild(bFormatJson);

        // Insertarlo arriba del textarea
        sRules.insertBefore(topRowRules, taRules);

        // Validación JSON en vivo (global)
        if (window.WF_Json && typeof WF_Json.attachValidator === 'function') {
            WF_Json.attachValidator(taRules);
        }

        // Click → formatear (global)
        if (window.WF_Json && typeof WF_Json.attachFormatterButton === 'function') {
            WF_Json.attachFormatterButton(bFormatJson, taRules);
        }

        const hintRules = el('div', 'hint');
        hintRules.innerHTML =
            'Legacy: se guarda tal cual en el nodo. Si lo dejás vacío y guardás, <b>no se persiste</b> la propiedad rulesJson.';
        sRules.appendChild(hintRules);

        // UI: si está en Modo BD, deshabilitamos rulesJson
        function refreshUiMode() {
            const dbOn = chkUseDb.checked === true;
            setEnabled(inpDocTipoId, dbOn);
            setEnabled(taRules, !dbOn);
            setEnabled(bFormatJson, !dbOn);

            // Si enciendo Modo BD, NO borro rulesJson (por si vuelve),
            // solo lo dejo deshabilitado para que no se use por error.
        }
        chkUseDb.addEventListener('change', refreshUiMode);
        refreshUiMode();

        // === Ayuda / salida (mantengo tu contrato legacy) ===
        const info = el('div', 'muted');
        info.style.padding = '6px 0';
        info.innerHTML =
            '<div><b>Salida:</b></div>' +
            '<div>• Escribe en contexto: <code>input.&lt;campo&gt;</code> (por cada regla).</div>';

        // === Botones estándar ===
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['doc.extract']) || {};
            if (def.label !== undefined) inpLbl.value = def.label || '';
            if (def.origen !== undefined) inpOrigen.value = def.origen || 'input.text';
            if (def.destino !== undefined) inpDestino.value = def.destino || '';
            if (def.rulesJson !== undefined) taRules.value = def.rulesJson || '';

            // opcional templates para BD (si existieran)
            if (def.useDbRules !== undefined) chkUseDb.checked = toBool(def.useDbRules);
            if (def.docTipoId !== undefined) inpDocTipoId.value = String(toInt(def.docTipoId, 0) || '');

            refreshUiMode();
        };

        bSave.onclick = () => {
            node.label = (inpLbl.value || '').trim() || node.label || 'Extraer de texto';

            const origen = (inpOrigen.value || '').trim() || 'input.text';
            const destino = (inpDestino.value || '').trim();

            const dbOn = chkUseDb.checked === true;
            const docTipoId = toInt(inpDocTipoId.value, 0);

            const newParams = { origen: origen };
            if (destino) newParams.destino = destino;

            if (dbOn) {
                // ✅ MODO BD: NO guardamos rulesJson
                newParams.useDbRules = true;
                if (docTipoId > 0) newParams.docTipoId = docTipoId;
            } else {
                // ✅ LEGACY: guardamos rulesJson SOLO si hay contenido
                const rules = (taRules.value || '').trim();
                if (rules) newParams.rulesJson = rules;

                // por las dudas, NO dejamos un useDbRules viejo
                // (si el usuario lo desactivó)
                // (no hace falta setear false)
            }

            node.params = newParams;

            ensurePosition(node);
            setNodeTitle(ctx, node);

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);

            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { /* noop */ }
            }, 0);
        };

        bDel.onclick = () => removeNodeAndEdges(node, ctx);

        // Render
        body.appendChild(sLbl);
        body.appendChild(sOrigen);
        body.appendChild(sDestino);

        body.appendChild(sDb);
        body.appendChild(sDocTipo);

        body.appendChild(sRules);
        body.appendChild(info);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
