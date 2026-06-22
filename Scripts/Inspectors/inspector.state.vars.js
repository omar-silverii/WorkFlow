; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function safeJsonParse(s) {
        try { return JSON.parse(s); } catch (e) { return null; }
    }

    function isPlainObject(v) {
        return v && typeof v === 'object' && !Array.isArray(v);
    }

    function isValidPath(path) {
        const v = String(path || '').trim();
        if (!v) return false;
        if (v.indexOf('${') >= 0 || /\s/.test(v)) return false;
        return /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/.test(v);
    }

    function parseValueForSimpleMode(raw) {
        const text = String(raw == null ? '' : raw);
        const trimmed = text.trim();
        if (!trimmed) return '';

        // En modo simple, texto común queda como texto.
        // Si el usuario escribe explícitamente un objeto/array JSON, se respeta como valor estructurado.
        if ((trimmed.charAt(0) === '{' && trimmed.charAt(trimmed.length - 1) === '}') ||
            (trimmed.charAt(0) === '[' && trimmed.charAt(trimmed.length - 1) === ']')) {
            const parsed = safeJsonParse(trimmed);
            if (parsed === null) throw new Error('El valor parece JSON, pero no es válido. Revisá llaves, comillas y comas.');
            return parsed;
        }

        return text;
    }

    function stringifyForTextarea(v) {
        if (v == null) return '';
        if (typeof v === 'string') return v;
        try { return JSON.stringify(v, null, 2); } catch (e) { return String(v); }
    }

    function getSetObject(p) {
        const setVal = p ? p.set : null;
        if (typeof setVal === 'string') {
            const parsed = safeJsonParse(setVal);
            return isPlainObject(parsed) ? parsed : {};
        }
        return isPlainObject(setVal) ? setVal : {};
    }

    register('state.vars', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'State Vars';
        if (sub) sub.textContent = 'Guardar / quitar variables en DatosContexto';

        const p = node.params || {};
        const setObj = getSetObject(p);
        const setKeys = Object.keys(setObj || {});
        const simpleDefault = setKeys.length <= 1;
        const firstKey = setKeys.length === 1 ? setKeys[0] : '';
        const firstValue = firstKey ? setObj[firstKey] : '';

        // Label
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Modo de guardado
        const selMode = el('select', 'input');
        [
            { v: 'simple', t: 'Simple: una variable' },
            { v: 'json', t: 'Avanzado: JSON / varios datos' }
        ].forEach(x => {
            const o = document.createElement('option');
            o.value = x.v;
            o.textContent = x.t;
            if ((simpleDefault ? 'simple' : 'json') === x.v) o.selected = true;
            selMode.appendChild(o);
        });
        const sMode = section('Modo de guardado', selMode);

        // Simple: destino + valor
        const simpleWrap = el('div');

        const inpKey = el('input', 'input');
        inpKey.value = firstKey;
        inpKey.placeholder = 'Ej.: biz.prueba.fix30';

        const taValue = el('textarea', 'input');
        taValue.rows = 5;
        taValue.placeholder = 'Ej.: OK_FIX30 o ${sql.rowCount}';
        taValue.value = stringifyForTextarea(firstValue);

        const btnPickValue = btn('Elegir dato…');
        btnPickValue.style.marginTop = '6px';
        btnPickValue.onclick = () => {
            if (!window.WF_FieldPicker) { alert('WF_FieldPicker no está cargado'); return; }
            window.WF_FieldPicker.open({
                ctx,
                title: 'Copiar dato disponible',
                onPick: (v) => {
                    taValue.value = '${' + v + '}';
                    taValue.focus();
                }
            });
        };

        const simpleHint = el('div');
        simpleHint.className = 'hint';
        simpleHint.textContent = 'El nombre real de la variable es el destino. Ejemplo: biz.prueba.fix30. No escribas “=” acá.';

        simpleWrap.appendChild(section('Variable destino', inpKey));
        simpleWrap.appendChild(section('Valor', taValue));
        simpleWrap.appendChild(btnPickValue);
        simpleWrap.appendChild(simpleHint);

        const sSimple = section('Guardar una variable', simpleWrap);

        // Avanzado: JSON completo
        const taSet = el('textarea', 'input');
        taSet.rows = 9;
        taSet.placeholder = '{\n  "biz.prueba.fix30": "OK_FIX30",\n  "biz.compra": { "estado": "Pendiente", "importe": 150000 }\n}';
        taSet.value = setKeys.length ? JSON.stringify(setObj, null, 2) : '';

        const advancedHint = el('div');
        advancedHint.className = 'hint';
        advancedHint.textContent = 'Usá este modo para guardar varios datos, objetos o arrays. Debe ser un JSON objeto válido.';

        const advancedWrap = el('div');
        advancedWrap.appendChild(taSet);
        advancedWrap.appendChild(advancedHint);
        const sSet = section('JSON a guardar', advancedWrap);

        // REMOVE CSV
        const inpRemove = el('input', 'input');
        if (Array.isArray(p.remove)) inpRemove.value = p.remove.join(',');
        else inpRemove.value = (p.remove || '');
        inpRemove.placeholder = 'Ej.: biz.prueba.fix30, wf.vars.temporal';
        const sRemove = section('Variables a quitar (separadas por coma)', inpRemove);

        function syncMode() {
            const mode = selMode.value || 'simple';
            sSimple.style.display = mode === 'simple' ? '' : 'none';
            sSet.style.display = mode === 'json' ? '' : 'none';
        }
        selMode.addEventListener('change', syncMode);
        syncMode();

        // Buttons
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'State Vars';

            let setToSave = null;
            const mode = selMode.value || 'simple';

            if (mode === 'simple') {
                const key = (inpKey.value || '').trim();
                const rawValue = taValue.value || '';
                if (key || rawValue.trim()) {
                    if (!isValidPath(key)) {
                        alert('La variable destino debe tener formato de ruta, por ejemplo biz.prueba.fix30. No uses espacios ni ${...}.');
                        return;
                    }
                    try {
                        setToSave = {};
                        setToSave[key] = parseValueForSimpleMode(rawValue);
                    } catch (ex) {
                        alert(ex.message || ex);
                        return;
                    }
                }
            } else {
                const rawSet = (taSet.value || '').trim();
                if (rawSet) {
                    setToSave = safeJsonParse(rawSet);
                    if (!isPlainObject(setToSave)) {
                        alert('El JSON a guardar debe ser un objeto válido, por ejemplo { "biz.prueba.fix30": "OK_FIX30" }.');
                        return;
                    }
                    const bad = Object.keys(setToSave).filter(k => !isValidPath(k));
                    if (bad.length) {
                        alert('Estas claves no tienen formato válido de ruta: ' + bad.join(', '));
                        return;
                    }
                }
            }

            // remove
            const rawRem = (inpRemove.value || '').trim();
            const remArr = rawRem
                ? rawRem.split(',').map(x => (x || '').trim()).filter(x => !!x)
                : [];
            const badRemove = remArr.filter(k => !isValidPath(k));
            if (badRemove.length) {
                alert('Estas variables a quitar no tienen formato válido: ' + badRemove.join(', '));
                return;
            }

            const next = {};
            if (setToSave && Object.keys(setToSave).length) next.set = setToSave;
            if (remArr.length) next.remove = remArr;

            node.params = next;
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

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

        body.appendChild(sLbl);
        body.appendChild(sMode);
        body.appendChild(sSimple);
        body.appendChild(sSet);
        body.appendChild(sRemove);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
