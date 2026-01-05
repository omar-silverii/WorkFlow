(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('human.task', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Tarea humana';
        if (sub) sub.textContent = node.key || '';

        // ✅ ÚNICO estándar: params
        const p = node.params || {};

        // Label visual
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Título
        const inpTitulo = el('input', 'input');
        inpTitulo.value = p.titulo || '';
        const sTitulo = section('Título de la tarea', inpTitulo);

        // Descripción
        const inpDesc = el('input', 'input');
        inpDesc.value = p.descripcion || '';
        const sDesc = section('Descripción', inpDesc);

        // Rol
        const inpRol = el('input', 'input');
        inpRol.value = p.rol || '';
        const sRol = section('Rol destino (ej: RRHH, Recepción)', inpRol);

        // Usuario asignado
        const inpUser = el('input', 'input');
        inpUser.value = p.usuarioAsignado || '';
        const sUser = section('Usuario asignado (opcional)', inpUser);

        // Deadline
        const inpDead = el('input', 'input');
        inpDead.type = 'number';
        inpDead.min = '0';
        inpDead.value = (p.deadlineMinutes !== undefined && p.deadlineMinutes !== null)
            ? p.deadlineMinutes
            : '';
        const sDead = section('Deadline en minutos (opcional)', inpDead);

        // Botones
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['human.task']) || {};
            if (def.titulo) inpTitulo.value = def.titulo;
            if (def.descripcion) inpDesc.value = def.descripcion;
            if (def.rol) inpRol.value = def.rol;
            if (def.usuarioAsignado) inpUser.value = def.usuarioAsignado;
            if (typeof def.deadlineMinutes !== 'undefined') inpDead.value = def.deadlineMinutes;
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Tarea humana';

            const deadlineVal = inpDead.value ? parseInt(inpDead.value, 10) : null;

            // ✅ Guardar en params (lo que exporta buildWorkflow)
            node.params = node.params || {};
            node.params.titulo = inpTitulo.value || '';
            node.params.descripcion = inpDesc.value || '';
            node.params.rol = inpRol.value || '';
            node.params.usuarioAsignado = inpUser.value || '';
            if (deadlineVal !== null) node.params.deadlineMinutes = deadlineVal;
            else delete node.params.deadlineMinutes;

            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
        };

        bDel.onclick = () => {
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (e && (e.from === node.id || e.to === node.id)) ctx.edges.splice(i, 1);
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
        body.appendChild(sTitulo);
        body.appendChild(sDesc);
        body.appendChild(sRol);
        body.appendChild(sUser);
        body.appendChild(sDead);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
