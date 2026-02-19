; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function safeObj(v) {
        if (!v || typeof v !== 'object') return {};
        return v;
    }

    register('control.switch', (node, ctx, dom) => {
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Switch';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Field opcional (si querés estandarizar después)
        const inpField = el('input', 'input'); inpField.value = p.field || '';
        inpField.placeholder = 'Opcional: payload.status / biz.xxx (para futuro)';
        const sField = section('Campo (opcional)', inpField);

        // Default label (edge condition)
        const inpDef = el('input', 'input'); inpDef.value = p.default || '';
        inpDef.placeholder = 'Ej: default';
        const sDef = section('Salida default (Condition)', inpDef);

        // Casos: grid simple
        const casos = safeObj(p.casos);
        const wrap = el('div', '');
        wrap.style.display = 'flex';
        wrap.style.flexDirection = 'column';
        wrap.style.gap = '6px';

        function addRow(k, v) {
            const row = el('div', '');
            row.style.display = 'grid';
            row.style.gridTemplateColumns = '160px 1fr auto';
            row.style.gap = '6px';
            row.style.alignItems = 'center';

            const inpKey = el('input', 'input');
            inpKey.value = k || '';
            inpKey.placeholder = 'Condition label (edge)';

            const inpExpr = el('input', 'input');
            inpExpr.value = v || '';
            inpExpr.placeholder = "Expr legacy: ${payload.status} == 200";

            const bX = btn('X');
            bX.onclick = () => { wrap.removeChild(row); };

            row.appendChild(inpKey);
            row.appendChild(inpExpr);
            row.appendChild(bX);
            wrap.appendChild(row);
        }

        Object.keys(casos).forEach(k => addRow(k, String(casos[k] ?? '')));
        if (Object.keys(casos).length === 0) addRow('', '');

        const bAdd = btn('+ Agregar caso');
        bAdd.onclick = () => addRow('', '');

        const sCasos = section('Casos (Condition → expresión)', wrap);
        sCasos.appendChild(bAdd);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            const next = Object.assign({}, node.params || {});
            node.label = (inpLbl.value || '').trim();

            const field = (inpField.value || '').trim();
            if (field) next.field = field; else delete next.field;

            const def = (inpDef.value || '').trim();
            if (def) next.default = def; else delete next.default;

            // reconstruir casos desde filas
            const obj = {};
            Array.from(wrap.children).forEach(r => {
                const key = (r.children[0].value || '').trim();
                const expr = (r.children[1].value || '').trim();
                if (key && expr) obj[key] = expr;
            });
            if (Object.keys(obj).length > 0) next.casos = obj; else delete next.casos;

            node.params = next;
            ctx.ensurePosition(node);
            if (ctx.onChange) ctx.onChange();
            if (ctx.onInspectorRefresh) ctx.onInspectorRefresh();
        };

        bDel.onclick = () => ctx.deleteNode(node.id);

        body.appendChild(sLbl);
        body.appendChild(sField);
        body.appendChild(sDef);
        body.appendChild(sCasos);
        body.appendChild(rowButtons([bSave, bDel]));
    });
})();
