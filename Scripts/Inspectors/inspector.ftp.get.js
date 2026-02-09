; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, btn } = helpers;

    register('ftp.get', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'FTP: Descargar archivo';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

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

        const inpRemote = el('input', 'input');
        inpRemote.value = p.remotePath || '/in/archivo.txt';
        const sRemote = section('Ruta remota (remotePath)', inpRemote);

        const inpLocal = el('input', 'input');
        inpLocal.value = p.localPath || 'C:/temp/descarga.txt';
        const sLocal = section('Ruta local (localPath)', inpLocal);

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
        const ckOverwrite = checkbox('Sobrescribir local si existe', p.overwrite !== false ? true : false);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'FTP: Descargar';

            node.params = Object.assign({}, node.params, {
                host: inpHost.value || '',
                user: inpUser.value || '',
                password: inpPass.value || '',
                remotePath: inpRemote.value || '',
                localPath: inpLocal.value || '',
                passive: !!ckPassive.input.checked,
                ssl: !!ckSsl.input.checked,
                overwrite: !!ckOverwrite.input.checked
            });

            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
        };

        bDel.onclick = () => {
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (!e) continue;
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }
            if (ctx.nodes && ctx.nodes[node.id]) delete ctx.nodes[node.id];
            if (ctx.removeNode) ctx.removeNode(node.id);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { } }, 0);
            body.innerHTML = '';
        };

        body.appendChild(sLbl);
        body.appendChild(sHost);
        body.appendChild(sUser);
        body.appendChild(sPass);
        body.appendChild(sRemote);
        body.appendChild(sLocal);
        body.appendChild(ckPassive.wrap);
        body.appendChild(ckSsl.wrap);
        body.appendChild(ckOverwrite.wrap);
        body.appendChild(bSave);
        body.appendChild(bDel);
    });
})();
