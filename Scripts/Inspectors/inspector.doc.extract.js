; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('doc.extract', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Extraer datos de documento';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Etiqueta del nodo
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Origen: key en ctx.Estado donde está el texto
        const inpOrigen = el('input', 'input');
        inpOrigen.value = p.origen || 'archivo';
        const sOrigen = section('Origen (key en contexto)', inpOrigen);

        // Reglas en JSON
        const txtRules = document.createElement('textarea');
        txtRules.className = 'input';
        txtRules.rows = 10;
        txtRules.spellcheck = false;
        txtRules.style.fontFamily = 'monospace';

        txtRules.value = p.rulesJson || `[
  { "campo": "Poliza",  "linea": 3, "colDesde": 9,  "largo": 11 },
  { "campo": "Nombre",  "regex": "NOMBRE\\\\s*:\\\\s*(.+)", "grupo": 1 }
]`;

        const sRules = section('Reglas (JSON)', txtRules);

        const bEjemplo = btn('Insertar ejemplo');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bEjemplo.onclick = () => {
            txtRules.value = `[
  { "campo": "Indice",   "linea": 1, "colDesde": 1,  "largo": 8 },
  { "campo": "Cabecera", "linea": 2, "colDesde": 20, "largo": 30 },
  { "campo": "Fecha",    "linea": 3, "colDesde": 89, "largo": 8 },
  { "campo": "Nombre",   "regex": "NOMBRE\\\\s*:\\\\s*(.+)", "grupo": 1 }
]`;
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;
            node.params = {
                origen: inpOrigen.value || 'archivo',
                rulesJson: txtRules.value || ''
            };

            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
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


        body.appendChild(sLbl);
        body.appendChild(sOrigen);
        body.appendChild(sRules);
        body.appendChild(rowButtons(bSave, bDel, bEjemplo));
    });
})();
