// Scripts/workflow.ui.js
// Mantiene TU UI y toolbox. Agrega:
// - Persistencia de posición en Parameters.position {x,y}
// - Validaciones cliente (start/end/edges & params básicos)
// - WF_getJson() para postbacks
// - Inspectores específicos: control.if y data.sql
// - Clonado profundo de plantillas para que cada nodo tenga su propio params
// - Recalcula idSeq tras rehidratación para evitar colisiones
(function () {
    // ====== acceso a catálogo / plantillas / íconos
    var DATA = window.WorkflowData || {};
    var CATALOG = DATA.CATALOG || [];
    var GROUPS = DATA.GROUPS || [];
   
    var ICONS = DATA.ICONS || {};
    var PARAM_TEMPLATES = DATA.PARAM_TEMPLATES || {};
    window.PARAM_TEMPLATES = PARAM_TEMPLATES;

    // PACK movido a /Scripts/workflow.templates.js


    // ====== estado
    var nodes = [];   // {id,key,label,x,y,tint,icon,params}
    var edges = [];   // {id,from,to,condition}
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
    function deepClone(o) { try { return JSON.parse(JSON.stringify(o || {})); } catch (e) { return o ? Object.assign({}, o) : {}; } }

    // ====== refs a elementos
    var canvas, svg;

    function canvasRect() { return canvas.getBoundingClientRect(); }
    function nodeEl(id) { return document.getElementById(id); }
    function nodeCenter(id) {
        var el = nodeEl(id); if (!el) return null;
        var r = el.getBoundingClientRect(); var c = canvasRect();
        return { x: r.left - c.left + r.width / 2, y: r.top - c.top + r.height / 2, w: r.width, h: r.height };
    }

    function ensureEdgeMarkers(svgEl) {
        if (!svgEl) return;

        var SVG_NS = 'http://www.w3.org/2000/svg';

        // Asegurar <defs> y que quede al principio del SVG
        var defs = svgEl.querySelector('defs');
        if (!defs) {
            defs = document.createElementNS(SVG_NS, 'defs');
            svgEl.insertBefore(defs, svgEl.firstChild);
        }

        function upsertMarker(id, color) {
            var mk = defs.querySelector('#' + id);

            // Si existe pero no es <marker>, lo reemplazo
            if (mk && mk.tagName.toLowerCase() !== 'marker') {
                mk.remove();
                mk = null;
            }

            // Crear el <marker> si no existe
            if (!mk) {
                mk = document.createElementNS(SVG_NS, 'marker');
                mk.setAttribute('id', id);
                mk.setAttribute('viewBox', '0 0 10 10');
                mk.setAttribute('refX', '10');          // punta de flecha en el borde
                mk.setAttribute('refY', '5');
                mk.setAttribute('markerWidth', '8');
                mk.setAttribute('markerHeight', '8');
                mk.setAttribute('orient', 'auto');
                defs.appendChild(mk);
            }

            // Asegurar el <path> interno
            var path = mk.querySelector('path');
            if (!path) {
                path = document.createElementNS(SVG_NS, 'path');
                path.setAttribute('d', 'M 0 0 L 10 5 L 0 10 z');
                mk.appendChild(path);
            }

            path.setAttribute('fill', color);
        }

        upsertMarker('arrow', '#94a3b8'); // normal
        upsertMarker('arrowSel', '#0ea5e9'); // seleccionado
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
                icon.innerHTML = ICONS[it.icon] || ICONS['box'] || '';

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
    function ensurePosition(n) {
        n.params = n.params || {};
        if (!n.params.position || typeof n.params.position !== 'object') {
            n.params.position = { x: n.x | 0, y: n.y | 0 };
        } else {
            if (typeof n.params.position.x === 'number') n.x = n.params.position.x;
            if (typeof n.params.position.y === 'number') n.y = n.params.position.y;
        }
    }

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
            // CLONADO PROFUNDO: cada nodo recibe SU copia de la plantilla
            params: deepClone(((window.PARAM_TEMPLATES || {})[meta.key]) || {})
        };
        ensurePosition(n);
        nodes.push(n);
        drawNode(n);
        select({ type: 'node', id: id });
        drawEdges();
    }

    var dragState = null;
    function drawNode(n) {
        var el = createEl('div', 'node');
        el.id = n.id;
        el.style.left = (n.x | 0) + 'px';
        el.style.top = (n.y | 0) + 'px';
        var head = createEl('div', 'node__header');
        var ic = createEl('div', 'node__icon'); ic.style.background = hexToRgba(n.tint, .15); ic.style.color = n.tint; ic.innerHTML = ICONS[n.icon] || ICONS['box'] || '';
        var title = createEl('div', 'node__title'); title.textContent = n.label;
        var pill = createEl('div', 'pill'); pill.textContent = n.key;
        head.appendChild(ic); head.appendChild(title); head.appendChild(pill);
        var body = createEl('div', 'node__body'); body.textContent = 'Arrastrá para mover. (Conectar: botón "Conectar")';
        el.appendChild(head); el.appendChild(body);

        var ports = createEl('div');
        ['top', 'right', 'bottom', 'left'].forEach(function (pos) {
            var p = createEl('div', 'port ' + pos);
            ports.appendChild(p);
        });
        el.appendChild(ports);

        canvas.appendChild(el);

        el.addEventListener('mousedown', function (ev) {
            if (ev.button !== 0) return; // solo botón izquierdo
            if (connectMode) { handleConnectClick(n.id); ev.stopPropagation(); return; }
            select({ type: 'node', id: n.id });
            startMove(ev, n, el);
        });
        el.addEventListener('click', function (ev) {
            ev.stopPropagation();
            select({ type: 'node', id: n.id });
        });
        // Right-click: seleccionar sin abrir menú y sin limpiar
        el.addEventListener('contextmenu', function (ev) {
            ev.preventDefault();
            ev.stopPropagation();
            select({ type: 'node', id: n.id });
        });
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

        // >>> snap
        var SNAP = 8;
        nx = Math.round(nx / SNAP) * SNAP;
        ny = Math.round(ny / SNAP) * SNAP;
        // <<<

        dragState.n.x = nx; dragState.n.y = ny;
        dragState.n.params = dragState.n.params || {};
        dragState.n.params.position = { x: nx | 0, y: ny | 0 };
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

    function edgeAnchors(fromRect, toRect) {
        // fromRect / toRect: { x: centerX, y: centerY, w, h }
        var dx = toRect.x - fromRect.x;
        var dy = toRect.y - fromRect.y;

        // Pad ~ tamaño de la flecha para que la PUNTA quede en el borde del nodo
        var pad = 10;

        // Si predomina el desplazamiento vertical, usamos top/bottom; si no, left/right
        var useVertical = Math.abs(dy) > Math.abs(dx) * 0.8;

        if (useVertical) {
            if (dy >= 0) {
                // Receptor abajo: sale por abajo, entra por arriba
                return {
                    sx: fromRect.x, sy: fromRect.y + fromRect.h / 2 - pad, // bottom (from)
                    tx: toRect.x, ty: toRect.y - toRect.h / 2 + pad,     // top (to)
                    mode: 'vertical'
                };
            } else {
                // Receptor arriba: sale por arriba, entra por abajo
                return {
                    sx: fromRect.x, sy: fromRect.y - fromRect.h / 2 + pad, // top (from)
                    tx: toRect.x, ty: toRect.y + toRect.h / 2 - pad,     // bottom (to)
                    mode: 'vertical'
                };
            }
        } else {
            if (dx >= 0) {
                // Receptor a la derecha: sale por derecha, entra por izquierda
                return {
                    sx: fromRect.x + fromRect.w / 2 - pad, sy: fromRect.y, // right (from)
                    tx: toRect.x - toRect.w / 2 + pad, ty: toRect.y,   // left (to)
                    mode: 'horizontal'
                };
            } else {
                // Receptor a la izquierda: sale por izquierda, entra por derecha
                return {
                    sx: fromRect.x - fromRect.w / 2 + pad, sy: fromRect.y, // left (from)
                    tx: toRect.x + toRect.w / 2 - pad, ty: toRect.y,   // right (to)
                    mode: 'horizontal'
                };
            }
        }
    }

    // ====== aristas
    function drawEdges() {
        var r = canvasRect();
        svg.setAttribute('viewBox', '0 0 ' + r.width + ' ' + r.height);

        // Limpio todo menos <defs>
        while (svg.lastChild && svg.lastChild.tagName !== 'defs') {
            svg.removeChild(svg.lastChild);
        }

        edges.forEach(function (e) {
            var from = nodeCenter(e.from), to = nodeCenter(e.to);
            if (!from || !to) return;

            // Calcula anclajes inteligentes (dos salidas y dos entradas posibles)
            var a = edgeAnchors(from, to);

            // Curvas suaves
            var dx = Math.max(40, Math.abs(a.tx - a.sx) / 2);
            var dy = Math.max(40, Math.abs(a.ty - a.sy) / 2);

            var d;
            if (a.mode === 'horizontal') {
                // curva horizontal
                d = 'M ' + a.sx + ' ' + a.sy +
                    ' C ' + (a.sx + dx) + ' ' + a.sy + ', ' +
                    (a.tx - dx) + ' ' + a.ty + ', ' +
                    a.tx + ' ' + a.ty;
            } else {
                // curva vertical
                d = 'M ' + a.sx + ' ' + a.sy +
                    ' C ' + a.sx + ' ' + (a.sy + dy) + ', ' +
                    a.tx + ' ' + (a.ty - dy) + ', ' +
                    a.tx + ' ' + a.ty;
            }

            var p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            p.setAttribute('d', d);
            p.setAttribute('class', 'edge' + (selected && selected.type === 'edge' && selected.id === e.id ? ' selected' : ''));
            p.setAttribute('data-id', e.id);
            // Mantiene tus marcadores de flecha (punta llega al borde gracias al pad)
            p.setAttribute('marker-end', 'url(#' + (selected && selected.type === 'edge' && selected.id === e.id ? 'arrowSel' : 'arrow') + ')');
            p.addEventListener('click', function (ev) {
                select({ type: 'edge', id: e.id });
                ev.stopPropagation();
            });
            svg.appendChild(p);

            // Etiqueta de condición centrada
            var label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            label.setAttribute('font-size', '10');
            label.setAttribute('fill', '#64748b');
            var mx = (a.sx + a.tx) / 2, my = (a.sy + a.ty) / 2 - 4;
            label.setAttribute('x', mx);
            label.setAttribute('y', my);
            label.textContent = e.condition || 'always';
            label.style.pointerEvents = 'none';
            svg.appendChild(label);
        });
    }

    function handleConnectClick(nodeId) {
        if (!connectFrom) { connectFrom = nodeId; highlightNode(nodeId, true); return; }
        if (connectFrom === nodeId) { highlightNode(nodeId, false); connectFrom = null; return; }

        var cond = 'always';
        var fromNode = nodes.find(n => n.id === connectFrom);
        if (fromNode && fromNode.key === 'control.if') {
            var cFrom = nodeCenter(connectFrom);
            var cTo = nodeCenter(nodeId);
            if (cFrom && cTo) {
                var dx = cTo.x - cFrom.x, dy = Math.abs(cTo.y - cFrom.y);
                if (Math.abs(dx) > dy * 0.8) cond = (dx >= 0) ? 'true' : 'false'; // horizontal dominante
            }
        }

        var id = uid('e');
        edges.push({ id: id, from: connectFrom, to: nodeId, condition: cond });
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

    // Reemplazar TODO el cuerpo anterior de renderInspector por este:
    function renderInspector() {
        var body = document.getElementById('inspectorBody');
        var title = document.getElementById('inspectorTitle');
        var sub = document.getElementById('inspectorSub');

        if (!body) return;

        // Si hay un motor de inspectores externo, delegamos:
        if (window.WF_Inspector && typeof window.WF_Inspector.render === 'function') {
            window.WF_Inspector.render(selected, {
                nodes: nodes,
                edges: edges,
                ensurePosition: ensurePosition,
                nodeEl: nodeEl,
                drawEdges: drawEdges,
                select: select,
                uid: uid
            }, { body: body, title: title, sub: sub });
            return;
        }

        // Fallback por si falta el core (mensaje mínimo):
        body.innerHTML = '';
        if (title) title.textContent = 'Seleccioná un nodo o una arista';
        if (sub) sub.textContent = '';
    }


    // ====== export + validación
    function buildWorkflow() {
        var start = nodes.find(function (n) { return n.key === 'util.start'; });

        var wf = {
            StartNodeId: start ? start.id : (nodes[0] ? nodes[0].id : null),
            Nodes: {},
            Edges: [],
            Meta: (window.__WF_META || null)
        };

        nodes.forEach(function (n) {
            ensurePosition(n);

            var pars = Object.assign({}, n.params || {});
            pars.position = { x: n.x | 0, y: n.y | 0 };

            wf.Nodes[n.id] = {
                Id: n.id,
                Type: n.key,
                Label: n.label || null,   // <<< NUEVO: guardamos el texto del nodo
                Parameters: pars
            };
        });

        edges.forEach(function (e) {
            wf.Edges.push({
                From: e.from,
                To: e.to,
                Condition: (e.condition || 'always').trim()
            });
        });

        return wf;
    }

    window.buildWorkflow = buildWorkflow; // para que el server lo pueda llamar
    // Reemplazar SOLO esta función (dejar el nombre igual)
    window.captureWorkflow = function () {
        var hf = $('hfWorkflow') || document.getElementById('hfWorkflow'); // tu helper $
        if (!hf) return;

        var wf = (window.buildWorkflow ? window.buildWorkflow() : {});

        // Adjuntar el nombre escrito en txtNombreWf dentro de Meta.Name
        try {
            var t = document.getElementById('txtNombreWf');
            var name = t ? (t.value || '').trim() : '';
            if (name) {
                wf.Meta = wf.Meta || {};
                wf.Meta.Name = name;
            }
        } catch (e) { /* noop */ }

        hf.value = JSON.stringify(wf);
    };

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
    function bumpIdSeqFromExisting() {
        var maxN = 0, maxE = 0;
        nodes.forEach(function (n) {
            var m = /^n(\d+)$/i.exec(n.id || '');
            if (m) { var v = parseInt(m[1], 10) || 0; if (v > maxN) maxN = v; }
        });
        edges.forEach(function (e) {
            var m = /^e(\d+)$/i.exec(e.id || '');
            if (m) { var v = parseInt(m[1], 10) || 0; if (v > maxE) maxE = v; }
        });
        idSeq = Math.max(idSeq, maxN + 1, maxE + 1);
    }
 
    function init() {
        canvas = $('canvas'); svg = $('edgesSvg');

        ensureEdgeMarkers(svg);

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
                            params: n.Parameters ? deepClone(n.Parameters) : {}
                        };
                        ensurePosition(newN);
                        nodes.push(newN);
                        drawNode(newN);
                    });
                    (wf.Edges || []).forEach(function (e) {
                        edges.push({ id: uid('e'), from: e.From, to: e.To, condition: e.Condition || 'always' });
                    });
                    drawEdges();
                    bumpIdSeqFromExisting();
                }
            } catch (e) { console.warn(e); }
        }

        // toolbox
        if ($('search')) $('search').addEventListener('input', function () { renderToolbox(this.value); });
        renderToolbox("");

        console.log('CATALOG:', (window.WorkflowData && window.WorkflowData.CATALOG) || []);

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
        canvas.addEventListener('click', function (ev) {
            // Solo click izquierdo
            if (ev.button !== 0) return;
            // No deselecciones si el click viene de un nodo o del inspector
            if (ev.target && (ev.target.closest('.node') || ev.target.closest('#inspector'))) return;
            select(null);
        });
        window.addEventListener('resize', drawEdges);

        // toolbar actions
        if ($('btnConectar')) $('btnConectar').addEventListener('click', function () {
            connectMode = !connectMode;
            this.classList.toggle('active', connectMode);
            document.body.classList.toggle('connect-mode', connectMode); // <<< añade esta línea
            connectFrom = null;
        });

        if ($('btnClear')) $('btnClear').addEventListener('click', clearAll);

        // Demo Emisión (externo)
        if ($('btnDemoEmision')) $('btnDemoEmision').addEventListener('click', function () {
            if (window.WF_Demo && typeof window.WF_Demo.Emision === 'function') {
                window.WF_Demo.Emision(window.__WF_UI);
            } else {
                alert('Demo de Emisión no disponible. ¿Falta /Scripts/workflow.demo.js?');
            }
        });

        // Demo simple (externo)
        if ($('btnDemo')) $('btnDemo').addEventListener('click', function () {
            if (window.WF_Demo && typeof window.WF_Demo.Simple === 'function') {
                window.WF_Demo.Simple(window.__WF_UI);
            } else {
                alert('Demo simple no disponible. ¿Falta /Scripts/workflow.demo.js?');
            }
        });


        if ($('btnJSON')) $('btnJSON').addEventListener('click', function () {
            var wf = buildWorkflow();
            showOutput('Workflow JSON', JSON.stringify(wf, null, 2));
        });

        // botón guardar SQL con validación
        if ($('btnSaveSql')) $('btnSaveSql').addEventListener('click', function () {
            var v = validate();
            if (!v.ok) {
                showOutput('Validación', "- " + v.errors.join("\n- "));
                return;
            }
            if (window.captureWorkflow) window.captureWorkflow();
            if (typeof __doPostBack === 'function') {
                __doPostBack('WF_SAVE', '');
            } else {
                alert('No está disponible __doPostBack. ¿Falta ScriptManager en la página?');
            }
        });

        if ($('btnCopy')) $('btnCopy').onclick = function () { try { navigator.clipboard.writeText($('outText').textContent); } catch (e) { } }
        if ($('btnCloseOut')) $('btnCloseOut').onclick = function () { $('output').style.display = 'none'; }

        try {
            var t = document.getElementById('txtNombreWf');
            if (t && window.__WF_RESTORE && window.__WF_RESTORE.Meta && window.__WF_RESTORE.Meta.Name) {
                t.value = window.__WF_RESTORE.Meta.Name;
            }
        } catch (e) { /* noop */ }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
    // === API pública mínima para demos / plugins ===
    // PEGAR justo antes de la última línea "})();"
    window.__WF_UI = {
        nodes: nodes,
        edges: edges,
        findCat: findCat,
        createNode: createNode,
        clearAll: clearAll,
        drawEdges: drawEdges,
        renderInspector: renderInspector,
        uid: uid
    };
    try {
        window.dispatchEvent(new Event('wf-ui-ready'));
    } catch (e) {
        // fallback para navegadores viejos
        var evt = document.createEvent('Event');
        evt.initEvent('wf-ui-ready', true, true);
        window.dispatchEvent(evt);
    }
})();
