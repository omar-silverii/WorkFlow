// Inspector de ARISTAS (edge) con combo de condición
// Depende sólo de window.WF_Inspector.render (si no existe register, nos enganchamos igual)

(function () {
    function renderEdge(selected, ctx, ui) {
        if (!selected || selected.type !== 'edge') return false;

        var e = (ctx.edges || []).find(function (x) { return x.id === selected.id; });
        if (!e) return false;

        var body = ui.body, title = ui.title, sub = ui.sub;
        body.innerHTML = '';

        // Titulares
        if (title) title.textContent = 'Arista';
        var fromN = (ctx.nodes || []).find(function (n) { return n.id === e.from; });
        var toN = (ctx.nodes || []).find(function (n) { return n.id === e.to; });
        if (sub) sub.textContent = (fromN ? (fromN.label || e.from) : e.from) + ' → ' + (toN ? (toN.label || e.to) : e.to);

        // ====== Condición (combo)
        var sec = document.createElement('div'); sec.className = 'section';
        var lab = document.createElement('div'); lab.className = 'label'; lab.textContent = 'Condición';
        var sel = document.createElement('select'); sel.className = 'input';

        ['always', 'true', 'false', 'error'].forEach(function (opt) {
            var o = document.createElement('option');
            o.value = opt; o.textContent = opt;
            if ((e.condition || 'always').toLowerCase() === opt) o.selected = true;
            sel.appendChild(o);
        });

        sec.appendChild(lab); sec.appendChild(sel);
        body.appendChild(sec);

        // ====== Botonera
        var secBtns = document.createElement('div'); secBtns.className = 'section';
        var row = document.createElement('div'); row.className = 'btn-row';

        var bSave = document.createElement('button'); bSave.type = 'button'; bSave.className = 'btn'; bSave.textContent = 'Guardar';
        var bDel = document.createElement('button'); bDel.type = 'button'; bDel.className = 'btn'; bDel.textContent = 'Eliminar arista';

        row.appendChild(bSave); row.appendChild(bDel);
        secBtns.appendChild(row);
        body.appendChild(secBtns);

        // Eventos
        bSave.onclick = function () {
            e.condition = (sel.value || 'always').trim();
            ctx.drawEdges(); // refrescar SVG
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


        // UX: guardar con Enter
        sel.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter') { bSave.click(); ev.preventDefault(); }
        });

        return true; // handled
    }

    // === Registro en el core del inspector ===
    window.WF_Inspector = window.WF_Inspector || {};

    // 1) Si el core expone un registro tipo register(...), lo usamos
    if (typeof window.WF_Inspector.register === 'function') {
        window.WF_Inspector.register('edge', renderEdge);
        return;
    }

    // 2) Si no, nos “inyectamos” envolviendo WF_Inspector.render sin romper lo existente
    var originalRender = window.WF_Inspector.render;
    window.WF_Inspector.render = function (selected, ctx, ui) {
        // Si es arista y lo manejamos, listo
        var handled = renderEdge(selected, ctx, ui);
        if (handled) return;

        // Sino, delegamos al core (si existe)
        if (typeof originalRender === 'function') {
            return originalRender(selected, ctx, ui);
        }
    };
})();
