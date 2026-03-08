(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    async function loadRoles() {
        const url = (window.WF_BASE_URL || '') + 'API/WF_Roles_List.ashx';
        const res = await fetch(url, { credentials: 'same-origin' });
        if (!res.ok) throw new Error('No se pudo cargar la lista de roles.');
        return await res.json();
    }

    function buildRolSelect(currentValue) {
        const sel = el('select', 'input');
        sel.innerHTML = '';

        const optEmpty = document.createElement('option');
        optEmpty.value = '';
        optEmpty.textContent = '-- seleccionar rol --';
        sel.appendChild(optEmpty);

        sel.value = currentValue || '';
        return sel;
    }

    function getRolDisplayText(sel) {
        if (!sel || !sel.options || sel.selectedIndex < 0) return '';
        const opt = sel.options[sel.selectedIndex];
        if (!opt) return '';

        const raw = (opt.textContent || '').trim();
        if (!raw || raw === '-- seleccionar rol --') return '';

        // Si viene "Compras (COMPRAS)" => usar "Compras"
        const p = raw.indexOf('(');
        if (p > 0) return raw.substring(0, p).trim();

        return raw;
    }

    function buildEstadoNegocioSugerido(selRol) {
        const txt = getRolDisplayText(selRol);
        if (!txt) return 'Pendiente';
        return 'Pendiente de ' + txt;
    }

    register('human.task', async (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Tarea humana';
        if (sub) sub.textContent = node.key || '';

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

        // Rol destino (combo)
        const selRol = buildRolSelect(p.rol || '');
        const sRol = section('Rol destino', selRol);

        const rolMsg = el('div', 'help');
        rolMsg.style.fontSize = '12px';
        rolMsg.style.marginTop = '6px';
        rolMsg.style.opacity = '0.8';
        rolMsg.textContent = 'Cargando roles...';
        sRol.appendChild(rolMsg);

        // Estado negocio pendiente
        const inpEstadoNeg = el('input', 'input');
        inpEstadoNeg.value = p.estadoNegocioPendiente || '';
        inpEstadoNeg.placeholder = 'Ej: Pendiente de Compras';
        const sEstadoNeg = section('Estado de negocio', inpEstadoNeg);

        const estadoMsg = el('div', 'help');
        estadoMsg.style.fontSize = '12px';
        estadoMsg.style.marginTop = '6px';
        estadoMsg.style.opacity = '0.8';
        estadoMsg.textContent = 'Si queda vacío, se sugerirá automáticamente según el rol.';
        sEstadoNeg.appendChild(estadoMsg);

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

        // Cargar roles desde BD
        try {
            const roles = await loadRoles();

            roles.forEach(r => {
                const opt = document.createElement('option');
                opt.value = r.RolKey || '';
                opt.textContent = r.Nombre
                    ? `${r.Nombre} (${r.RolKey})`
                    : (r.RolKey || '');
                selRol.appendChild(opt);
            });

            selRol.value = p.rol || '';
            rolMsg.textContent = 'Seleccione el rol destino desde la base.';

            if (!inpEstadoNeg.value || !inpEstadoNeg.value.trim()) {
                inpEstadoNeg.value = buildEstadoNegocioSugerido(selRol);
            }
        } catch (err) {
            rolMsg.textContent = 'No se pudieron cargar los roles.';
            rolMsg.style.color = '#b02a37';
        }

        selRol.addEventListener('change', () => {
            const actual = (inpEstadoNeg.value || '').trim();
            const sugeridoAnterior = buildEstadoNegocioSugerido({
                options: selRol.options,
                selectedIndex: selRol.selectedIndex
            });

            if (!actual || actual === 'Pendiente' || actual.indexOf('Pendiente de ') === 0) {
                inpEstadoNeg.value = buildEstadoNegocioSugerido(selRol);
            }
        });

        bTpl.onclick = () => {
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['human.task']) || {};
            if (def.titulo) inpTitulo.value = def.titulo;
            if (def.descripcion) inpDesc.value = def.descripcion;
            if (def.rol) selRol.value = def.rol;
            if (def.usuarioAsignado) inpUser.value = def.usuarioAsignado;
            if (typeof def.deadlineMinutes !== 'undefined') inpDead.value = def.deadlineMinutes;
            if (def.estadoNegocioPendiente) {
                inpEstadoNeg.value = def.estadoNegocioPendiente;
            } else if (!inpEstadoNeg.value || !inpEstadoNeg.value.trim()) {
                inpEstadoNeg.value = buildEstadoNegocioSugerido(selRol);
            }
        };

        bSave.onclick = () => {
            const rol = (selRol.value || '').trim();
            const usuarioAsignado = (inpUser.value || '').trim();

            if (!rol && !usuarioAsignado) {
                alert('Debe seleccionar un Rol destino o completar Usuario asignado.');
                try { selRol.focus(); } catch (e) { }
                return;
            }

            node.label = inpLbl.value || node.label || 'Tarea humana';

            const deadlineVal = inpDead.value ? parseInt(inpDead.value, 10) : null;
            let estadoNegocioPendiente = (inpEstadoNeg.value || '').trim();

            if (!estadoNegocioPendiente) {
                estadoNegocioPendiente = buildEstadoNegocioSugerido(selRol);
            }

            node.params = node.params || {};
            node.params.titulo = inpTitulo.value || '';
            node.params.descripcion = inpDesc.value || '';
            node.params.rol = rol;
            node.params.usuarioAsignado = usuarioAsignado;
            node.params.estadoNegocioPendiente = estadoNegocioPendiente;

            if (deadlineVal !== null && !isNaN(deadlineVal)) {
                node.params.deadlineMinutes = deadlineVal;
            } else {
                delete node.params.deadlineMinutes;
            }

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
        body.appendChild(sEstadoNeg);
        body.appendChild(sUser);
        body.appendChild(sDead);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();