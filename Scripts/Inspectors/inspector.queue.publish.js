; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function removeNode(node, ctx) {
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
    }

    function updateTitle(node, ctx) {
        const elNode = ctx.nodeEl(node.id);
        if (elNode) {
            const title = elNode.querySelector('.node__title');
            if (title) title.textContent = node.label;
        }
    }

    function parseJsonOrText(raw) {
        raw = (raw || '').trim();
        if (!raw) return null;
        try { return JSON.parse(raw); }
        catch { return raw; }
    }

    register('queue.publish', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Cola: Publicar';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish.templates']) || {};
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                selTpl.appendChild(o);
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpBroker = el('input', 'input'); inpBroker.value = p.broker || 'sql';
        const sBroker = section('Broker', inpBroker);

        const inpQueue = el('input', 'input'); inpQueue.value = p.queue || '';
        const sQueue = section('Queue', inpQueue);

        const inpConn = el('input', 'input'); inpConn.value = p.connectionStringName || 'DefaultConnection';
        const sConn = section('ConnectionString', inpConn);

        const taPayload = el('textarea', 'textarea');
        taPayload.rows = 5;
        taPayload.style.resize = 'vertical';
        taPayload.value = (p.payload === undefined || p.payload === null)
            ? ''
            : (typeof p.payload === 'string' ? p.payload : JSON.stringify(p.payload, null, 2));
        const sPayload = section('Payload (texto o JSON)', taPayload);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.publish']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;
            inpBroker.value = tpl.broker || 'sql';
            inpQueue.value = tpl.queue || '';
            inpConn.value = tpl.connectionStringName || 'DefaultConnection';
            taPayload.value = (tpl.payload === undefined || tpl.payload === null)
                ? ''
                : (typeof tpl.payload === 'string' ? tpl.payload : JSON.stringify(tpl.payload, null, 2));
        };

        bSave.onclick = () => {
            const next = {
                broker: inpBroker.value || 'sql',
                queue: inpQueue.value || '',
                connectionStringName: inpConn.value || 'DefaultConnection'
            };

            const payload = parseJsonOrText(taPayload.value);
            if (payload !== null) next.payload = payload;

            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);
            updateTitle(node, ctx);
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); } }, 0);
        };

        bDel.onclick = () => removeNode(node, ctx);

        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sBroker);
        body.appendChild(sQueue);
        body.appendChild(sConn);
        body.appendChild(sPayload);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });

    register('queue.consume', (node, ctx, dom) => {
        const { ensurePosition } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Cola: Consumir';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.consume.templates']) || {};
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                selTpl.appendChild(o);
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        const inpBroker = el('input', 'input'); inpBroker.value = p.broker || 'sql';
        const sBroker = section('Broker', inpBroker);

        const inpQueue = el('input', 'input'); inpQueue.value = p.queue || '';
        const sQueue = section('Queue', inpQueue);

        const inpTake = el('input', 'input'); inpTake.type = 'number'; inpTake.min = '1'; inpTake.value = p.take || 1;
        const sTake = section('Take', inpTake);

        const inpPrefetch = el('input', 'input'); inpPrefetch.type = 'number'; inpPrefetch.min = '1'; inpPrefetch.value = p.prefetch || p.take || 1;
        const sPrefetch = section('Prefetch', inpPrefetch);

        const inpConn = el('input', 'input'); inpConn.value = p.connectionStringName || 'DefaultConnection';
        const sConn = section('ConnectionString', inpConn);

        const inpOutput = el('input', 'input'); inpOutput.value = p.outputPrefix || 'queue.consume';
        const sOutput = section('Output prefix', inpOutput);

        const chkDebug = el('input');
        chkDebug.type = 'checkbox';
        chkDebug.checked = !!p.debug;
        const debugWrap = el('label');
        debugWrap.style.display = 'flex';
        debugWrap.style.gap = '8px';
        debugWrap.style.alignItems = 'center';
        debugWrap.appendChild(chkDebug);
        debugWrap.appendChild(document.createTextNode('Mostrar logs debug'));
        const sDebug = section('Debug', debugWrap);

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.consume.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['queue.consume']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;
            inpBroker.value = tpl.broker || 'sql';
            inpQueue.value = tpl.queue || '';
            inpTake.value = tpl.take || 1;
            inpPrefetch.value = tpl.prefetch || tpl.take || 1;
            inpConn.value = tpl.connectionStringName || 'DefaultConnection';
            inpOutput.value = tpl.outputPrefix || 'queue.consume';
            chkDebug.checked = !!tpl.debug;
        };

        bSave.onclick = () => {
            const take = parseInt(inpTake.value, 10);
            const prefetch = parseInt(inpPrefetch.value, 10);

            const next = {
                broker: inpBroker.value || 'sql',
                queue: inpQueue.value || '',
                take: (!isNaN(take) && take > 0) ? take : 1,
                prefetch: (!isNaN(prefetch) && prefetch > 0) ? prefetch : 1,
                connectionStringName: inpConn.value || 'DefaultConnection',
                outputPrefix: inpOutput.value || 'queue.consume',
                debug: !!chkDebug.checked
            };

            node.label = inpLbl.value || node.label;
            node.params = next;
            ensurePosition(node);
            updateTitle(node, ctx);
            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);
            setTimeout(() => { try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); } }, 0);
        };

        bDel.onclick = () => removeNode(node, ctx);

        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sBroker);
        body.appendChild(sQueue);
        body.appendChild(sTake);
        body.appendChild(sPrefetch);
        body.appendChild(sConn);
        body.appendChild(sOutput);
        body.appendChild(sDebug);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
