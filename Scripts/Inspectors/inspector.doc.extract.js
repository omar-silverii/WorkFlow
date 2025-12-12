(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('doc.extract', (node, ctx, dom) => {

        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';

        // Título y subtítulo
        if (title) title.textContent = node.label || 'Extraer datos';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // =====================================
        // 1) Etiqueta del nodo
        // =====================================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || 'Extraer datos';
        const sLbl = section('Etiqueta', inpLbl);

        // =====================================
        // 2) Origen: clave en contexto donde está el texto
        //      *** UNIFICADO A input.text ***
        // =====================================
        const inpOrigen = el('input', 'input');
        inpOrigen.value = p.origen || 'input.text';
        const sOrigen = section('Origen (ctx[key] con el texto)', inpOrigen);

        // =====================================
        // 3) Reglas JSON
        // =====================================
        const txtRules = document.createElement('textarea');
        txtRules.className = 'input';
        txtRules.rows = 12;
        txtRules.style.fontFamily = 'monospace';
        txtRules.spellcheck = false;

        txtRules.value = p.rulesJson || `[
  { "campo": "Poliza",  "linea": 3, "colDesde": 9,  "largo": 11 },
  { "campo": "Nombre",  "regex": "NOMBRE\\\\s*:\\\\s*(.+)", "grupo": 1 }
]`;

        const sRules = section('Reglas (JSON)', txtRules);

        // =====================================
        // Botones
        // =====================================
        const bEjemplo = btn('Insertar ejemplo');
        const bTest = btn('Probar extracción');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // =====================================
        // Acciones
        // =====================================

        bEjemplo.onclick = () => {
            txtRules.value = `[
  { "campo": "Indice",   "linea": 1, "colDesde": 1,  "largo": 8 },
  { "campo": "Cabecera", "linea": 2, "colDesde": 20, "largo": 30 },
  { "campo": "Fecha",    "linea": 3, "colDesde": 89, "largo": 8 },
  { "campo": "Nombre",   "regex": "NOMBRE\\\\s*:\\\\s*(.+)", "grupo": 1 }
]`;
        };

        // === PROBAR EXTRACCIÓN ===
        bTest.onclick = () => {
            if (!window.WF_Inspector.previewBox) {
                window.WF_Inspector.previewBox = document.createElement('pre');
                window.WF_Inspector.previewBox.style.background = '#111';
                window.WF_Inspector.previewBox.style.color = '#0f0';
                window.WF_Inspector.previewBox.style.padding = '8px';
                window.WF_Inspector.previewBox.style.whiteSpace = 'pre-wrap';
                window.WF_Inspector.previewBox.style.fontSize = '12px';
                body.appendChild(window.WF_Inspector.previewBox);
            }

            let rules;
            try {
                rules = JSON.parse(txtRules.value);
            } catch (err) {
                window.WF_Inspector.previewBox.textContent =
                    '❌ ERROR en el JSON de reglas:\n' + err.message;
                return;
            }

            if (typeof ctx.previewExtract !== 'function') {
                window.WF_Inspector.previewBox.textContent =
                    '⚠ No existe ctx.previewExtract(). Agregar hook en el motor.';
                return;
            }

            const origen = inpOrigen.value.trim() || 'input.text';

            const result = ctx.previewExtract(origen, rules);

            window.WF_Inspector.previewBox.textContent =
                'Resultado de prueba:\n' + JSON.stringify(result, null, 2);
        };

        // === GUARDAR ===
        bSave.onclick = () => {
            node.label = inpLbl.value || 'Extraer datos';

            node.params = {
                origen: inpOrigen.value || 'input.text',
                rulesJson: txtRules.value || ''
            };

            ensurePosition(node);

            // Actualizar título en el canvas
            const nd = nodeEl(node.id);
            if (nd) {
                const t = nd.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);

            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
        };

        // === ELIMINAR NODO ===
        bDel.onclick = () => {

            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (e.from === node.id || e.to === node.id) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    if (ctx.nodes[i].id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            const nd = nodeEl(node.id);
            if (nd) nd.remove();

            ctx.drawEdges();
            ctx.select(null);
        };

        // =====================================
        // Armar DOM final
        // =====================================
        body.appendChild(sLbl);
        body.appendChild(sOrigen);
        body.appendChild(sRules);
        body.appendChild(rowButtons(bSave, bDel, bEjemplo, bTest));

    });
})();
