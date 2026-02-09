// Scripts/Inspectors/inspector.ai.call.js
(function () {
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

    window.WF_Inspector.register('ai.call', function (selected, state, ui, helpers) {
        var body = ui.body;
        body.innerHTML = '';

        var n = (state.nodes || []).find(function (x) { return x.id === selected.id; });
        if (!n) return;

        var p = n.params || {};

        // Label
        var sLbl = createEl('div', 'section'); sLbl.innerHTML = '<div class="label">Etiqueta (label)</div>';
        var inpLbl = createEl('input', 'input'); inpLbl.value = n.label || ''; sLbl.appendChild(inpLbl);

        // URL
        var sUrl = createEl('div', 'section'); sUrl.innerHTML = '<div class="label">url (endpoint IA)</div>';
        var inpUrl = createEl('input', 'input'); inpUrl.value = p.url || ''; sUrl.appendChild(inpUrl);

        // Method
        var sMethod = createEl('div', 'section'); sMethod.innerHTML = '<div class="label">method</div>';
        var selMethod = createEl('select', 'input');
        ['POST', 'GET', 'PUT'].forEach(function (m) {
            var o = document.createElement('option');
            o.value = m; o.textContent = m;
            selMethod.appendChild(o);
        });
        selMethod.value = (p.method || 'POST').toUpperCase();
        sMethod.appendChild(selMethod);

        // Headers
        var sHeaders = createEl('div', 'section'); sHeaders.innerHTML = '<div class="label">headers</div>';
        var kvHeaders = helpers.kvTable(p.headers || {});
        sHeaders.appendChild(kvHeaders);

        // System
        var sSystem = createEl('div', 'section'); sSystem.innerHTML = '<div class="label">system (opcional)</div>';
        var taSystem = createEl('textarea', 'textarea'); taSystem.value = p.system || ''; sSystem.appendChild(taSystem);

        // Prompt
        var sPrompt = createEl('div', 'section'); sPrompt.innerHTML = '<div class="label">prompt</div>';
        var taPrompt = createEl('textarea', 'textarea'); taPrompt.value = p.prompt || ''; sPrompt.appendChild(taPrompt);

        // responseFormat
        var sFmt = createEl('div', 'section'); sFmt.innerHTML = '<div class="label">responseFormat</div>';
        var selFmt = createEl('select', 'input');
        [
            { v: 'text', t: 'text' },
            { v: 'json', t: 'json' }
        ].forEach(function (x) {
            var o = document.createElement('option');
            o.value = x.v; o.textContent = x.t;
            selFmt.appendChild(o);
        });
        selFmt.value = (p.responseFormat || 'text').toLowerCase();
        sFmt.appendChild(selFmt);

        // timeoutMs
        var sTimeout = createEl('div', 'section'); sTimeout.innerHTML = '<div class="label">timeoutMs</div>';
        var inpTimeout = createEl('input', 'input');
        inpTimeout.type = 'number';
        inpTimeout.value = (typeof p.timeoutMs === 'number' ? p.timeoutMs : (p.timeoutMs || 30000));
        sTimeout.appendChild(inpTimeout);

        // output
        var sOut = createEl('div', 'section'); sOut.innerHTML = '<div class="label">output (prefijo en estado)</div>';
        var inpOut = createEl('input', 'input'); inpOut.value = p.output || 'ai'; sOut.appendChild(inpOut);

        // Botones
        var row = createEl('div', 'btn-row');
        var bSave = createEl('button', 'btn'); bSave.textContent = 'Guardar';
        var bDel = createEl('button', 'btn'); bDel.textContent = 'Eliminar nodo';
        row.appendChild(bSave);
        row.appendChild(bDel);

        body.appendChild(sLbl);
        body.appendChild(sUrl);
        body.appendChild(sMethod);
        body.appendChild(sHeaders);
        body.appendChild(sSystem);
        body.appendChild(sPrompt);
        body.appendChild(sFmt);
        body.appendChild(sTimeout);
        body.appendChild(sOut);
        body.appendChild(row);

        bSave.onclick = function () {
            n.label = inpLbl.value || n.label || '';

            n.params = {
                url: inpUrl.value || '',
                method: selMethod.value || 'POST',
                headers: kvHeaders.getValue(),
                system: taSystem.value || '',
                prompt: taPrompt.value || '',
                responseFormat: selFmt.value || 'text',
                timeoutMs: parseInt(inpTimeout.value || '30000', 10),
                output: inpOut.value || 'ai'
            };

            ensurePosition(n);

            // refrescar título en canvas
            var el = document.getElementById(n.id);
            if (el) {
                var t = el.querySelector('.node__title');
                if (t) t.textContent = n.label;
            }

            window.WF_Inspector.render({ type: 'node', id: n.id }, state, ui);
        };

        bDel.onclick = function () {
            // Eliminar edges que salen o llegan a este nodo
            if (Array.isArray(ctx.edges)) {
                for (var i = ctx.edges.length - 1; i >= 0; i--) {
                    var e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === n.id || e.to === n.id) ctx.edges.splice(i, 1);
                }
            }

            // Eliminar nodo
            if (Array.isArray(ctx.nodes)) {
                for (var j = ctx.nodes.length - 1; j >= 0; j--) {
                    var nn = ctx.nodes[j];
                    if (nn && nn.id === n.id) ctx.nodes.splice(j, 1);
                }
            }

            var elNode = ctx.nodeEl(n.id);
            if (elNode) elNode.remove();

            try { ctx.drawEdges(); } catch (e2) { }
            body.innerHTML = '';
        };
    });
})();
