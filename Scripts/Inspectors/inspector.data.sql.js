// Scripts/inspectors/inspector.data.sql.js
(function () {
    var PT = (window.PARAM_TEMPLATES = window.PARAM_TEMPLATES || {});
    function createEl(tag, cls) { var el = document.createElement(tag); if (cls) el.className = cls; return el; }

    function ensurePosition(n) {
        n.params = n.params || {};
        if (!n.params.position || typeof n.params.position !== 'object') {
            n.params.position = { x: n.x | 0, y: n.y | 0 };
        } else {
            if (typeof n.params.position.x === 'number') n.x = n.params.position.x;
            if (typeof n.params.position.y === 'number') n.y = n.params.position.y;
        }
    }

    window.WF_Inspector.register('data.sql', function (selected, state, ui, helpers) {
        var body = ui.body;
        body.innerHTML = '';
        var n = (state.nodes || []).find(function (x) { return x.id === selected.id; });
        if (!n) return;
        var p = n.params || {};

        // Label
        var sLbl = createEl('div', 'section'); sLbl.innerHTML = '<div class="label">Etiqueta (label)</div>';
        var inpLbl = createEl('input', 'input'); inpLbl.value = n.label || ''; sLbl.appendChild(inpLbl);

        // --- Plantillas (dinámico) ---
        var sTpl = createEl('div', 'section');
        sTpl.innerHTML = '<div class="label">Plantilla</div>';
        var selTpl = createEl('select', 'input');
        sTpl.appendChild(selTpl);

        function fillTemplatesSelect() {
            var pack = PT['data.sql.templates'];
            selTpl.innerHTML = '';
            var opt0 = document.createElement('option');
            opt0.value = ''; opt0.textContent = '— Elegir —';
            selTpl.appendChild(opt0);

            if (pack && Object.keys(pack).length) {
                Object.keys(pack).forEach(function (k) {
                    var o = document.createElement('option');
                    o.value = k;
                    o.textContent = (pack[k].label || k);
                    selTpl.appendChild(o);
                });
                selTpl._pack = pack;          // guardo el pack activo
            } else {
                // Fallback mínimo si aún no cargó workflow.templates.js
                var fallback = {
                    select_top10: {
                        label: 'SELECT TOP 10',
                        query: 'SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;',
                        parameters: {}
                    },
                    merge_upsert_demo: {
                        label: 'MERGE Upsert por Numero',
                        query:
                            'MERGE PolizasDemo AS T USING (SELECT @NroPoliza AS Numero, @Asegurado AS Asegurado) AS S ' +
                            'ON (T.Numero = S.Numero) WHEN MATCHED THEN UPDATE SET Asegurado = S.Asegurado ' +
                            'WHEN NOT MATCHED THEN INSERT (Numero, Asegurado) VALUES (S.Numero, S.Asegurado);',
                        parameters: { NroPoliza: '${payload.data.nro}', Asegurado: 'Póliza ${payload.data.nro}' }
                    }
                };
                Object.keys(fallback).forEach(function (k) {
                    var o = document.createElement('option');
                    o.value = k; o.textContent = fallback[k].label || k;
                    selTpl.appendChild(o);
                });
                selTpl._pack = fallback;
            }
        }

        // Recargas/esperas para evitar carrera de carga
        fillTemplatesSelect();                      // primer intento
        selTpl.addEventListener('mousedown', fillTemplatesSelect); // recarga al abrir
        window.addEventListener('wf-templates-ready', fillTemplatesSelect);

        // Retry corto hasta que aparezcan las plantillas reales
        (function retryUntilReady(tries) {
            tries = (tries || 0);
            var pack = PT['data.sql.templates'] || {};
            if (Object.keys(pack).length || tries >= 25) {
                fillTemplatesSelect();
            } else {
                setTimeout(function () { retryUntilReady(tries + 1); }, 80); // ~2s total
            }
        })();



        // Conexión
        var sConnMode = createEl('div', 'section'); sConnMode.innerHTML = '<div class="label">Conexión</div>';
        var wrapConnMode = createEl('div');

        // radios SIN ids, con referencias directas
        var rbCSN = document.createElement('input'); rbCSN.type = 'radio'; rbCSN.name = 'connMode_' + n.id;
        var rbCONN = document.createElement('input'); rbCONN.type = 'radio'; rbCONN.name = 'connMode_' + n.id;

        var lblA = document.createElement('label'); lblA.style.marginRight = '10px';
        lblA.appendChild(rbCSN); lblA.appendChild(document.createTextNode(' connectionStringName'));

        var lblB = document.createElement('label');
        lblB.appendChild(rbCONN); lblB.appendChild(document.createTextNode(' connection'));

        wrapConnMode.appendChild(lblA);
        wrapConnMode.appendChild(lblB);
        sConnMode.appendChild(wrapConnMode);

        // campos de cada modo
        var sCSN = createEl('div', 'section'); sCSN.innerHTML = '<div class="label">connectionStringName</div>';
        var inpCSN = createEl('input', 'input'); inpCSN.value = p.connectionStringName || 'DefaultConnection'; sCSN.appendChild(inpCSN);

        var sCONN = createEl('div', 'section'); sCONN.innerHTML = '<div class="label">connection (connection string)</div>';
        var inpCONN = createEl('input', 'input'); inpCONN.value = p.connection || ''; sCONN.appendChild(inpCONN);

        // toggle visual
        function refreshConnMode() {
            var useCSN = rbCSN.checked;
            sCSN.style.display = useCSN ? '' : 'none';
            sCONN.style.display = useCSN ? 'none' : '';
        }

        // estado inicial según params
        var useCSN0 = !!p.connectionStringName || !p.connection;
        rbCSN.checked = useCSN0;
        rbCONN.checked = !useCSN0;
        rbCSN.addEventListener('change', refreshConnMode);
        rbCONN.addEventListener('change', refreshConnMode);
        refreshConnMode();  // aplicar

        // Query
        var sQuery = createEl('div', 'section'); sQuery.innerHTML = '<div class="label">Query</div>';
        var taQuery = createEl('textarea', 'textarea'); taQuery.value = p.query || ''; sQuery.appendChild(taQuery);

        // Parameters (KV)
        var sPars = createEl('div', 'section'); sPars.innerHTML = '<div class="label">Parameters</div>';
        var kv = helpers.kvTable(p.parameters || {}); sPars.appendChild(kv);

        // Botones
        var row = createEl('div', 'btn-row');
        var bTpl = createEl('button', 'btn'); bTpl.textContent = 'Insertar plantilla';
        var bSave = createEl('button', 'btn'); bSave.textContent = 'Guardar';
        var bDel = createEl('button', 'btn'); bDel.textContent = 'Eliminar nodo';
        row.appendChild(bTpl); row.appendChild(bSave); row.appendChild(bDel);

        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sConnMode);
        body.appendChild(sCSN);
        body.appendChild(sCONN);
        body.appendChild(sQuery);
        body.appendChild(sPars);
        body.appendChild(row);

        bTpl.onclick = function (ev) {
            ev.preventDefault(); ev.stopPropagation();
            var base = PT['data.sql'] || {};
            var pack = selTpl._pack || PT['data.sql.templates'] || {};
            var tpl = (selTpl.value && pack[selTpl.value]) ? pack[selTpl.value] : base;

            if (tpl) {
                if ('query' in tpl) taQuery.value = tpl.query || '';
                // reemplazo la tabla de parámetros
                sPars.innerHTML = '<div class="label">Parameters</div>';
                var kv2 = helpers.kvTable(tpl.parameters || {});
                sPars.appendChild(kv2);
                kv = kv2; // importante: actualizar la referencia para Guardar
            }
        };


        bSave.onclick = function () {
            var next = { query: taQuery.value || '', parameters: kv.getValue() };
            var useCSN = rbCSN.checked;
            if (useCSN) {
                next.connectionStringName = inpCSN.value || 'DefaultConnection';
            } else {
                next.connection = inpCONN.value || '';
            }
            n.label = inpLbl.value || n.label || '';
            n.params = next;
            ensurePosition(n);
            var el = document.getElementById(n.id); if (el) {
                var t = el.querySelector('.node__title'); if (t) t.textContent = n.label;
            }
            window.WF_Inspector.render({ type: 'node', id: n.id }, state, ui);
            // === FIX: redraw edges after save ===
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
        };

        bDel.onclick = () => {
            // Eliminar edges que salen o llegan a este nodo (mutando el array real)
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            // Eliminar el nodo del array real
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            // Quitar del DOM y refrescar canvas
            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();

            ctx.drawEdges();
            ctx.select(null);
        };

    });
})();
