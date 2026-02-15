; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function opt(sel, value, text) {
        const o = document.createElement('option');
        o.value = value; o.textContent = text;
        sel.appendChild(o);
    }

    function setVisible(elm, vis) {
        if (!elm) return;
        elm.style.display = vis ? '' : 'none';
    }

    register('control.if', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom; body.innerHTML = '';
        if (title) title.textContent = node.label || 'If';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // ===== Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // ===== Plantillas (simple + legacy)
        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if.templates']) || {};
            opt(selTpl, '', '— Elegir —');
            Object.keys(pack).forEach(k => {
                const t = pack[k] || {};
                opt(selTpl, k, (t.label || k));
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        // ===== Modo SIMPLE (recomendado para administrativos)
        const inpField = el('input', 'input'); inpField.value = p.field || '';
        inpField.placeholder = 'Ej: payload.status o biz.doc.search.count';

        const selOp = el('select', 'input');
        opt(selOp, '==', 'Igual (=)');
        opt(selOp, '!=', 'Distinto (!=)');
        opt(selOp, '>=', 'Mayor o igual (>=)');
        opt(selOp, '<=', 'Menor o igual (<=)');
        opt(selOp, '>', 'Mayor (>)');
        opt(selOp, '<', 'Menor (<)');
        opt(selOp, 'contains', 'Contiene');
        opt(selOp, 'not_contains', 'No contiene');
        opt(selOp, 'starts_with', 'Empieza con');
        opt(selOp, 'ends_with', 'Termina con');
        opt(selOp, 'exists', 'Existe');
        opt(selOp, 'not_exists', 'No existe');
        opt(selOp, 'empty', 'Vacío');
        opt(selOp, 'not_empty', 'No vacío');

        selOp.value = p.op || '==';

        const inpVal = el('input', 'input'); inpVal.value = p.value || '';
        inpVal.placeholder = 'Valor (puede usar ${...})';

        const sField = section('Campo (path)', inpField);
        const sOp = section('Operador', selOp);
        const sVal = section('Valor', inpVal);

        function refreshValueVisibility() {
            const opv = (selOp.value || '').toLowerCase();
            const needs = !(opv === 'exists' || opv === 'not_exists' || opv === 'empty' || opv === 'not_empty');
            setVisible(sVal, needs);
        }
        selOp.onchange = refreshValueVisibility;
        refreshValueVisibility();

        // ===== Modo AVANZADO (oculto por defecto)
        const chkAdv = el('input', 'input');
        chkAdv.type = 'checkbox';
        chkAdv.checked = !!p.expression; // si ya venía legacy, lo mostramos al usuario
        const sAdvToggle = section('Modo avanzado (solo admins)', chkAdv);

        const inpExpr = el('input', 'input'); inpExpr.value = p.expression || '';
        inpExpr.placeholder = 'Ej: ${payload.status} == 200';
        const sExpr = section('Expresión (legacy)', inpExpr);

        function refreshAdvancedVisibility() {
            setVisible(sExpr, !!chkAdv.checked);
        }
        chkAdv.onchange = refreshAdvancedVisibility;
        refreshAdvancedVisibility();

        // ===== Botones
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if.templates']) || {};
            const tpl = (selTpl.value && pack[selTpl.value]) ? pack[selTpl.value] : ((window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.if']) || {});

            // Plantillas simples
            if (tpl.field || tpl.op || tpl.value) {
                inpField.value = tpl.field || '';
                selOp.value = tpl.op || '==';
                inpVal.value = (tpl.value != null ? String(tpl.value) : '');
                // al usar simple, NO activamos avanzado y limpiamos expression
                chkAdv.checked = false;
                inpExpr.value = '';
                refreshValueVisibility();
                refreshAdvancedVisibility();
                return;
            }

            // Legacy
            chkAdv.checked = true;
            inpExpr.value = tpl.expression || '${payload.status} == 200';
            refreshAdvancedVisibility();
        };

        bSave.onclick = () => {
            const next = Object.assign({}, node.params || {});

            const field = (inpField.value || '').trim();
            const op = (selOp.value || '').trim();
            const val = (inpVal.value || '').trim();

            const adv = !!chkAdv.checked;
            const expr = (inpExpr.value || '').trim();

            // REGLA EMPRESARIAL:
            // - Si NO está tildado "avanzado": siempre guardamos SIMPLE y borramos expression
            // - Si está tildado "avanzado": guardamos expression (si tiene algo) y borramos simple
            if (!adv) {
                next.field = field;
                next.op = op;
                if (val) next.value = val; else delete next.value;
                delete next.expression;
                inpExpr.value = ''; // evita que quede algo “viejo” visualmente
            } else {
                if (!expr) {
                    // si lo activó pero no escribió, lo tratamos como simple igualmente
                    next.field = field;
                    next.op = op;
                    if (val) next.value = val; else delete next.value;
                    delete next.expression;
                    chkAdv.checked = false;
                    inpExpr.value = '';
                    refreshAdvancedVisibility();
                } else {
                    next.expression = expr;
                    delete next.field; delete next.op; delete next.value;
                }
            }

            node.label = inpLbl.value || node.label;
            node.params = next;

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
        body.appendChild(sTpl);

        body.appendChild(sField);
        body.appendChild(sOp);
        body.appendChild(sVal);

        body.appendChild(sAdvToggle);
        body.appendChild(sExpr);

        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
