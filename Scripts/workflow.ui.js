// Scripts/workflow.ui.js
// Mantiene TU UI y toolbox. Agrega:
// - Persistencia de posición en Parameters.position {x,y}
// - Validaciones cliente (start/end/edges & params básicos)
// - WF_getJson() para postbacks
// - Inspectores específicos: control.if y data.sql
// - Clonado profundo de plantillas para que cada nodo tenga su propio params
// - Recalcula idSeq tras rehidratación para evitar colisiones
(function () {
    window.__WF_UI_BUILD = 'concepto-a-zoom-minimap-dev210';
    console.log('workflow.ui.js build:', window.__WF_UI_BUILD);
    // ====== acceso a catálogo / plantillas / íconos
    var DATA = window.WorkflowData || {};
    var CATALOG = DATA.CATALOG || [];
    var GROUPS = DATA.GROUPS || [];
    var ICONS = DATA.ICONS || {};

    // ✅ FIX: NO pisar templates. Usar el global si existe.
    //    (workflow.templates.js llena window.PARAM_TEMPLATES)
    var PARAM_TEMPLATES = window.PARAM_TEMPLATES || DATA.PARAM_TEMPLATES || {};
    window.PARAM_TEMPLATES = PARAM_TEMPLATES; // asegura referencia global (pero NO lo vacía)


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
    var canvas, world, svg;
    var zoom = 1;
    var ZOOM_MIN = 0.5;
    var ZOOM_MAX = 1.8;
    var ZOOM_STEP = 0.1;
    var miniMapState = null;


    function canvasRect() { return canvas.getBoundingClientRect(); }
    function nodeEl(id) { return (world || canvas).querySelector('[data-node-id="' + id + '"]'); }
    function nodeById(id) {
        return nodes.find(function (n) { return n.id === id; });
    }

    function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

    function nodePosition(n, el) {
        // Posición LÓGICA del nodo dentro del grafo.
        // Con zoom activo, el DOM se dibuja escalado, por eso offsetLeft/Top
        // deben dividirse por zoom cuando se usan como respaldo.
        var x = (n && typeof n.x === 'number') ? n.x : null;
        var y = (n && typeof n.y === 'number') ? n.y : null;

        if ((x === null || y === null) && n && n.params && n.params.position) {
            if (x === null && typeof n.params.position.x === 'number') x = n.params.position.x;
            if (y === null && typeof n.params.position.y === 'number') y = n.params.position.y;
        }

        if ((x === null || y === null) && el) {
            if (x === null) x = ((typeof el.offsetLeft === 'number' ? el.offsetLeft : (parseInt(el.style.left, 10) || 0)) / zoom);
            if (y === null) y = ((typeof el.offsetTop === 'number' ? el.offsetTop : (parseInt(el.style.top, 10) || 0)) / zoom);
        }

        return { x: x || 0, y: y || 0 };
    }

    function applyNodeVisualPosition(n, el) {
        if (!n || !el) return;
        el.style.left = Math.round((n.x || 0) * zoom) + 'px';
        el.style.top = Math.round((n.y || 0) * zoom) + 'px';
        el.style.transform = 'scale(' + zoom + ')';
        el.style.transformOrigin = '0 0';
    }

    function applyAllNodeVisualPositions() {
        nodes.forEach(function (n) {
            var el = nodeEl(n.id);
            if (el) applyNodeVisualPosition(n, el);
        });
    }

    function nodeLogicalCenter(id) {
        var n = nodeById(id);
        var el = nodeEl(id);
        if (!n && !el) return null;
        var pos = nodePosition(n, el);
        var w = el ? el.offsetWidth : 180;
        var h = el ? el.offsetHeight : 70;
        return { x: pos.x + w / 2, y: pos.y + h / 2, w: w, h: h };
    }

    function syncNodeModelPosition(n, el) {
        if (!n || !el) return;
        var pos = nodePosition(n, el);
        n.x = pos.x | 0;
        n.y = pos.y | 0;
        n.params = n.params || {};
        n.params.position = { x: n.x, y: n.y };
    }

    function nodeCenter(id) {
        var n = nodeById(id);
        var el = nodeEl(id);
        if (!n && !el) return null;

        // Coordenadas VISUALES dentro de canvasWorld, ya multiplicadas por zoom.
        // Las aristas se dibujan en el mismo mundo visual que los nodos.
        var pos = nodePosition(n, el);
        var w = (el ? el.offsetWidth : 180) * zoom;
        var h = (el ? el.offsetHeight : 70) * zoom;
        var x = pos.x * zoom;
        var y = pos.y * zoom;

        return {
            x: x + w / 2,
            y: y + h / 2,
            left: x,
            top: y,
            right: x + w,
            bottom: y + h,
            w: w,
            h: h
        };
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
        applyNodeVisualPosition(n, el);
        // === FIX: asegurar referencia correcta del nodo en DOM ===
        el.dataset.nodeId = n.id;
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

        (world || canvas).appendChild(el);

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
        // Sincronizar antes de mover: el DOM manda.
        // Esto evita que el arrastre arranque desde una coordenada vieja del modelo.
        syncNodeModelPosition(n, el);

        var sx = ev.clientX;
        var sy = ev.clientY;
        var bx = n.x;
        var by = n.y;

        dragState = { n: n, el: el, sx: sx, sy: sy, bx: bx, by: by };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', endMove);
    }
    function onMove(ev) {
        if (!dragState) return;
        var dx = (ev.clientX - dragState.sx) / zoom, dy = (ev.clientY - dragState.sy) / zoom;
        var nx = dragState.bx + dx, ny = dragState.by + dy;

        // >>> snap
        var SNAP = 8;
        nx = Math.round(nx / SNAP) * SNAP;
        ny = Math.round(ny / SNAP) * SNAP;
        // <<<

        dragState.n.x = nx; dragState.n.y = ny;
        dragState.n.params = dragState.n.params || {};
        dragState.n.params.position = { x: nx | 0, y: ny | 0 };
        applyNodeVisualPosition(dragState.n, dragState.el);
        drawEdges();
    }
    function endMove() {
        document.removeEventListener('mousemove', onMove);
        document.removeEventListener('mouseup', endMove);
        dragState = null;
    }

    function clearAll() {
        nodes = []; edges = [];
        if (window.__WF_UI) {
            window.__WF_UI.nodes = nodes;
            window.__WF_UI.edges = edges;
        }
        (world || canvas).querySelectorAll('.node').forEach(function (e) { e.remove(); });
        drawEdges(); select(null);
    }

    function edgeAnchors(fromRect, toRect) {
        // Concepto A: cada nodo tiene 4 anclajes exactos:
        // top, right, bottom, left. Las flechas salen/llegan SOLO desde esos puntos.
        function chooseSide(a, b) {
            var dx = b.x - a.x;
            var dy = b.y - a.y;

            if (Math.abs(dx) >= Math.abs(dy)) {
                return dx >= 0 ? 'right' : 'left';
            }
            return dy >= 0 ? 'bottom' : 'top';
        }

        function pointOnSide(r, side) {
            if (side === 'top') return { x: r.x, y: r.top, side: side };
            if (side === 'bottom') return { x: r.x, y: r.bottom, side: side };
            if (side === 'left') return { x: r.left, y: r.y, side: side };
            return { x: r.right, y: r.y, side: 'right' };
        }

        function opposite(side) {
            if (side === 'top') return 'bottom';
            if (side === 'bottom') return 'top';
            if (side === 'left') return 'right';
            return 'left';
        }

        var fromSide = chooseSide(fromRect, toRect);
        var toSide = opposite(chooseSide(fromRect, toRect));

        // Si por la geometría del destino conviene otro lado más natural, usar el lado que mira al origen.
        toSide = chooseSide(toRect, fromRect);

        var start = pointOnSide(fromRect, fromSide);
        var end = pointOnSide(toRect, toSide);

        return {
            sx: start.x,
            sy: start.y,
            tx: end.x,
            ty: end.y,
            fromSide: start.side,
            toSide: end.side
        };
    }

    function resizeSvgToFitContent() {
        var maxX = canvas ? canvas.clientWidth : 0;
        var maxY = canvas ? canvas.clientHeight : 0;

        nodes.forEach(function (n) {
            var el = nodeEl(n.id);
            var pos = nodePosition(n, el);
            var w = el ? el.offsetWidth : 300;
            var h = el ? el.offsetHeight : 150;

            if ((pos.x + w + 160) * zoom > maxX) maxX = (pos.x + w + 160) * zoom;
            if ((pos.y + h + 160) * zoom > maxY) maxY = (pos.y + h + 160) * zoom;
        });

        maxX = Math.max(maxX, canvas ? canvas.clientWidth : 0);
        maxY = Math.max(maxY, canvas ? canvas.clientHeight : 0);

        if (world && world !== canvas) {
            world.style.width = Math.ceil(maxX) + 'px';
            world.style.height = Math.ceil(maxY) + 'px';
        }

        svg.setAttribute("width", Math.ceil(maxX));
        svg.setAttribute("height", Math.ceil(maxY));
        svg.style.width = Math.ceil(maxX) + 'px';
        svg.style.height = Math.ceil(maxY) + 'px';
    }

    // ====== aristas
    function drawEdges() {

        // Ajustar SVG al tamaño real del contenido
        resizeSvgToFitContent();

        // NO usar viewBox porque distorsiona al scrollear
        svg.removeAttribute("viewBox");

        // Limpiar todo menos <defs>
        while (svg.lastChild && svg.lastChild.tagName !== 'defs') {
            svg.removeChild(svg.lastChild);
        }

        edges.forEach(function (e) {
            var from = nodeCenter(e.from),
                to = nodeCenter(e.to);

            if (!from || !to) return;

            // Anclaje preciso sobre el borde de cada nodo
            var a = edgeAnchors(from, to);

            // Curvatura suave saliendo desde el puerto elegido.
            function tangent(side, len) {
                if (side === 'right') return { x: len, y: 0 };
                if (side === 'left') return { x: -len, y: 0 };
                if (side === 'bottom') return { x: 0, y: len };
                return { x: 0, y: -len };
            }

            var dist = Math.max(48, Math.min(180, Math.abs(a.tx - a.sx) + Math.abs(a.ty - a.sy)) * 0.35);
            var t1 = tangent(a.fromSide, dist);
            var t2 = tangent(a.toSide, dist);

            var d = 'M ' + a.sx + ' ' + a.sy +
                ' C ' + (a.sx + t1.x) + ' ' + (a.sy + t1.y) + ', ' +
                (a.tx + t2.x) + ' ' + (a.ty + t2.y) + ', ' +
                a.tx + ' ' + a.ty;

            // Path
            var p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            p.setAttribute('d', d);
            p.setAttribute(
                'class',
                'edge' + (selected && selected.type === 'edge' && selected.id === e.id ? ' selected' : '')
            );
            p.setAttribute('data-id', e.id);

            // Punta de flecha
            var markerId = (selected && selected.type === 'edge' && selected.id === e.id)
                ? 'arrowSel'
                : 'arrow';

            p.setAttribute('marker-end', 'url(#' + markerId + ')');

            p.addEventListener('click', function (ev) {
                select({ type: 'edge', id: e.id });
                ev.stopPropagation();
            });

            svg.appendChild(p);

            // Etiqueta centrada
            var label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
            label.setAttribute('font-size', '10');
            label.setAttribute('fill', '#64748b');

            var mx = (a.sx + a.tx) / 2;
            var my = (a.sy + a.ty) / 2 - 4;

            label.setAttribute('x', mx);
            label.setAttribute('y', my);
            label.textContent = e.condition || 'always';
            label.style.pointerEvents = 'none';

            svg.appendChild(label);
        });

        renderMiniMap();
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
                uid: uid,

                // ============================================================
                // === PROBAR EXTRACCIÓN (solo para vista previa en inspector)
                // ============================================================
                previewExtract: function (origenKey, rules) {

                    try {
                        // 1) Buscar nodo doc.load que haya puesto texto en ctx
                        let last = null;

                        for (let n of nodes) {
                            if (n.key === 'doc.load') {
                                last = n;
                            }
                        }

                        if (!last)
                            return { error: "No existe un nodo 'doc.load' en el grafo." };

                        // 2) Revisar si tiene texto cargado en params (modo vista previa)
                        const tmpText = last.params && last.params.previewText;
                        const text = tmpText || "";

                        const lines = text.split(/\r?\n/);
                        const salida = {};

                        for (const r of rules) {

                            // === POSICIONAL ===
                            if (r.linea && r.colDesde && r.largo) {
                                const ln = lines[r.linea - 1] || "";
                                salida[r.campo] = ln.substr(r.colDesde - 1, r.largo).trim();
                            }

                            // === REGEX ===
                            if (r.regex) {
                                const re = new RegExp(r.regex, 'i');
                                const m = re.exec(text);
                                if (m) {
                                    salida[r.campo] = r.grupo ? m[r.grupo] : m[0];
                                }
                            }
                        }

                        return salida;

                    } catch (err) {
                        return { error: "previewExtract EXCEPTION: " + err.message };
                    }
                }

            }, { body: body, title: title, sub: sub });

            // === FIX: asegurar que después de dibujar inspector no se destruyan edges ===
            requestAnimationFrame(() => {
                try { drawEdges(); } catch (e) { console.warn('drawEdges post-inspector', e); }
            });
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
            // Antes de guardar/exportar, tomar la posición real del DOM.
            // Así el JSON no persiste coordenadas viejas.
            syncNodeModelPosition(n, nodeEl(n.id));
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
                Id: e.id,                    // <<< AGREGAR ESTO
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
                const hasExpr = !!(p.expression && String(p.expression).trim());
                const hasSimple = !!(p.field && String(p.field).trim()) && !!(p.op && String(p.op).trim());
                const hasCompound = Array.isArray(p.rules) && p.rules.some(function (r) {
                    return r && String(r.field || r.fieldPath || '').trim() && String(r.op || r.operator || '').trim();
                });
                if (!hasExpr && !hasSimple && !hasCompound) {
                    errors.push("Nodo " + id + ": falta condición (simple field/op, compuesta rules[] o técnica expression).");
                }
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


    // ====== UI moderna: controles de canvas + minimapa liviano
    function updateZoomLabel() {
        var lbl = document.getElementById('wfZoomLabel');
        if (lbl) lbl.textContent = Math.round(zoom * 100) + '%';
    }

    function setZoom(newZoom, keepCenter) {
        if (!canvas) return;
        var oldZoom = zoom;
        newZoom = Math.round(clamp(newZoom, ZOOM_MIN, ZOOM_MAX) * 100) / 100;
        if (Math.abs(newZoom - oldZoom) < 0.001) return;

        var centerX = (canvas.scrollLeft + canvas.clientWidth / 2) / oldZoom;
        var centerY = (canvas.scrollTop + canvas.clientHeight / 2) / oldZoom;

        zoom = newZoom;
        applyAllNodeVisualPositions();
        updateZoomLabel();
        drawEdges();

        if (keepCenter !== false) {
            canvas.scrollLeft = Math.max(0, centerX * zoom - canvas.clientWidth / 2);
            canvas.scrollTop = Math.max(0, centerY * zoom - canvas.clientHeight / 2);
        }

        renderMiniMap();
    }

    function fitView() {
        if (!canvas || !nodes.length) return;
        var minX = Infinity, minY = Infinity, maxX = 0, maxY = 0;
        nodes.forEach(function (n) {
            var el = nodeEl(n.id);
            var pos = nodePosition(n, el);
            var w = el ? el.offsetWidth : 180;
            var h = el ? el.offsetHeight : 70;
            minX = Math.min(minX, pos.x);
            minY = Math.min(minY, pos.y);
            maxX = Math.max(maxX, pos.x + w);
            maxY = Math.max(maxY, pos.y + h);
        });
        if (!isFinite(minX) || !isFinite(minY)) return;

        var pad = 120;
        var graphW = Math.max(1, maxX - minX + pad * 2);
        var graphH = Math.max(1, maxY - minY + pad * 2);
        var z = Math.min(canvas.clientWidth / graphW, canvas.clientHeight / graphH);
        z = clamp(z, ZOOM_MIN, 1.2);
        zoom = Math.round(z * 100) / 100;
        applyAllNodeVisualPositions();
        updateZoomLabel();
        drawEdges();
        canvas.scrollLeft = Math.max(0, (minX - pad) * zoom);
        canvas.scrollTop = Math.max(0, (minY - pad) * zoom);
        renderMiniMap();
    }

    function ensureCanvasChrome() {
        if (!canvas) return;

        if (!document.getElementById('wfCanvasControls')) {
            var controls = createEl('div', 'canvas-controls');
            controls.id = 'wfCanvasControls';
            controls.innerHTML =
                '<button type="button" class="canvas-ctrl" id="wfZoomOut" title="Alejar">−</button>' +
                '<span class="canvas-zoom" id="wfZoomLabel">100%</span>' +
                '<button type="button" class="canvas-ctrl" id="wfZoomIn" title="Acercar">+</button>' +
                '<button type="button" class="canvas-ctrl" id="wfFitView" title="Ajustar vista">⛶</button>';
            canvas.appendChild(controls);

            var zin = document.getElementById('wfZoomIn');
            var zout = document.getElementById('wfZoomOut');
            var fit = document.getElementById('wfFitView');

            if (zin) zin.addEventListener('click', function () { setZoom(zoom + ZOOM_STEP, true); });
            if (zout) zout.addEventListener('click', function () { setZoom(zoom - ZOOM_STEP, true); });
            if (fit) fit.addEventListener('click', fitView);
            updateZoomLabel();
        }

        if (!document.getElementById('wfMiniMap')) {
            var mini = createEl('div', 'canvas-minimap');
            mini.id = 'wfMiniMap';
            mini.innerHTML = '<svg id="wfMiniMapSvg" viewBox="0 0 220 130" preserveAspectRatio="xMidYMid meet"></svg>';
            canvas.appendChild(mini);

            var miniSvg = document.getElementById('wfMiniMapSvg');
            if (miniSvg) {
                miniSvg.addEventListener('mousedown', startMiniMapPan);
            }
        }
    }

    function renderMiniMap() {
        var miniSvg = document.getElementById('wfMiniMapSvg');
        if (!miniSvg || !nodes.length) return;

        var SVG_NS = 'http://www.w3.org/2000/svg';
        while (miniSvg.firstChild) miniSvg.removeChild(miniSvg.firstChild);

        var minX = Infinity, minY = Infinity, maxX = 0, maxY = 0;
        nodes.forEach(function (n) {
            var el = nodeEl(n.id);
            var pos = nodePosition(n, el);
            var w = el ? el.offsetWidth : 180;
            var h = el ? el.offsetHeight : 70;
            minX = Math.min(minX, pos.x);
            minY = Math.min(minY, pos.y);
            maxX = Math.max(maxX, pos.x + w);
            maxY = Math.max(maxY, pos.y + h);
        });

        if (!isFinite(minX) || !isFinite(minY)) return;
        var pad = 24;
        var graphW = Math.max(1, maxX - minX + pad * 2);
        var graphH = Math.max(1, maxY - minY + pad * 2);
        var sx = 220 / graphW;
        var sy = 130 / graphH;
        var sc = Math.min(sx, sy);
        var ox = (220 - graphW * sc) / 2;
        var oy = (130 - graphH * sc) / 2;

        miniMapState = { minX: minX, minY: minY, pad: pad, sc: sc, ox: ox, oy: oy };

        function px(x) { return ox + (x - minX + pad) * sc; }
        function py(y) { return oy + (y - minY + pad) * sc; }

        edges.forEach(function (e) {
            var a = nodeLogicalCenter(e.from), b = nodeLogicalCenter(e.to);
            if (!a || !b) return;
            var line = document.createElementNS(SVG_NS, 'line');
            line.setAttribute('x1', px(a.x));
            line.setAttribute('y1', py(a.y));
            line.setAttribute('x2', px(b.x));
            line.setAttribute('y2', py(b.y));
            line.setAttribute('class', 'minimap-edge');
            miniSvg.appendChild(line);
        });

        nodes.forEach(function (n) {
            var el = nodeEl(n.id);
            var pos = nodePosition(n, el);
            var w = (el ? el.offsetWidth : 180) * sc;
            var h = (el ? el.offsetHeight : 70) * sc;
            var r = document.createElementNS(SVG_NS, 'rect');
            r.setAttribute('x', px(pos.x));
            r.setAttribute('y', py(pos.y));
            r.setAttribute('width', Math.max(8, w));
            r.setAttribute('height', Math.max(5, h));
            r.setAttribute('rx', 3);
            r.setAttribute('class', selected && selected.type === 'node' && selected.id === n.id ? 'minimap-node selected' : 'minimap-node');
            miniSvg.appendChild(r);
        });

        if (canvas) {
            var vp = document.createElementNS(SVG_NS, 'rect');
            vp.setAttribute('x', px(canvas.scrollLeft / zoom));
            vp.setAttribute('y', py(canvas.scrollTop / zoom));
            vp.setAttribute('width', Math.max(10, (canvas.clientWidth / zoom) * sc));
            vp.setAttribute('height', Math.max(8, (canvas.clientHeight / zoom) * sc));
            vp.setAttribute('rx', 4);
            vp.setAttribute('class', 'minimap-viewport');
            miniSvg.appendChild(vp);
        }
    }

    function miniMapEventToLogical(ev) {
        var miniSvg = document.getElementById('wfMiniMapSvg');
        if (!miniSvg || !miniMapState) return null;
        var r = miniSvg.getBoundingClientRect();
        if (!r.width || !r.height) return null;
        var svgX = (ev.clientX - r.left) * 220 / r.width;
        var svgY = (ev.clientY - r.top) * 130 / r.height;
        return {
            x: ((svgX - miniMapState.ox) / miniMapState.sc) + miniMapState.minX - miniMapState.pad,
            y: ((svgY - miniMapState.oy) / miniMapState.sc) + miniMapState.minY - miniMapState.pad
        };
    }

    function panCanvasToLogicalCenter(pt) {
        if (!canvas || !pt) return;
        canvas.scrollLeft = Math.max(0, pt.x * zoom - canvas.clientWidth / 2);
        canvas.scrollTop = Math.max(0, pt.y * zoom - canvas.clientHeight / 2);
        renderMiniMap();
    }

    var miniDrag = false;
    function startMiniMapPan(ev) {
        if (ev.button !== 0) return;
        ev.preventDefault();
        ev.stopPropagation();
        miniDrag = true;
        panCanvasToLogicalCenter(miniMapEventToLogical(ev));
        document.addEventListener('mousemove', onMiniMapPan);
        document.addEventListener('mouseup', endMiniMapPan);
    }

    function onMiniMapPan(ev) {
        if (!miniDrag) return;
        ev.preventDefault();
        panCanvasToLogicalCenter(miniMapEventToLogical(ev));
    }

    function endMiniMapPan() {
        miniDrag = false;
        document.removeEventListener('mousemove', onMiniMapPan);
        document.removeEventListener('mouseup', endMiniMapPan);
    }


    // ====== Asistente IA: aplicar propuesta al canvas (fix9)
    function normalizeAiText(v) {
        return String(v == null ? '' : v).trim().toLowerCase();
    }

    function aiActionKey(type, label) {
        return normalizeAiText(type) + '|' + normalizeAiText(label);
    }

    function aiEdgeCondition(value) {
        var v = normalizeAiText(value);
        if (!v || v === 'always') return 'always';
        if (v === 'si' || v === 'sí' || v === 'true') return 'true';
        if (v === 'no' || v === 'false') return 'false';
        return value;
    }

    function buildAiPlanWorkflow(plan) {
        plan = plan || {};
        var actions = Array.isArray(plan.actions) ? plan.actions : [];
        var connections = Array.isArray(plan.proposedConnections) ? plan.proposedConnections : [];

        var addActions = actions.filter(function (a) {
            return a && String(a.action || '').toUpperCase() === 'ADD_NODE' && a.nodeType;
        });

        if (!addActions.length) return null;

        var wf = {
            StartNodeId: null,
            Nodes: {},
            Edges: [],
            Meta: window.__WF_META || null
        };

        var actionIds = [];
        var byLabel = {};
        var byTypeLabel = {};

        addActions.forEach(function (a, i) {
            var id = 'n' + (i + 1);
            var type = String(a.nodeType || '').trim();
            var meta = findCat(type) || { key: type, label: type, tint: '#94a3b8', icon: 'box' };
            var label = String(a.label || meta.label || type).trim();

            actionIds.push(id);
            byLabel[normalizeAiText(label)] = id;
            byTypeLabel[aiActionKey(type, label)] = id;

            if (type === 'util.start' && !wf.StartNodeId) wf.StartNodeId = id;
        });

        if (!wf.StartNodeId && actionIds.length) wf.StartNodeId = actionIds[0];

        function findNodeId(label, type) {
            var key = aiActionKey(type || '', label || '');
            if (type && byTypeLabel[key]) return byTypeLabel[key];
            return byLabel[normalizeAiText(label)];
        }

        var graphEdges = [];
        connections.forEach(function (c) {
            if (!c) return;
            var fromId = findNodeId(c.from, c.fromNodeType);
            var toId = findNodeId(c.to, c.toNodeType);
            if (!fromId || !toId || fromId === toId) return;
            graphEdges.push({ from: fromId, to: toId, condition: aiEdgeCondition(c.condition) });
        });

        if (!graphEdges.length) {
            for (var i = 0; i < actionIds.length - 1; i++) {
                graphEdges.push({ from: actionIds[i], to: actionIds[i + 1], condition: 'always' });
            }
        }

        var layer = {};
        actionIds.forEach(function (id, idx) { layer[id] = idx; });
        if (wf.StartNodeId) layer[wf.StartNodeId] = 0;

        var changed = true;
        for (var pass = 0; pass < actionIds.length + 3 && changed; pass++) {
            changed = false;
            graphEdges.forEach(function (e) {
                var next = (layer[e.from] || 0) + 1;
                if ((layer[e.to] || 0) < next) {
                    layer[e.to] = next;
                    changed = true;
                }
            });
        }

        var baseX = 120, baseY = 180, dx = 270, dy = 150;
        var branchLaneCount = {};
        function incomingFor(id) {
            for (var i = 0; i < graphEdges.length; i++) {
                if (graphEdges[i].to === id) return graphEdges[i];
            }
            return null;
        }

        addActions.forEach(function (a, i) {
            var id = actionIds[i];
            var type = String(a.nodeType || '').trim();
            var meta = findCat(type) || { key: type, label: type, tint: '#94a3b8', icon: 'box' };
            var label = String(a.label || meta.label || type).trim();
            var params = deepClone(a.params || {});
            var inc = incomingFor(id);

            var x = baseX + ((layer[id] || i) * dx);
            var y = baseY;

            if (type === 'human.task' && inc) {
                if (inc.condition === 'true') y = baseY - dy;
                else if (inc.condition === 'false') y = baseY + dy;
                else y = baseY + dy;

                var laneKey = String(layer[id] || i) + '|' + y;
                branchLaneCount[laneKey] = (branchLaneCount[laneKey] || 0) + 1;
                if (branchLaneCount[laneKey] > 1) {
                    y += (branchLaneCount[laneKey] - 1) * 90;
                }
            }

            params.position = { x: x | 0, y: y | 0 };

            wf.Nodes[id] = {
                Id: id,
                Type: type,
                Label: label,
                Parameters: params
            };
        });

        var usedEdges = {};
        var edgeSeq = 1;
        graphEdges.forEach(function (e) {
            var cond = e.condition || 'always';
            var dedupeKey = e.from + '|' + e.to + '|' + cond;
            if (usedEdges[dedupeKey]) return;
            usedEdges[dedupeKey] = true;

            wf.Edges.push({
                Id: 'e' + (edgeSeq++),
                From: e.from,
                To: e.to,
                Condition: cond
            });
        });

        return wf;
    }

    function applyAiPlanToCanvas(plan, options) {
        options = options || {};
        plan = plan || {};

        var missing = Array.isArray(plan.missingData) ? plan.missingData : [];
        if (missing.length && !options.forceIncomplete) {
            return {
                ok: false,
                message: 'La propuesta tiene datos faltantes. Completalos antes de aplicarla al canvas.'
            };
        }

        var wf = buildAiPlanWorkflow(plan);
        if (!wf) {
            return {
                ok: false,
                message: 'No hay acciones suficientes para aplicar al canvas.'
            };
        }

        if (nodes.length > 0 && !options.replaceExisting) {
            var ok = window.confirm('Esto reemplazará el grafo actual del canvas por la propuesta del Asistente IA. ¿Querés continuar?');
            if (!ok) {
                return { ok: false, cancelled: true, message: 'Aplicación cancelada.' };
            }
        }

        WF_applyGraphFromObject(wf);
        if (window.__WF_UI) {
            window.__WF_UI.nodes = nodes;
            window.__WF_UI.edges = edges;
        }

        try { fitView(); } catch (e) { }
        try { drawEdges(); } catch (e2) { }
        try { renderMiniMap(); } catch (e3) { }

        return {
            ok: true,
            message: 'Propuesta aplicada al canvas. Revisá nodos, aristas y parámetros antes de guardar.'
        };
    }

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

    function convertListFormatToWorkflow(raw) {

        if (raw.Nodes && raw.Edges) return raw; // ya es formato correcto
        if (!Array.isArray(raw.nodes) || !Array.isArray(raw.edges)) return null;

        var wf = {
            StartNodeId: null,
            Nodes: {},
            Edges: [],
            Meta: { Name: raw.name || "Workflow importado" }
        };

        // Convertir nodos
        raw.nodes.forEach(n => {
            wf.Nodes[n.id] = {
                Id: n.id,
                Type: n.type,
                Label: n.label,
                Parameters: Object.assign({}, n.parameters || {}, {
                    position: { x: n.x || 100, y: n.y || 100 }
                })
            };

            if (n.type === "util.start")
                wf.StartNodeId = n.id;
        });

        // Si no había start node, ponemos el primero
        if (!wf.StartNodeId && raw.nodes.length > 0)
            wf.StartNodeId = raw.nodes[0].id;

        // Convertir edges
        raw.edges.forEach(e => {
            wf.Edges.push({
                From: e.from,
                To: e.to,
                Condition: e.label || "always"
            });
        });

        return wf;
    }



    // Cargar un grafo (objeto) en el canvas
    function WF_applyGraphFromObject(wf) {
        if (!wf || !wf.Nodes) {
            console.warn('WF_applyGraphFromObject: wf inválido o sin Nodes.', wf);
            return;
        }

        // Asegurar refs a canvas/svg por si alguien llama WF_loadFromJson antes de init()
        if (!canvas) {
            canvas = $('canvas');
            world = $('canvasWorld') || canvas;
        }
        if (!world) {
            world = $('canvasWorld') || canvas;
        }
        if (!svg) {
            svg = $('edgesSvg');
        }
        if (!canvas) {
            console.error('WF_applyGraphFromObject: canvas no encontrado.');
            return;
        }

        console.log('WF_applyGraphFromObject: cargando grafo con Nodes=', Object.keys(wf.Nodes).length,
            'Edges=', (wf.Edges || []).length);

        // limpiar lo que haya
        clearAll();

        Object.keys(wf.Nodes).forEach(function (id) {
            var n = wf.Nodes[id];
            var meta = findCat(n.Type) || { key: n.Type, label: n.Type, tint: '#94a3b8', icon: 'box' };

            var par = n.Parameters || n.params || {};
            var hasPos = par &&
                par.position &&
                typeof par.position.x === 'number' &&
                typeof par.position.y === 'number';
            var x = hasPos ? par.position.x : (120 + (nodes.length * 40));
            var y = hasPos ? par.position.y : (120 + (nodes.length * 10));

            var newN = {
                id: id,
                key: meta.key,
                label: n.Label || meta.label,
                x: x,
                y: y,
                tint: meta.tint,
                icon: meta.icon,
                params: deepClone(par)
            };
            ensurePosition(newN);
            nodes.push(newN);
            drawNode(newN);
        });

        // === FIX: preservar IDs reales y evitar colisiones ===
        (wf.Edges || []).forEach(function (e) {

            // respetar ID del JSON
            const edgeId = e.Id || e.id || ('e' + uid('fix'));

            edges.push({
                id: edgeId,
                from: e.From,
                to: e.To,
                condition: e.Condition || 'always'
            });
        });
        // === FIX: recalcular idSeq basado en nodos y edges cargados ===
        bumpIdSeqFromExisting();
        drawEdges();


        // Nombre del workflow en la caja de texto, si viene en Meta
        try {
            if (wf.Meta && wf.Meta.Name) {
                var t = document.getElementById('txtNombreWf');
                if (t) t.value = wf.Meta.Name;
            }
        } catch (e) { /* noop */ }
    }

    // Normaliza distintas formas de JSON para llegar a { StartNodeId, Nodes, Edges, Meta }
    // Normaliza distintas formas de JSON para llegar a { StartNodeId, Nodes, Edges, Meta }
    function normalizeWorkflowObject(raw) {
        var wf = raw || {};

        // Caso ideal: ya es el formato nuevo
        if (wf.Nodes && wf.Edges) {
            console.log('normalizeWorkflowObject: formato directo (root.Nodes)');
            return wf;
        }

        // Caso 1: string con JSON adentro en alguna propiedad "conocida"
        var stringProps = [
            'json', 'Json',
            'workflowJson', 'WorkflowJson',
            'graphJson', 'GraphJson'
        ];

        for (var i = 0; i < stringProps.length; i++) {
            var sp = stringProps[i];
            if (typeof wf[sp] === 'string') {
                try {
                    var inner = JSON.parse(wf[sp]);
                    if (inner && inner.Nodes && inner.Edges) {
                        console.log('normalizeWorkflowObject: usando inner string wf.' + sp);
                        return inner;
                    }
                } catch (e) {
                    console.warn('normalizeWorkflowObject: error parseando wf.' + sp, e);
                }
            }
        }

        // Caso 2: objetos anidados con nombres típicos
        var objProps = ['workflow', 'Workflow', 'graph', 'Graph'];
        for (var j = 0; j < objProps.length; j++) {
            var op = objProps[j];
            var v = wf[op];
            if (v && typeof v === 'object' && v.Nodes && v.Edges) {
                console.log('normalizeWorkflowObject: usando wf.' + op);
                return v;
            }
        }

        // Caso 3 (GENÉRICO): buscar en cualquier propiedad hija
        for (var k in wf) {
            if (!Object.prototype.hasOwnProperty.call(wf, k)) continue;
            var val = wf[k];

            // 3a) Si ya es un objeto con Nodes/Edges
            if (val && typeof val === 'object' && val.Nodes && val.Edges) {
                console.log('normalizeWorkflowObject: usando wf.' + k + ' (scan genérico)');
                return val;
            }

            // 3b) Si tiene una subpropiedad Json/string con el grafo
            if (val && typeof val === 'object') {
                var innerStr = val.Json || val.json || val.WorkflowJson || val.workflowJson;
                if (typeof innerStr === 'string') {
                    try {
                        var innerObj = JSON.parse(innerStr);
                        if (innerObj && innerObj.Nodes && innerObj.Edges) {
                            console.log('normalizeWorkflowObject: usando wf.' + k + '.Json (scan genérico)');
                            return innerObj;
                        }
                    } catch (e2) {
                        console.warn('normalizeWorkflowObject: error parseando wf.' + k + '.Json', e2);
                    }
                }
            }
        }

        // Si llegamos acá, no supimos normalizar
        console.warn('normalizeWorkflowObject: formato desconocido', wf);
        return wf; // WF_applyGraphFromObject va a volver a decir "wf inválido o sin Nodes"
    }

    // Cargar el JSON del TextBox (JsonServidor) en el canvas (sin postback)
    window.WF_cargarJsonServidorEnCanvas = function () {
        var tb = document.getElementById('JsonServidor');
        var raw = tb ? (tb.value || '').trim() : '';
        if (!raw) { alert('No hay JSON en el cuadro.'); return; }
        try {
            var wf = (typeof raw === 'string') ? JSON.parse(raw) : raw;
            window.WF_loadFromJson(wf);
        } catch (e) {
            console.warn(e);
            alert('JSON inválido. Revisá el contenido pegado.');
        }
    };

    // API pública para cargar desde JSON (string u objeto)
    window.WF_loadFromJson = function (jsonOrObject) {
        console.log('WF_loadFromJson llamado');

        var base = (typeof jsonOrObject === 'string')
            ? JSON.parse(jsonOrObject)
            : jsonOrObject;

        // Guardamos para inspección desde la consola si hace falta
        window.__WF_LAST_JSON_SERVER = base;
        console.log('WF_loadFromJson objeto base:', base);

        var wf = normalizeWorkflowObject(base);
        // Si no es formato workflow, intentar conversión automática:
        if (!wf.Nodes || !wf.Edges) {
            var converted = convertListFormatToWorkflow(wf);
            if (converted) wf = converted;
        }
        WF_applyGraphFromObject(wf);
    };



    function init() {
        canvas = $('canvas'); world = $('canvasWorld') || canvas; svg = $('edgesSvg');

        ensureCanvasChrome();
        ensureEdgeMarkers(svg);

        // Restaurar si el server mandó algo (usa posiciones si existen)
        if (window.__WF_RESTORE) {
            try {
                var wfRestore = (typeof window.__WF_RESTORE === 'string')
                    ? JSON.parse(window.__WF_RESTORE)
                    : window.__WF_RESTORE;
                WF_applyGraphFromObject(wfRestore);
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
            var rect = canvasRect();
            var x = (ev.clientX - rect.left + canvas.scrollLeft) / zoom;
            var y = (ev.clientY - rect.top + canvas.scrollTop) / zoom;
            createNode(meta, x, y, meta.label);
        });
        canvas.addEventListener('click', function (ev) {
            // Solo click izquierdo
            if (ev.button !== 0) return;
            // No deselecciones si el click viene de un nodo o del inspector
            if (ev.target && (ev.target.closest('.node') || ev.target.closest('#inspector'))) return;
            select(null);
        });
        window.addEventListener('resize', function () { drawEdges(); renderMiniMap(); });
        canvas.addEventListener('scroll', function () { drawEdges(); renderMiniMap(); });

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
        setZoom: setZoom,
        renderMiniMap: renderMiniMap,
        renderInspector: renderInspector,
        uid: uid,
        applyAiPlan: applyAiPlanToCanvas
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
