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

        // Plantillas
        var sTpl = createEl('div', 'section'); sTpl.innerHTML = '<div class="label">Plantilla</div>';
        var selTpl = createEl('select', 'input');
        (function () {
            var pack = PT['data.sql.templates'] || PT['data.sql'] || {};
            var opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(function (k) {
                var o = document.createElement('option'); o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o);
            });
        })();
        sTpl.appendChild(selTpl);

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


        // set initial mode
        var useCSN0 = !!p.connectionStringName || !p.connection;
        setTimeout(function () {
            document.getElementById(idA).checked = useCSN0;
            document.getElementById(idB).checked = !useCSN0;
            refreshConnMode();
            document.getElementById(idA).addEventListener('change', refreshConnMode);
            document.getElementById(idB).addEventListener('change', refreshConnMode);
        }, 0);

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
            var pack = PT['data.sql.templates'] || {};
            var def = PT['data.sql'] || {};
            var tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;
            if (tpl) {
                if ('query' in tpl) taQuery.value = tpl.query || '';
                // parámetros (reemplazo tabla)
                sPars.innerHTML = '<div class="label">Parameters</div>';
                var kv2 = helpers.kvTable(tpl.parameters || {}); sPars.appendChild(kv2);
                kv = kv2;
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
        };

        bDel.onclick = function (ev) {
            ev.preventDefault(); ev.stopPropagation();
            state.edges = (state.edges || []).filter(function (e) { return e.from !== n.id && e.to !== n.id; });
            state.nodes = (state.nodes || []).filter(function (x) { return x.id !== n.id; });
            var el = document.getElementById(n.id); if (el) el.remove();
            state.drawEdges(); state.select(null);
        };
    });
})();
