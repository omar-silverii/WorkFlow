; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('email.send', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Correo: Enviar';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // --- Campos básicos ---
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const inpFrom = el('input', 'input');
        inpFrom.value = p.from || '';
        const sFrom = section('De (from)', inpFrom);

        const inpTo = el('input', 'input');
        // admitimos string o array
        if (Array.isArray(p.to)) {
            inpTo.value = p.to.join(',');
        } else {
            inpTo.value = p.to || '';
        }
        const sTo = section('Para (to, CSV)', inpTo);

        const inpCc = el('input', 'input');
        if (Array.isArray(p.cc)) {
            inpCc.value = p.cc.join(',');
        } else {
            inpCc.value = p.cc || '';
        }
        const sCc = section('CC (opcional, CSV)', inpCc);

        const inpSubject = el('input', 'input');
        inpSubject.value = p.subject || '';
        const sSubject = section('Asunto', inpSubject);

        const txtBody = document.createElement('textarea');
        txtBody.className = 'textarea';
        txtBody.rows = 6;
        txtBody.value = p.body || '';
        const sBody = section('Cuerpo', txtBody);

        // --- Opciones de envío ---
        const selModo = el('select', 'input');
        ['simulado', 'real'].forEach(m => {
            const o = document.createElement('option');
            o.value = m;
            o.textContent = m;
            if ((p.modo || 'simulado') === m) o.selected = true;
            selModo.appendChild(o);
        });
        const sModo = section('Modo', selModo);

        const inpHost = el('input', 'input');
        inpHost.value = p.host || '';
        const sHost = section('SMTP Host (ej: smtp.miempresa.com)', inpHost);

        const inpPort = el('input', 'input');
        inpPort.type = 'number';
        inpPort.min = '1';
        inpPort.max = '65535';
        inpPort.value = p.port != null ? String(p.port) : '25';
        const sPort = section('Puerto (por defecto 25)', inpPort);

        const inpUser = el('input', 'input');
        inpUser.value = p.user || '';
        const sUser = section('Usuario (opcional, si requiere auth)', inpUser);

        const inpPass = el('input', 'input');
        inpPass.type = 'password';
        inpPass.value = p.password || '';
        const sPass = section('Password', inpPass);

        function checkbox(label, checked) {
            const wrap = el('div', 'section');
            const id = 'chk_' + Math.random().toString(36).slice(2);
            wrap.innerHTML = '<label><input type="checkbox" id="' + id + '"> ' + label + '</label>';
            const ck = wrap.querySelector('#' + id);
            ck.checked = !!checked;
            return { wrap, input: ck };
        }

        const ckHtml = checkbox('Cuerpo en HTML', p.isHtml !== false); // default TRUE
        const ckSsl = checkbox('Usar SSL (TLS/SSL)', !!p.enableSsl);

        // --- Botones ---
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Correo: Enviar';

            function splitCsv(v) {
                return (v || '')
                    .split(',')
                    .map(s => s.trim())
                    .filter(Boolean);
            }

            node.params = Object.assign({}, node.params, {
                from: inpFrom.value || '',
                to: splitCsv(inpTo.value),
                cc: splitCsv(inpCc.value),
                subject: inpSubject.value || '',
                body: txtBody.value || '',
                modo: selModo.value || 'simulado',
                host: inpHost.value || '',
                port: parseInt(inpPort.value, 10) || 25,
                user: inpUser.value || '',
                password: inpPass.value || '',
                isHtml: !!ckHtml.input.checked,
                enableSsl: !!ckSsl.input.checked
            });

            ensurePosition(node);
            const elNode = nodeEl(node.id);
            if (elNode) elNode.querySelector('.node__title').textContent = node.label;

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            // === IMPORTANTE: mutar arrays con splice, NO reasignar ===
            if (Array.isArray(ctx.edges)) {
                for (let i = ctx.edges.length - 1; i >= 0; i--) {
                    const e = ctx.edges[i];
                    if (e && (e.from === node.id || e.to === node.id)) {
                        ctx.edges.splice(i, 1);
                    }
                }
            }

            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) {
                        ctx.nodes.splice(i, 1);
                    }
                }
            }

            const elNode = ctx.nodeEl(node.id);
            if (elNode) elNode.remove();
            ctx.drawEdges();
            ctx.select(null);
        };

        body.appendChild(sLbl);
        body.appendChild(sFrom);
        body.appendChild(sTo);
        body.appendChild(sCc);
        body.appendChild(sSubject);
        body.appendChild(sBody);
        body.appendChild(sModo);
        body.appendChild(sHost);
        body.appendChild(sPort);
        body.appendChild(sUser);
        body.appendChild(sPass);
        body.appendChild(ckHtml.wrap);
        body.appendChild(ckSsl.wrap);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
