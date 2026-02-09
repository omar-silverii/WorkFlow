; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function opt(text, value) {
        const o = document.createElement('option');
        o.textContent = text;
        o.value = value;
        return o;
    }

    register('config.secrets', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Config Secrets';
        if (sub) sub.textContent = 'Lee secretos desde web.config (AppSettings/ConnectionStrings) o ENV. No loguea valores.';

        const p = node.params || {};

        // Label
        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // source
        const selSource = document.createElement('select');
        selSource.className = 'input';
        selSource.appendChild(opt('appSettings', 'appSettings'));
        selSource.appendChild(opt('connectionStrings', 'connectionStrings'));
        selSource.appendChild(opt('env', 'env'));
        selSource.value = p.source || 'appSettings';
        const sSource = section('source', selSource);

        // key
        const inpKey = el('input', 'input');
        inpKey.placeholder = 'Ej: ApiKey.Clientes';
        inpKey.value = p.key || '';
        const sKey = section('key (obligatorio)', inpKey);

        // output
        const inpOut = el('input', 'input');
        inpOut.placeholder = 'Ej: secrets.apiKey';
        inpOut.value = p.output || '';
        const sOut = section('output (path) - vacío => secrets.<key>', inpOut);

        // required
        const chkReq = el('input');
        chkReq.type = 'checkbox';
        chkReq.checked = (p.required === undefined) ? true : !!p.required;
        const sReq = section('required (si falta => error)', chkReq);

        // defaultValue
        const inpDef = el('input', 'input');
        inpDef.placeholder = 'defaultValue (solo si required=false)';
        inpDef.value = p.defaultValue || '';
        const sDef = section('defaultValue', inpDef);

        // maskInfo
        const chkMask = el('input');
        chkMask.type = 'checkbox';
        chkMask.checked = (p.maskInfo === undefined) ? true : !!p.maskInfo;
        const sMask = section('maskInfo (guarda .masked y .len)', chkMask);

        // buttons
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Config Secrets';

            const key = (inpKey.value || '').trim();
            if (!key) { alert('key es obligatorio.'); return; }

            const next = {
                source: selSource.value,
                key: key,
                required: chkReq.checked,
                maskInfo: chkMask.checked
            };

            const out = (inpOut.value || '').trim();
            if (out) next.output = out;

            const defv = (inpDef.value || '').trim();
            if (defv) next.defaultValue = defv;

            node.params = next;
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
        body.appendChild(sSource);
        body.appendChild(sKey);
        body.appendChild(sOut);
        body.appendChild(sReq);
        body.appendChild(sDef);
        body.appendChild(sMask);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
