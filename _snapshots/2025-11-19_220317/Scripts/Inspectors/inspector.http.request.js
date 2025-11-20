; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, kvTable, section, rowButtons, btn } = helpers;

    register('http.request', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Solicitud HTTP';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function cargar() {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request.templates']) || {};
            const opt = document.createElement('option'); opt.value = ''; opt.textContent = '— Elegir —'; selTpl.appendChild(opt);
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k; o.textContent = (pack[k].label || k); selTpl.appendChild(o);
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpUrl = el('input', 'input'); inpUrl.value = p.url || '';
        const sUrl = section('URL', inpUrl);

        const selMethod = el('select', 'input');
        ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'].forEach(m => {
            const o = document.createElement('option'); o.value = m; o.textContent = m;
            if ((p.method || 'GET').toUpperCase() === m) o.selected = true;
            selMethod.appendChild(o);
        });
        const sMet = section('Método', selMethod);

        const kvH = kvTable(p.headers || {}); const sH = section('Headers', kvH);
        const kvQ = kvTable(p.query || {}); const sQ = section('Query', kvQ);

        const taBody = el('textarea', 'textarea');
        taBody.value = (p.body == null ? '' : (typeof p.body === 'string' ? p.body : JSON.stringify(p.body, null, 2)));
        const sBody = section('Cuerpo', taBody);

        const inpCT = el('input', 'input'); inpCT.value = p.contentType || '';
        const sCT = section('Content-Type (opcional)', inpCT);

        const hint = el('div', 'hint'); hint.textContent = 'GET/HEAD ignoran body al enviar.';

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request.templates']) || {};
            const name = selTpl.value;
            const tpl = name && pack[name] ? pack[name] : ((window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['http.request']) || {});
            inpUrl.value = (tpl.url || '/Api/Ping.ashx');
            selMethod.value = ((tpl.method || 'GET') + '').toUpperCase();

            sH.replaceChildren(section('Headers', (function () { const x = kvTable(tpl.headers || {}); return x; })()).lastChild);
            sQ.replaceChildren(section('Query', (function () { const x = kvTable(tpl.query || {}); return x; })()).lastChild);

            if (tpl.body == null) taBody.value = '';
            else taBody.value = (typeof tpl.body === 'string') ? tpl.body : JSON.stringify(tpl.body, null, 2);
            inpCT.value = tpl.contentType || '';
        };

        bSave.onclick = () => {
            const next = {
                url: inpUrl.value.trim(),
                method: selMethod.value,
                headers: kvH.getValue(),
                query: kvQ.getValue(),
                timeoutMs: (p.timeoutMs || 10000)
            };
            const raw = taBody.value.trim();
            if (raw) { try { next.body = JSON.parse(raw); } catch { next.body = raw; } }
            if (inpCT.value.trim()) next.contentType = inpCT.value.trim();
            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);
            const elNode = nodeEl(node.id); if (elNode) elNode.querySelector('.node__title').textContent = node.label;
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
        };

        bDel.onclick = () => {
            ctx.edges = ctx.edges.filter(e => e.from !== node.id && e.to !== node.id);
            ctx.nodes = ctx.nodes.filter(x => x.id !== node.id);
            const elNode = ctx.nodeEl(node.id); if (elNode) elNode.remove();
            ctx.drawEdges(); ctx.select(null);
        };

        body.appendChild(sLbl); body.appendChild(sTpl);
        body.appendChild(sUrl); body.appendChild(sMet);
        body.appendChild(sH); body.appendChild(sQ);
        body.appendChild(sBody); body.appendChild(sCT);
        body.appendChild(hint);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
