(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    console.log('[inspector.util.docTipo.resolve] LOADED v=dev9');

    const API = '/Api/Generico.ashx';

    async function apiDocTipoList() {
        const url = `${API}?action=doctipo.list`;
        const r = await fetch(url, { cache: 'no-store' });
        if (!r.ok) throw new Error(`doctipo.list HTTP ${r.status}`);
        return await r.json(); // [{ id, codigo, nombre }]
    }

    async function apiDocTipoRulesJson(codigo) {
        const url = `${API}?action=doctipo.rules&codigo=${encodeURIComponent(codigo || '')}`;
        const r = await fetch(url, { cache: 'no-store' });
        if (!r.ok) throw new Error(`doctipo.rules HTTP ${r.status}`);
        return await r.json(); // ← JSON REAL
    }

    function setNodeTitle(ctx, node) {
        const elNode = ctx.nodeEl(node.id);
        if (elNode) {
            const t = elNode.querySelector('.node__title');
            if (t) t.textContent = node.label || '';
        }
    }

    register('util.docTipo.resolve', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Documento: Resolver tipo';
        if (sub) sub.textContent = node.key || '';

        // === Label visual del nodo ===
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // === Combo DocTipo + botón force ===
        const wrapCombo = el('div', '');
        wrapCombo.style.display = 'flex';
        wrapCombo.style.gap = '8px';
        wrapCombo.style.alignItems = 'center';

        const sel = el('select', 'input');
        sel.style.width = '100%';

        const bForce = btn('↻ Tabla');
        bForce.title = 'Forzar cargar rulesJson desde la tabla (API)';

        wrapCombo.appendChild(sel);
        wrapCombo.appendChild(bForce);

        const sCodigo = section('DocTipo (desde catálogo)', wrapCombo);

        // === rulesJson textarea ===
        const taRules = el('textarea', 'textarea');
        taRules.rows = 10;
        taRules.wrap = 'off';
        taRules.spellcheck = false;
        // 👉 ACÁ (esto preguntaste “¿dónde?”)
        taRules.style.fontFamily = 'Consolas, Monaco, monospace';
        taRules.style.fontSize = '12px';
        // Validación JSON en vivo (global)
        if (window.WF_Json && typeof WF_Json.attachValidator === 'function') {
            WF_Json.attachValidator(taRules);
        }

        // Cargar desde params actuales
        {
            const p = node.params || {};
            taRules.value = (p.rulesJson !== undefined && p.rulesJson !== null) ? String(p.rulesJson) : '';
        }

        const hint = el('div', 'hint');
        hint.innerHTML =
            'Reglas JSON (legacy) asociadas al DocTipo. Si lo dejás vacío y guardás, queda guardado como “vacío intencional” (no se auto-completa solo).';

        // Contenedor para botón pequeño arriba del textarea
        const topRow = el('div', '');
        topRow.style.display = 'flex';
        topRow.style.justifyContent = 'flex-end';
        topRow.style.gap = '8px';

        const bFormat = btn('Formatear JSON');
        bFormat.title = 'Formatea (pretty print) el JSON del textarea';
        topRow.appendChild(bFormat);

        const sRules = section('Reglas JSON (rulesJson)', taRules);
        sRules.insertBefore(topRow, taRules);   // botón arriba del textarea
        sRules.appendChild(hint);

        // Validación + formateo global
        if (window.WF_Json && typeof WF_Json.attachValidator === 'function') {
            WF_Json.attachValidator(taRules);
        }
        if (window.WF_Json && typeof WF_Json.attachFormatterButton === 'function') {
            WF_Json.attachFormatterButton(bFormat, taRules);
        }


        // === Ayuda / salida ===
        const info = el('div', 'muted');
        info.style.padding = '6px 0';
        info.innerHTML =
            '<div><b>Salida en contexto:</b></div>' +
            '<div>• wf.docTipoCodigo</div>' +
            '<div>• wf.docTipoId</div>' +
            '<div>• wf.contextPrefix</div>';

        // === Botones estándar ===
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // ========= Carga del combo =========
        let listLoaded = false;

        async function loadComboAndSelect() {
            if (listLoaded) return;
            listLoaded = true;

            sel.innerHTML = '';
            const opt0 = document.createElement('option');
            opt0.value = '';
            opt0.textContent = '(seleccioná un DocTipo)';
            sel.appendChild(opt0);

            try {
                const items = await apiDocTipoList();
                (items || []).forEach(it => {
                    const o = document.createElement('option');
                    o.value = it.codigo || '';
                    o.textContent = (it.codigo || '') + (it.nombre ? ` — ${it.nombre}` : '');
                    sel.appendChild(o);
                });

                const p = node.params || {};
                const current = (p.docTipoCodigo || '').trim();
                if (current) sel.value = current;
            } catch (e) {
                console.warn('[util.docTipo.resolve] doctipo.list error', e);
            }
        }

        // ========= Auto/Force load rules =========
        async function maybeAutoLoadRulesByChange(codigo) {
            const p = node.params || {};
            const savedEmpty = (p.rulesJsonEmpty === true);
            const hasText = !!(taRules.value || '').trim();
            if (!codigo) return;

            // Auto SOLO si está vacío y NO fue guardado vacío intencionalmente
            if (!hasText && !savedEmpty) {
                try {
                    const rules = await apiDocTipoRulesJson(codigo);
                    taRules.value = JSON.stringify(rules, null, 2);
                } catch (e) {
                    console.warn('[util.docTipo.resolve] doctipo.rules (auto) error', e);
                }
            }
        }

        async function forceLoadRules(codigo) {
            if (!codigo) return;
            try {
                const rules = await apiDocTipoRulesJson(codigo);
                taRules.value = JSON.stringify(rules, null, 2);
                // Si forzás, deja de ser "vacío intencional"
                if (node.params) delete node.params.rulesJsonEmpty;
            } catch (e) {
                console.warn('[util.docTipo.resolve] doctipo.rules (force) error', e);
            }
        }

        sel.addEventListener('change', async () => {
            const codigo = (sel.value || '').trim();
            await maybeAutoLoadRulesByChange(codigo);
        });

        bForce.onclick = async () => {
            const codigo = (sel.value || '').trim();
            await forceLoadRules(codigo);
        };

        // ========= Botones =========
        bTpl.onclick = async () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.docTipo.resolve']) || {};
            await loadComboAndSelect();

            if (def.docTipoCodigo) sel.value = def.docTipoCodigo;

            if (def.rulesJson !== undefined) {
                taRules.value = (def.rulesJson || '');
                if (node.params) delete node.params.rulesJsonEmpty;
            }
        };

        bSave.onclick = () => {
            node.label = (inpLbl.value || '').trim() || node.label || 'Documento: Resolver tipo';

            const codigo = (sel.value || '').trim();
            const rules = (taRules.value || '').trim();

            const newParams = { docTipoCodigo: codigo };

            if (rules) {
                newParams.rulesJson = rules;
            } else {
                // tu requisito: que quede “sin rulesJson”, pero recordando que fue intencional
                newParams.rulesJsonEmpty = true;
            }

            node.params = newParams;

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

        // Render
        body.appendChild(sLbl);
        body.appendChild(sCodigo);
        body.appendChild(sRules);
        body.appendChild(info);
        body.appendChild(rowButtons(bTpl, bSave, bDel));

        // Inicializar combo + auto-load 1 vez
        loadComboAndSelect().then(async () => {
            const codigo = (sel.value || '').trim();
            if (codigo) await maybeAutoLoadRulesByChange(codigo);
        });
    });
})();

