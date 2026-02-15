; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function toInt(v, def) {
        const n = parseInt(v, 10);
        return isNaN(n) ? def : n;
    }

    register('control.loop', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Loop';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';

        // forEach
        const inpForEach = el('input', 'input');
        inpForEach.placeholder = 'Ej: ${payload.items}';
        inpForEach.value = (p.forEach != null ? String(p.forEach) : '');

        // itemVar
        const inpItemVar = el('input', 'input');
        inpItemVar.placeholder = 'Ej: item';
        inpItemVar.value = (p.itemVar != null ? String(p.itemVar) : 'item');

        // max
        const inpMax = el('input', 'input');
        inpMax.type = 'number';
        inpMax.value = (p.max != null ? String(p.max) : '');

        const sLbl = section('Etiqueta (label)', inpLbl);
        const sForEach = section('ForEach (lista o ${ruta})', inpForEach);
        const sItemVar = section('Variable del ítem (itemVar)', inpItemVar);
        const sMax = section('Máximo iteraciones (opcional)', inpMax);

        // Nota de funcionamiento
        const note = el('div');
        note.style.marginTop = '8px';
        note.innerHTML =
            '<div style="font-size:12px;color:rgba(0,0,0,.65)">' +
            'Este nodo itera sobre una lista y expone el ítem actual en <b>itemVar</b>. ' +
            'Salida: <b>true</b> mientras haya ítems; <b>false</b> al finalizar.' +
            '</div>';

        // Botones estándar
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            const next = {
                forEach: (inpForEach.value || '').trim(),
                itemVar: (inpItemVar.value || 'item').trim() || 'item'
            };

            const maxVal = (inpMax.value || '').trim();
            if (maxVal !== '') {
                next.max = Math.max(1, Math.min(1000000, toInt(maxVal, 0)));
            }

            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
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
        body.appendChild(sForEach);
        body.appendChild(sItemVar);
        body.appendChild(sMax);
        body.appendChild(note);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
