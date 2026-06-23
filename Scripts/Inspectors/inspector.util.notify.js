; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function getParam(p, a, b, def) {
        if (p && p[a] != null) return p[a];
        if (b && p && p[b] != null) return p[b];
        return def;
    }

    function addOption(sel, value, text, selectedValue) {
        const o = document.createElement('option');
        o.value = value;
        o.textContent = text;
        if (String(selectedValue || '') === String(value || '')) o.selected = true;
        sel.appendChild(o);
        return o;
    }

    async function loadRoles() {
        const url = (window.WF_BASE_URL || '') + 'API/WF_Roles_List.ashx';
        const res = await fetch(url, { credentials: 'same-origin' });
        if (!res.ok) throw new Error('No se pudo cargar la lista de roles.');
        return await res.json();
    }

    function normalizeNivel(v) {
        const raw = String(v || 'info').trim().toLowerCase();
        if (raw === 'warn' || raw === 'warning') return 'warn';
        if (raw === 'error') return 'error';
        if (raw === 'debug') return 'debug';
        return 'info';
    }

    function normalizeDestinoTipo(v, p) {
        const raw = String(v || '').trim().toLowerCase();
        if (raw === 'usuario' || raw === 'rol' || raw === 'sistema') return raw;
        if (p && p.rolDestino) return 'rol';
        if (p && p.usuarioDestino) return 'usuario';
        return 'usuarioActual';
    }

    function insertAtCursor(textbox, text) {
        const ta = textbox;
        const start = (typeof ta.selectionStart === 'number') ? ta.selectionStart : ta.value.length;
        const end = (typeof ta.selectionEnd === 'number') ? ta.selectionEnd : ta.value.length;
        ta.value = ta.value.substring(0, start) + text + ta.value.substring(end);
        ta.focus();
        ta.selectionStart = ta.selectionEnd = start + text.length;
    }

    function wireFieldPicker(button, ctx, target, title, mode) {
        button.onclick = () => {
            if (!window.WF_FieldPicker) {
                alert('WF_FieldPicker no está cargado');
                return;
            }
            window.WF_FieldPicker.open({
                ctx,
                title: title || 'Elegir dato disponible',
                onPick: (v) => {
                    const expr = '${' + v + '}';
                    if (mode === 'replace') {
                        target.value = expr;
                        target.focus();
                    } else {
                        insertAtCursor(target, expr);
                    }
                }
            });
        };
    }

    register('util.notify', async (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Notificar';
        if (sub) sub.textContent = node.key || 'util.notify';

        const p = node.params || {};

        // Etiqueta visual
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || 'Notificar';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Tipo / canal / nivel / prioridad
        const selCanal = el('select', 'input');
        const currentCanal = String(getParam(p, 'canal', 'tipo', 'sistema') || 'sistema').toLowerCase();
        addOption(selCanal, 'sistema', 'Sistema / interno', currentCanal);
        addOption(selCanal, 'log', 'Sólo log', currentCanal);
        addOption(selCanal, 'email', 'Email solicitado (no envía correo real)', currentCanal);
        const sCanal = section('Canal', selCanal);

        const selNivel = el('select', 'input');
        const currentNivel = normalizeNivel(getParam(p, 'nivel', null, 'info'));
        addOption(selNivel, 'info', 'Info', currentNivel);
        addOption(selNivel, 'warn', 'Warn', currentNivel);
        addOption(selNivel, 'error', 'Error', currentNivel);
        addOption(selNivel, 'debug', 'Debug', currentNivel);
        const sNivel = section('Nivel', selNivel);

        const selPrioridad = el('select', 'input');
        const currentPrioridad = String(getParam(p, 'prioridad', null, 'normal') || 'normal').toLowerCase();
        addOption(selPrioridad, 'normal', 'Normal', currentPrioridad);
        addOption(selPrioridad, 'alta', 'Alta', currentPrioridad);
        addOption(selPrioridad, 'baja', 'Baja', currentPrioridad);
        const sPrioridad = section('Prioridad', selPrioridad);

        // Destino
        const selDestinoTipo = el('select', 'input');
        const currentDestinoTipo = normalizeDestinoTipo(p.destinoTipo, p);
        addOption(selDestinoTipo, 'usuarioActual', 'Usuario actual', currentDestinoTipo);
        addOption(selDestinoTipo, 'usuario', 'Usuario específico', currentDestinoTipo);
        addOption(selDestinoTipo, 'rol', 'Rol', currentDestinoTipo);
        addOption(selDestinoTipo, 'sistema', 'Sistema / general', currentDestinoTipo);
        const sDestinoTipo = section('Destino', selDestinoTipo);

        const inpUsuario = el('input', 'input');
        inpUsuario.value = p.usuarioDestino || '';
        inpUsuario.placeholder = 'Ej.: OMARD\\USUARIO';
        const sUsuario = section('Usuario destino', inpUsuario);

        const selRol = el('select', 'input');
        selRol.innerHTML = '';
        addOption(selRol, '', '-- seleccionar rol --', p.rolDestino || p.destino || '');
        const rolMsg = el('div', 'help');
        rolMsg.style.fontSize = '12px';
        rolMsg.style.marginTop = '6px';
        rolMsg.style.opacity = '0.8';
        rolMsg.textContent = 'Cargando roles...';
        const rolWrap = el('div');
        rolWrap.appendChild(selRol);
        rolWrap.appendChild(rolMsg);
        const sRol = section('Rol destino', rolWrap);

        const inpDestino = el('input', 'input');
        inpDestino.value = p.destino || '';
        inpDestino.placeholder = 'Destino libre / compatibilidad';
        const sDestinoLibre = section('Destino libre', inpDestino);

        // Asunto
        const inpAsunto = el('input', 'input');
        inpAsunto.value = getParam(p, 'asunto', 'titulo', getParam(p, 'title', null, 'Aviso Workflow Studio')) || '';
        const sAsunto = section('Asunto / título', inpAsunto);

        // Mensaje
        const taMensaje = el('textarea', 'input');
        taMensaje.rows = 6;
        taMensaje.style.resize = 'vertical';
        taMensaje.value = getParam(p, 'mensaje', 'message', '') || '';
        taMensaje.placeholder = 'Mensaje. Podés usar ${wf.instanceId}, ${biz.campo}, etc.';

        const bPickMsg = btn('Insertar dato…');
        bPickMsg.style.marginTop = '6px';
        wireFieldPicker(bPickMsg, ctx, taMensaje, 'Insertar dato en mensaje', 'insert');

        const msgHint = el('div');
        msgHint.className = 'hint';
        msgHint.textContent = 'util.notify crea una notificación interna/sistema. No envía correo real; para correo usar email.send.';

        const msgWrap = el('div');
        msgWrap.appendChild(taMensaje);
        msgWrap.appendChild(bPickMsg);
        msgWrap.appendChild(msgHint);
        const sMensaje = section('Mensaje', msgWrap);

        // URL acción
        const inpUrl = el('input', 'input');
        inpUrl.value = p.urlAccion || '';
        inpUrl.placeholder = 'Vacío = instancia actual si existe';
        const sUrl = section('URL acción', inpUrl);

        function syncDestino() {
            const t = selDestinoTipo.value || 'usuarioActual';
            sUsuario.style.display = t === 'usuario' ? '' : 'none';
            sRol.style.display = t === 'rol' ? '' : 'none';
            sDestinoLibre.style.display = 'none';
        }
        selDestinoTipo.addEventListener('change', syncDestino);
        syncDestino();

        try {
            const data = await loadRoles();
            const roles = Array.isArray(data) ? data : (data && Array.isArray(data.roles) ? data.roles : []);
            const selected = p.rolDestino || ((p.destinoTipo === 'rol') ? p.destino : '') || '';

            roles.forEach(r => {
                const key = r.RolKey || r.rolKey || r.key || r.Id || r.id || '';
                const name = r.Nombre || r.nombre || r.Name || r.name || key;
                if (!key) return;
                const text = name && name !== key ? (key + ' - ' + name) : key;
                addOption(selRol, key, text, selected);
            });

            if (selected && !Array.prototype.some.call(selRol.options, o => o.value === selected)) {
                addOption(selRol, selected, selected, selected);
            }

            rolMsg.textContent = roles.length ? '' : 'No se encontraron roles activos.';
        } catch (ex) {
            rolMsg.textContent = 'No se pudieron cargar roles. Podés escribir destino libre desde JSON si hace falta.';
            if (p.rolDestino) addOption(selRol, p.rolDestino, p.rolDestino, p.rolDestino);
        }

        // Plantilla mínima
        const bTpl = btn('Plantilla Compras');
        bTpl.onclick = () => {
            inpLbl.value = 'Notificar';
            selCanal.value = 'sistema';
            selNivel.value = 'info';
            selPrioridad.value = 'normal';
            selDestinoTipo.value = 'rol';
            syncDestino();
            if (!selRol.value) selRol.value = 'COMPRAS';
            inpAsunto.value = 'Aviso para Compras';
            taMensaje.value = 'Hay una revisión pendiente. Instancia=${wf.instanceId}';
        };

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Notificar';

            const destinoTipo = selDestinoTipo.value || 'usuarioActual';
            const params = {
                tipo: 'sistema',
                canal: selCanal.value || 'sistema',
                nivel: selNivel.value || 'info',
                destinoTipo: destinoTipo,
                prioridad: selPrioridad.value || 'normal',
                asunto: inpAsunto.value || 'Notificación',
                mensaje: taMensaje.value || '',
                urlAccion: inpUrl.value || ''
            };

            if (destinoTipo === 'usuario') {
                params.usuarioDestino = inpUsuario.value || '';
                params.destino = inpUsuario.value || '';
            } else if (destinoTipo === 'rol') {
                params.rolDestino = selRol.value || '';
                params.destino = selRol.value || '';
            } else if (destinoTipo === 'sistema') {
                params.destino = 'sistema';
            }

            node.params = params;
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
        body.appendChild(sCanal);
        body.appendChild(sNivel);
        body.appendChild(sPrioridad);
        body.appendChild(sDestinoTipo);
        body.appendChild(sUsuario);
        body.appendChild(sRol);
        body.appendChild(sDestinoLibre);
        body.appendChild(sAsunto);
        body.appendChild(sMensaje);
        body.appendChild(sUrl);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
