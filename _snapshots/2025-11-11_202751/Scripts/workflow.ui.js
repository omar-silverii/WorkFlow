// Scripts/workflow.ui.js
// Mantiene TU UI y toolbox. Agrega:
// - Persistencia de posición en Parameters.position {x,y}
// - Validaciones cliente (start/end/edges & params básicos)
// - WF_getJson() para postbacks
(function () {
    // ====== acceso a catálogo / plantillas / íconos
    var DATA = window.WorkflowData || {};
    var CATALOG = DATA.CATALOG || [];
    var GROUPS = DATA.GROUPS || [];
    var PARAM_TEMPLATES = DATA.PARAM_TEMPLATES || {};
    var ICONS = DATA.ICONS || {};

    // ====== estado
    // nodos: {id,key,label,x,y,tint,icon,params}
    var nodes = [];
    // aristas: {id,from,to,condition}
    var edges = [];
    var selected = null; // {type:'node'|'edge', id:string}
    var connectMode = false;
    var connectFrom = null;
    var idSeq = 1;

    // Fallback: si no hay grupos, mostrar todos los items
    if (!GROUPS.length && CATALOG.length) {
        GROUPS = [{ name: 'Todos', items: CATALOG.map(function (it) { return it.key; }) }];
    }

    // ====== helpers DOM y utilidades
    function $(id) { return document.getElementById(id); }
    function createEl(tag, cls) { var el = document.createElement(tag); if (cls) el.className = cls; return el; }
    function findCat(key) { return CATALOG.find(function (i) { return i.key === key }); }
    function uid(p) { return (p || 'n') + (idSeq++); }
    function hexToRgba(hex, a) {
        var v = (hex || '').replace('#', ''); if (v.length === 3) { v = v.split('').map(function (x) { return x + x }).join(''); }
        var r = parseInt(v.substr(0, 2) || '0', 16), g = parseInt(v.substr(2, 2) || '0', 16), b = parseInt(v.substr(4, 2) || '0', 16);
        return 'rgba(' + r + ',' + g + ',' + b + ',' + (a == null ? 1 : a) + ')';
    }

    // ====== refs a elementos
    var canvas, svg;

    function canvasRect() { return canvas.getBoundingClientRect(); }
    function nodeEl(id) { return document.getElementById(id); }
    function nodeCenter(id) {
        var el = nodeEl(id); if (!el) return null;
        var r = el.getBoundingClientRect(); var c = canvasRect();
        return { x: r.left - c.left + r.width / 2, y: r.top - c.top + r.height / 2, w: r.width, h: r.height };
    }

    // ====== toolbox
    function renderToolbox(filter) {
        var list = $('toolboxList');
        if (!list) return;
        list.innerHTML = '';

        var groups = GROUPS;
        if ((!groups || !groups.length) && CATALOG.length) {
            groups = [{ name: 'Todos', items: CATALOG.map(function (it) { return it.key; }) }];
        }

        groups.forEach(function (g) {
            var items = g.items
                .map(findCat)
                .filter(Boolean)
                .filter(function (it) {
                    if (!filter) return true;
                    var f = (filter || '').toLowerCase();
                    return it.label.toLowerCase().indexOf(f) >= 0 || it.key.toLowerCase().indexOf(f) >= 0;
                });

            if (!items.length) return;

            var group = createEl('div', 'group');
            var title = createEl('div', 'group__title');
            title.textContent = g.name;
            group.appendChild(title);

            items.forEach(function (it) {
                var row = createEl('div', 'item');
                row.setAttribute('draggable', 'true');
                row.dataset.key = it.key;

                var icon = createEl('div', 'item__icon');
                icon.style.background = hexToRgba(it.tint, 0.15);
                icon.style.color = it.tint;
                icon.innerHTML = ICONS[it.icon] || '';

                var label = createEl('div', 'item__label');
                label.textContent = it.label;

                row.appendChild(icon);
                row.appendChild(label);

                row.addEventListener('dragstart', function (ev) {
                    ev.dataTransfer.setData('text/plain', it.key);
                    try { ev.dataTransfer.effectAllowed = 'copyMove'; } catch (e) { }
                });

                group.appendChild(row);
            });

            list.appendChild(group);
        });
    }

    // ====== nodos
    function createNode(meta, x, y, label) {
        var id = uid('n');
        var n = {
            id: id,
            key: meta.key,
            label: label || meta.label,
            x: x,
            y: y,
            tint: meta.tint,
            icon: meta.icon,
            params: (PARAM_TEMPLATES[meta.key] || {})
        };
        // Aseguramos que si luego exportamos, tenga position
        ensurePosition(n);
        nodes.push(n);
        drawNode(n);
        select({ type: 'node', id: id });
        drawEdges();
    }

    function ensurePosition(n) {
        n.params = n.params || {};
        if (!n.params.position || typeof n.params.position !== 'object') {
            n.params.position = { x: n.x | 0, y: n.y | 0 };
        } else {
            // si ya existe, sincronizamos a x/y
            if (typeof n.params.position.x === 'number') n.x = n.params.position.x;
            if (typeof n.params.position.y === 'number') n.y = n.params.position.y;
        }
    }

    var dragState = null;
    function drawNode(n) {
        var el = createEl('div', 'node');
        el.id = n.id;
        el.style.left = (n.x | 0) + 'px';
        el.style.top = (n.y | 0) + 'px';
        var head = createEl('div', 'node__header');
        var ic = createEl('div', 'node__icon'); ic.style.background = hexToRgba(n.tint, .15); ic.style.color = n.tint; ic.innerHTML = ICONS[n.icon] || '';
        var title = createEl('div', 'node__title'); title.textContent = n.label;
        var pill = createEl('div', 'pill'); pill.textContent = n.key;
        head.appendChild(ic); head.appendChild(title); head.appendChild(pill);
        var body = createEl('div', 'node__body'); body.textContent = 'Arrastrá para mover. (Conectar: botón "Conectar")';
        el.appendChild(head); el.appendChild(body);
        canvas.appendChild(el);

        el.addEventListener('mousedown', function (ev) {
            if (connectMode) { handleConnectClick(n.id); ev.stopPropagation(); return; }
            select({ type: 'node', id: n.id });
            startMove(ev, n, el);
        });
        el.addEventListener('click', function (ev) { select({ type: 'node', id: n.id }); ev.stopPropagation(); });
    }

    function startMove(ev, n, el) {
        var sx = ev.clientX, sy = ev.clientY, bx = n.x, by = n.y;
        dragState = { n: n, el: el, sx: sx, sy: sy, bx: bx, by: by };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', endMove);
    }
    function onMove(ev) {
        if (!dragState) return;
        var dx = ev.clientX - dragState.sx, dy = ev.clientY - dragState.sy;
        var nx = dragState.bx + dx, ny = dragState.by + dy;
        dragState.n.x = nx; dragState.n.y = ny;
        dragState.n.params = dragState.n.params || {};
        dragState.n.params.position = { x: nx | 0, y: ny | 0 }; // ← persistir posición mientras se mueve
        dragState.el.style.left = (nx | 0) + 'px';
        dragState.el.style.top = (ny | 0) + 'px';
        drawEdges();
    }
    function endMove() {
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', endMove);
        dragState = null;
    }

    function clearAll() {
        nodes = []; edges = [];
        canvas.querySelectorAll('.node').forEach(function (e) { e.remove(); });
        drawEdges(); select(null);
    }

    // ====== aristas
    function drawEdges() {
        var r = canvasRect(); svg.setAttribute('viewBox', '0 0 ' + r.width + ' ' + r.height);
        while (svg.lastChild && svg.lastChild.tagName !== 'defs') { svg.removeChild(svg.lastChild); }
        edges.forEach(function (e) {
            var from = nodeCenter(e.from), to = nodeCenter(e.to); if (!from || !to) return;
            var sx = from.x + from.w / 2 - 20, sy = from.y;
            var tx = to.x - to.w / 2 + 20, ty = to.y;
            var dx = Math.max(40, Math.abs(tx - sx) / 2);
            var d = 'M ' + sx + ' ' + sy + ' C ' + (sx + dx) + ' ' + sy + ', ' + (tx - dx) + ' ' + ty + ', ' + tx + ' ' + ty;
            var p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            p.setAttribute('d', d);
            p.setAttribute('class', 'edge' + (selected && selected.type === 'edge' && selected.id === e.id ? ' selected' : ''));
            p.setAttribute('data-id', e.id);
            p.setAttribute('marker-end', 'url(#' + (selected && selected.type === 'edge' && selected.id === e.id ? 'arrowSel' : 'arrow') + ')');
            p.addEventListener('click', function (ev) { select({ type: 'edge', id: e.id }); ev.stopPropagation(); });
            svg.appendChild(p);

            var label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            label.setAttribute('font-size', '10'); label.setAttribute('fill', '#64748b');
            var mx = (sx + tx) / 2, my = (sy + ty) / 2 - 4;
            label.setAttribute('x', mx); label.setAttribute('y', my);
            label.textContent = e.condition || 'always';
            label.style.pointerEvents = 'none';
            svg.appendChild(label);
        });
    }

    function handleConnectClick(nodeId) {
        if (!connectFrom) { connectFrom = nodeId; highlightNode(nodeId, true); return; }
        if (connectFrom === nodeId) { highlightNode(nodeId, false); connectFrom = null; return; }
        var id = uid('e');
        edges.push({ id: id, from: connectFrom, to: nodeId, condition: 'always' });
        highlightNode(connectFrom, false);
        connectFrom = null; drawEdges(); select({ type: 'edge', id: id });
    }
    function highlightNode(id, on) {
        var el = nodeEl(id); if (!el) return;
        if (on) el.classList.add('selected'); else el.classList.remove('selected');
    }

    // ====== selección + inspector
    function select(sel) {
        selected = sel;
        canvas.querySelectorAll('.node').forEach(function (el) { el.classList.remove('selected'); });
        if (sel && sel.type === 'node') { var el = nodeEl(sel.id); if (el) el.classList.add('selected'); }
        drawEdges();
        renderInspector();
    }

    window.PARAM_TEMPLATES = window.PARAM_TEMPLATES || {
        "http.request": { url: "", method: "GET", headers: {}, query: {}, body: null, contentType: "", timeoutMs: 8000 }
    };

    function renderInspector() {
        var body = $('inspectorBody'), title = $('inspectorTitle'), sub = $('inspectorSub');
        if (!body) return;
        body.innerHTML = '';
        if (!selected) { if (title) title.textContent = 'Seleccioná un nodo o una arista'; if (sub) sub.textContent = ''; return; }

        function kvTable(obj) {
            obj = obj || {};
            var wrap = createEl('div');
            var tbl = createEl('table', 'kv'); wrap.appendChild(tbl);
            Object.keys(obj).forEach(function (k) {
                var tr = createEl('tr');
                var kIn = createEl('input', 'input'); kIn.value = k;
                var vIn = createEl('input', 'input'); vIn.value = obj[k];
                tr.appendChild(kIn); tr.appendChild(vIn); tbl.appendChild(tr);
            });
            var tr = createEl('tr');
            var kNew = createEl('input', 'input'), vNew = createEl('input', 'input');
            tr.appendChild(kNew); tr.appendChild(vNew); tbl.appendChild(tr);

            wrap.getValue = function () {
                var res = {};
                var rows = tbl.querySelectorAll('tr');
                [].forEach.call(rows, function (r) {
                    var inputs = r.querySelectorAll('input');
                    if (!inputs.length) return;
                    var k = (inputs[0].value || '').trim();
                    var v = (inputs[1].value || '').trim();
                    if (k) res[k] = v;
                });
                return res;
            };
            return wrap;
        }

        if (selected.type === 'node') {
            var n = nodes.find(function (x) { return x.id === selected.id });
            if (!n) { if (title) title.textContent = 'Nodo no encontrado'; return; }

            if (title) title.textContent = n.label || 'Nodo';
            if (sub) sub.textContent = n.key || '';

            if (n.key === 'http.request') {
                var p = n.params || {};

                var sLbl = createEl('div', 'section');
                sLbl.innerHTML = '<div class="label">Etiqueta (label)</div>';
                var inpLbl = createEl('input', 'input'); inpLbl.value = n.label || ''; sLbl.appendChild(inpLbl);

                var sUrl = createEl('div', 'section'); sUrl.innerHTML = '<div class="label">URL</div>';
                var inpUrl = createEl('input', 'input'); inpUrl.value = p.url || ''; sUrl.appendChild(inpUrl);

                var sMethod = createEl('div', 'section'); sMethod.innerHTML = '<div class="label">Método</div>';
                var sel = createEl('select', 'input');
                ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].forEach(function (m) {
                    var opt = document.createElement('option'); opt.value = m; opt.textContent = m;
                    if ((p.method || 'GET').toUpperCase() === m) opt.selected = true;
                    sel.appendChild(opt);
                });
                sMethod.appendChild(sel);

                var sHeaders = createEl('div', 'section'); sHeaders.innerHTML = '<div class="label">Headers</div>';
                var kvH = kvTable(p.headers); sHeaders.appendChild(kvH);

                var sQuery = createEl('div', 'section'); sQuery.innerHTML = '<div class="label">Query</div>';
                var kvQ = kvTable(p.query); sQuery.appendChild(kvQ);

                var sBody = createEl('div', 'section'); sBody.innerHTML = '<div class="label">Cuerpo</div>';
                var taBody = createEl('textarea', 'textarea');
                taBody.value = (p.body == null ? '' : (typeof p.body === 'string' ? p.body : JSON.stringify(p.body, null, 2)));
                sBody.appendChild(taBody);

                var sCT = createEl('div', 'section'); sCT.innerHTML = '<div class="label">Content-Type (opcional)</div>';
                var inpCT = createEl('input', 'input'); inpCT.value = p.contentType || ''; sCT.appendChild(inpCT);

                var hint = createEl('div', 'hint');
                hint.textContent = 'Si el método es GET/HEAD, el motor ignorará el body al enviar.';

                var row = createEl('div', 'btn-row');
                var bTpl = createEl('button', 'btn'); bTpl.type = 'button'; bTpl.textContent = 'Insertar plantilla';
                var bSave = createEl('button', 'btn'); bSave.type = 'button'; bSave.textContent = 'Guardar';
                var bDel = createEl('button', 'btn'); bDel.type = 'button'; bDel.textContent = 'Eliminar nodo';
                row.appendChild(bTpl); row.appendChild(bSave); row.appendChild(bDel);

                body.appendChild(sLbl);
                body.appendChild(sUrl);
                body.appendChild(sMethod);
                body.appendChild(sHeaders);
                body.appendChild(sQuery);
                body.appendChild(sBody);
                body.appendChild(sCT);
                body.appendChild(hint);
                body.appendChild(row);

                bTpl.onclick = function () {
                    inpUrl.value = inpUrl.value || '/Api/Ping.ashx';
                    sel.value = (p.method || 'GET').toUpperCase();
                    if (!inpCT.value) inpCT.value = 'application/json';
                    if (!taBody.value) taBody.value = '';
                };

                bSave.onclick = function () {
                    var next = {
                        url: inpUrl.value.trim(),
                        method: sel.value,
                        headers: kvH.getValue(),
                        query: kvQ.getValue(),
                        timeoutMs: (p.timeoutMs || 10000)
                    };
                    var raw = taBody.value.trim();
                    if (raw) { try { next.body = JSON.parse(raw); } catch (e) { next.body = raw; } }
                    if (inpCT.value.trim()) next.contentType = inpCT.value.trim();

                    n.label = inpLbl.value || n.label;
                    n.params = next;
                    ensurePosition(n); // ← mantener position

                    var el = nodeEl(n.id);
                    if (el) el.querySelector('.node__title').textContent = n.label;

                    renderInspector();
                };

                bDel.onclick = function () {
                    edges = edges.filter(function (e) { return e.from !== n.id && e.to !== n.id; });
                    nodes = nodes.filter(function (x) { return x.id !== n.id; });
                    var el = nodeEl(n.id); if (el) el.remove();
                    drawEdges(); select(null);
                };

                return;
            }

            // genérico
            var s1 = createEl('div', 'section');
            s1.innerHTML = '<div class="label">Etiqueta (label)</div>';
            var inp = createEl('input', 'input'); inp.value = n.label || ''; s1.appendChild(inp);

            var s2 = createEl('div', 'section');
            s2.innerHTML = '<div class="label">Parámetros (JSON)</div>';
            var ta = createEl('textarea', 'textarea'); ta.value = JSON.stringify(n.params || {}, null, 2); s2.appendChild(ta);
            var hint2 = createEl('div', 'hint'); hint2.textContent = 'Sugerencia: usá JSON válido.'; s2.appendChild(hint2);

            var s3 = createEl('div', 'section'); var row2 = createEl('div', 'btn-row'); s3.appendChild(row2);
            var bSave2 = createEl('button', 'btn'); bSave2.textContent = 'Guardar';
            var bTpl2 = createEl('button', 'btn'); bTpl2.textContent = 'Insertar plantilla';
            var bDel2 = createEl('button', 'btn'); bDel2.textContent = 'Eliminar nodo';
            row2.appendChild(bSave2); row2.appendChild(bTpl2); row2.appendChild(bDel2);

            body.appendChild(s1); body.appendChild(s2); body.appendChild(s3);

            bTpl2.onclick = function () {
                var tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES[n.key]) || {};
                ta.value = JSON.stringify(tpl, null, 2);
            };
            bSave2.onclick = function () {
                n.label = inp.value || n.label || '';
                try { n.params = ta.value.trim() ? JSON.parse(ta.value) : {}; }
                catch (e) { alert('JSON inválido en parámetros'); return; }
                ensurePosition(n); // ← mantener position
                var el = nodeEl(n.id); if (el) el.querySelector('.node__title').textContent = n.label;
                renderInspector();
            };
            bDel2.onclick = function () {
                edges = edges.filter(function (e) { return e.from !== n.id && e.to !== n.id; });
                nodes = nodes.filter(function (x) { return x.id !== n.id; });
                var el = nodeEl(n.id); if (el) el.remove();
                drawEdges(); select(null);
            };

        } else if (selected.type === 'edge') {
            var e = edges.find(function (x) { return x.id === selected.id });
            if (!e) { if (title) title.textContent = 'Arista no encontrada'; return; }

            if (title) title.textContent = 'Arista';
            if (sub) sub.textContent = e.from + ' → ' + e.to;

            var s1 = createEl('div', 'section');
            s1.innerHTML = '<div class="label">Condición</div>';
            var inpCond = createEl('input', 'input'); inpCond.value = e.condition || 'always'; s1.appendChild(inpCond);

            var s2 = createEl('div', 'section'); var row = createEl('div', 'btn-row'); s2.appendChild(row);
            var bSave = createEl('button', 'btn'); bSave.textContent = 'Guardar';
            var bDel = createEl('button', 'btn'); bDel.textContent = 'Eliminar arista';
            row.appendChild(bSave); row.appendChild(bDel);

            body.appendChild(s1); body.appendChild(s2);

            bSave.onclick = function () { e.condition = (inpCond.value || 'always').trim(); drawEdges(); renderInspector(); };
            bDel.onclick = function () { edges = edges.filter(function (x) { return x.id !== e.id }); drawEdges(); select(null); };
        }
    }

    // ====== export + validación
    function buildWorkflow() {
        var start = nodes.find(function (n) { return n.key === 'util.start' });
        var wf = { StartNodeId: start ? start.id : (nodes[0] ? nodes[0].id : null), Nodes: {}, Edges: [] };
        nodes.forEach(function (n) {
            ensurePosition(n);
            // Inyectamos position en Parameters
            var pars = Object.assign({}, n.params || {});
            pars.position = { x: n.x | 0, y: n.y | 0 };
            wf.Nodes[n.id] = { Id: n.id, Type: n.key, Parameters: pars };
        });
        edges.forEach(function (e) { wf.Edges.push({ From: e.from, To: e.to, Condition: (e.condition || 'always').trim() }) });
        return wf;
    }
    window.buildWorkflow = buildWorkflow; // para que el server lo pueda llamar
    window.captureWorkflow = function () {
        var hf = $('hfWorkflow') || $('hfWorkflowJson');
        if (!hf) return;
        var wf = buildWorkflow();
        hf.value = JSON.stringify(wf);
    };
    // Exponer también WF_getJson() (usado por wf_beforePostback)
    window.WF_getJson = function () { return JSON.stringify(buildWorkflow()); };

    function validate() {
        var g = buildWorkflow();
        var errors = [];
        if (!g.StartNodeId) errors.push("StartNodeId requerido.");

        var ids = Object.keys(g.Nodes || {});
        if (!ids.length) errors.push("Nodes vacío.");
        var startCount = 0, endCount = 0;
        ids.forEach(function (id) {
            var n = g.Nodes[id] || {};
            var type = (n.Type || '').toLowerCase();
            if (type === 'util.start') startCount++;
            if (type === 'util.end') endCount++;

            var p = n.Parameters || {};
            // data.sql: conexión + query/commandText
            if (type === 'data.sql') {
                var hasConn = !!(p.connection || p.connectionStringName);
                var hasCmd = !!(p.query || p.commandText);
                if (!hasConn) errors.push("Nodo " + id + ": falta 'connection' (o 'connectionStringName').");
                if (!hasCmd) errors.push("Nodo " + id + ": falta 'query' (o 'commandText').");
            }
            if (type === 'control.if') {
                if (!p.expression) errors.push("Nodo " + id + ": 'expression' requerido.");
            }
            if (p.position) {
                if (typeof p.position.x !== 'number' || typeof p.position.y !== 'number')
                    errors.push("Nodo " + id + ": position.x/y deben ser numéricos.");
            }
        });

        if (startCount !== 1) errors.push("Debe existir exactamente 1 nodo util.start.");
        if (endCount < 1) errors.push("Debe existir al menos 1 nodo util.end.");
        if (!g.Nodes[g.StartNodeId]) errors.push("StartNodeId no coincide con un nodo existente.");

        var allowed = { "always": 1, "true": 1, "false": 1, "error": 1 };
        (g.Edges || []).forEach(function (e) {
            if (!g.Nodes[e.From] || !g.Nodes[e.To]) errors.push("Arista inválida: " + e.From + " → " + e.To);
            var c = (e.Condition || "").toLowerCase();
            if (!allowed[c]) errors.push("Condition no válida en arista " + e.From + "→" + e.To + ": '" + e.Condition + "'");
        });

        return { ok: errors.length === 0, errors: errors, graph: g };
    }

    function showOutput(title, text) {
        var out = $('output'), ttl = $('outTitle'), txt = $('outText');
        if (!out || !ttl || !txt) {
            alert(title + "\n\n" + text);
            return;
        }
        ttl.textContent = title;
        txt.textContent = text;
        out.style.display = 'block';
    }
    window.showOutput = showOutput;

    // ====== init
    function init() {
        canvas = $('canvas'); svg = $('edgesSvg');

        // Restaurar si el server mandó algo (usa posiciones si existen)
        if (window.__WF_RESTORE) {
            try {
                var wf = (typeof window.__WF_RESTORE === 'string')
                    ? JSON.parse(window.__WF_RESTORE)
                    : window.__WF_RESTORE;
                if (wf && wf.Nodes) {
                    Object.keys(wf.Nodes).forEach(function (id) {
                        var n = wf.Nodes[id];
                        var meta = findCat(n.Type) || { key: n.Type, label: n.Type, tint: '#94a3b8' };
                        var hasPos = n.Parameters && n.Parameters.position && typeof n.Parameters.position.x === 'number' && typeof n.Parameters.position.y === 'number';
                        var x = hasPos ? n.Parameters.position.x : (120 + (Object.keys(nodes).length * 40));
                        var y = hasPos ? n.Parameters.position.y : (120 + (Object.keys(nodes).length * 10));
                        var newN = {
                            id: id,
                            key: meta.key,
                            label: n.Label || meta.label,
                            x: x,
                            y: y,
                            tint: meta.tint,
                            icon: meta.icon,
                            params: n.Parameters || {}
                        };
                        ensurePosition(newN);
                        nodes.push(newN);
                        drawNode(newN);
                    });
                    (wf.Edges || []).forEach(function (e) {
                        edges.push({ id: uid('e'), from: e.From, to: e.To, condition: e.Condition || 'always' });
                    });
                    drawEdges();
                }
            } catch (e) { console.warn(e); }
        }

        // toolbox
        if ($('search')) $('search').addEventListener('input', function () { renderToolbox(this.value); });
        renderToolbox("");

        // DnD solo UNA VEZ sobre el canvas
        canvas.addEventListener('dragover', function (ev) {
            ev.preventDefault();
            if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'copy';
        });
        canvas.addEventListener('drop', function (ev) {
            ev.preventDefault();
            var key = ev.dataTransfer.getData('text/plain') || ev.dataTransfer.getData('application/reactflow');
            if (!key) return;
            var meta = findCat(key); if (!meta) return;
            var rect = canvasRect(); var x = ev.clientX - rect.left, y = ev.clientY - rect.top;
            createNode(meta, x, y, meta.label);
        });
        canvas.addEventListener('click', function () { select(null); });
        window.addEventListener('resize', drawEdges);

        // toolbar actions
        if ($('btnConectar')) $('btnConectar').addEventListener('click', function () {
            connectMode = !connectMode; this.classList.toggle('active', connectMode); connectFrom = null;
        });
        if ($('btnClear')) $('btnClear').addEventListener('click', clearAll);

        if ($('btnDemo')) $('btnDemo').addEventListener('click', function () {
            clearAll();
            function P(x, y) { return { x: x, y: y } }

            var pStart = P(100, 160),
                pHttp = P(320, 160),
                pIf = P(540, 160),
                pChat = P(760, 120),
                pEnd = P(980, 160),
                pLog = P(760, 220);

            createNode(findCat('util.start'), pStart.x, pStart.y, 'Inicio');
            createNode(findCat('http.request'), pHttp.x, pHttp.y, 'Solicitud HTTP');
            createNode(findCat('control.if'), pIf.x, pIf.y, 'If status==200');
            createNode(findCat('chat.notify'), pChat.x, pChat.y, 'Chat OK');
            createNode(findCat('util.logger'), pLog.x, pLog.y, 'Logger Error');
            var logNodeObj = nodes.find(function (n) { return n.label === 'Logger Error'; });
            if (logNodeObj) { logNodeObj.params = { level: 'Error', message: 'Falló HTTP' }; ensurePosition(logNodeObj); }
            createNode(findCat('util.end'), pEnd.x, pEnd.y, 'Fin');

            var idOf = function (key, label) {
                var n = nodes.find(function (n) { return n.key === key && n.label === label; });
                return n && n.id;
            };

            var nStart = idOf('util.start', 'Inicio'),
                nHttp = idOf('http.request', 'Solicitud HTTP'),
                nIf = idOf('control.if', 'If status==200'),
                nChat = idOf('chat.notify', 'Chat OK'),
                nLog = idOf('util.logger', 'Logger Error'),
                nEnd = idOf('util.end', 'Fin');

            edges.push({ id: uid('e'), from: nStart, to: nHttp, condition: 'always' });
            edges.push({ id: uid('e'), from: nHttp, to: nIf, condition: 'always' });
            edges.push({ id: uid('e'), from: nIf, to: nChat, condition: 'true' });
            edges.push({ id: uid('e'), from: nIf, to: nEnd, condition: 'false' });
            edges.push({ id: uid('e'), from: nHttp, to: nLog, condition: 'error' });
            edges.push({ id: uid('e'), from: nLog, to: nEnd, condition: 'always' });

            drawEdges();
            renderInspector();
        });

        if ($('btnJSON')) $('btnJSON').addEventListener('click', function () {
            var wf = buildWorkflow();
            showOutput('Workflow JSON', JSON.stringify(wf, null, 2));
        });

        // NUEVO: botón guardar SQL con validación
        if ($('btnSaveSql')) $('btnSaveSql').addEventListener('click', function () {
            var v = validate();
            if (!v.ok) {
                showOutput('Validación', "- " + v.errors.join("\n- "));
                return;
            }
            // 1) mandar al hidden
            if (window.captureWorkflow) window.captureWorkflow();
            // 2) disparar postback
            if (typeof __doPostBack === 'function') {
                __doPostBack('WF_SAVE', '');
            } else {
                alert('No está disponible __doPostBack. ¿Falta ScriptManager en la página?');
            }
        });

        if ($('btnCopy')) $('btnCopy').onclick = function () { try { navigator.clipboard.writeText($('outText').textContent); } catch (e) { } }
        if ($('btnCloseOut')) $('btnCloseOut').onclick = function () { $('output').style.display = 'none'; }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
