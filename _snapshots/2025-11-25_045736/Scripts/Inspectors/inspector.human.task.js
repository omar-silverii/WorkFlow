(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    // Inspector para nodos de tipo "human.task"
    register('human.task', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Tarea humana';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // === Label visual del nodo ===
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // === Título de la tarea ===
        const inpTitulo = el('input', 'input');
        inpTitulo.value = p.titulo || '';
        const sTitulo = section('Título de la tarea', inpTitulo);

        // === Descripción ===
        const inpDesc = el('input', 'input');
        inpDesc.value = p.descripcion || '';
        const sDesc = section('Descripción', inpDesc);

        // === Rol destino (Recepción, RRHH, Médico, etc.) ===
        const inpRol = el('input', 'input');
        inpRol.value = p.rol || '';
        const sRol = section('Rol destino (ej: RRHH, Recepción)', inpRol);

        // === Usuario asignado (opcional) ===
        const inpUser = el('input', 'input');
        inpUser.value = p.usuarioAsignado || '';
        const sUser = section('Usuario asignado (opcional)', inpUser);

        // === Deadline en minutos (opcional) ===
        const inpDead = el('input', 'input');
        inpDead.type = 'number';
        inpDead.min = '0';
        inpDead.value = (p.deadlineMinutes !== undefined && p.deadlineMinutes !== null)
            ? p.deadlineMinutes
            : '';
        const sDead = section('Deadline en minutos (opcional)', inpDead);

        // === Botones ===
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        // Usa PARAM_TEMPLATES['human.task'] como plantilla por defecto
        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['human.task']) || {};
            if (def.titulo) inpTitulo.value = def.titulo;
            if (def.descripcion) inpDesc.value = def.descripcion;
            if (def.rol) inpRol.value = def.rol;
            if (def.usuarioAsignado) inpUser.value = def.usuarioAsignado;
            if (typeof def.deadlineMinutes !== 'undefined') {
                inpDead.value = def.deadlineMinutes;
            }
        };

        bSave.onclick = () => {
            // Actualizar label del nodo
            node.label = inpLbl.value || node.label || 'Tarea humana';

            // Guardar parámetros
            const deadlineVal = inpDead.value ? parseInt(inpDead.value, 10) : null;

            node.params = {
                titulo: inpTitulo.value || '',
                descripcion: inpDesc.value || '',
                rol: inpRol.value || '',
                usuarioAsignado: inpUser.value || '',
                // solo guardo si tiene valor, para no meter nulls al cohete
                ...(deadlineVal !== null ? { deadlineMinutes: deadlineVal } : {})
            };

            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            ctx.edges = ctx.edges.filter(e => e.from !== node.id && e.to !== node.id);
            ctx.nodes = ctx.nodes.filter(x => x.id !== node.id);
            const elNode = ctx.nodeEl(node.id); if (elNode) elNode.remove();
            ctx.drawEdges(); ctx.select(null);
        };

        body.appendChild(sLbl);
        body.appendChild(sTitulo);
        body.appendChild(sDesc);
        body.appendChild(sRol);
        body.appendChild(sUser);
        body.appendChild(sDead);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
