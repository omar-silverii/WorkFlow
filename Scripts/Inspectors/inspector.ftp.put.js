; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('ftp.put', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'FTP: Subir archivo';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // Campos
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const inpHost = el('input', 'input');
        inpHost.value = p.host || '';
        const sHost = section('Host (ej: ftp.miempresa.com)', inpHost);

        const inpUser = el('input', 'input');
        inpUser.value = p.user || '';
        const sUser = section('Usuario', inpUser);

        const inpPass = el('input', 'input');
        inpPass.type = 'password';
        inpPass.value = p.password || '';
        const sPass = section('Password', inpPass);

        const inpLocal = el('input', 'input');
        inpLocal.value = p.localPath || 'C:/temp/salida.txt';
        const sLocal = section('Ruta local (localPath)', inpLocal);

        const inpRemote = el('input', 'input');
        inpRemote.value = p.remotePath || '/out/salida.txt';
        const sRemote = section('Ruta remota (remotePath)', inpRemote);

        function checkbox(label, checked) {
            const wrap = el('div', 'section');
            const id = 'chk_' + Math.random().toString(36).slice(2);
            wrap.innerHTML = '<label><input type="checkbox" id="' + id + '"> ' + label + '</label>';
            const ck = wrap.querySelector('#' + id);
            ck.checked = !!checked;
            return { wrap, input: ck };
        }

        const ckPassive = checkbox('Usar modo pasivo (recomendado)', p.passive !== false);
        const ckSsl = checkbox('Usar SSL (FTPS)', !!p.ssl);
        const ckOverwrite = checkbox('Sobrescribir si existe', p.overwrite !== false ? true : false);

        // Botones
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'FTP: Subir';

            node.params = Object.assign({}, node.params, {
                host: inpHost.value || '',
                user: inpUser.value || '',
                password: inpPass.value || '',
                localPath: inpLocal.value || '',
                remotePath: inpRemote.value || '',
                passive: !!ckPassive.input.checked,
                ssl: !!ckSsl.input.checked,
                overwrite: !!ckOverwrite.input.checked
            });

            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
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


        body.appendChild(sLbl);
        body.appendChild(sHost);
        body.appendChild(sUser);
        body.appendChild(sPass);
        body.appendChild(sLocal);
        body.appendChild(sRemote);
        body.appendChild(ckPassive.wrap);
        body.appendChild(ckSsl.wrap);
        body.appendChild(ckOverwrite.wrap);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();

