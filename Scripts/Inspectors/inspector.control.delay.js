; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('control.delay', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Demora (Delay)';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};
        const tpl = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['control.delay']) || {};

        // Label
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // ms
        const inpMs = el('input', 'input');
        inpMs.type = 'number';
        inpMs.min = '0';
        inpMs.step = '1';
        inpMs.placeholder = 'Ej: 1500';
        inpMs.value = (p.ms != null ? p.ms : (tpl.ms != null ? tpl.ms : 1000));
        const sMs = section('Milisegundos (ms)', inpMs);

        // seconds (opcional)
        const inpSec = el('input', 'input');
        inpSec.type = 'text';
        inpSec.placeholder = 'Ej: 1.5 (opcional, si no usás ms)';
        inpSec.value = (p.seconds != null ? String(p.seconds) : (tpl.seconds != null ? String(tpl.seconds) : ''));
        const sSec = section('Segundos (seconds) (opcional)', inpSec);

        // message
        const inpMsg = el('input', 'input');
        inpMsg.placeholder = 'Ej: Esperando respuesta...';
        inpMsg.value = (p.message != null ? String(p.message) : (tpl.message != null ? String(tpl.message) : ''));
        const sMsg = section('Mensaje de log (opcional)', inpMsg);

        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Demora';

            // parse ms
            let ms = 0;
            try { ms = parseInt(inpMs.value, 10) || 0; } catch (e) { ms = 0; }

            const secondsRaw = (inpSec.value || '').trim();
            const messageRaw = (inpMsg.value || '').trim();

            const nextParams = Object.assign({}, node.params);

            // Regla: si ms > 0, guardo ms y limpio seconds
            if (ms > 0) {
                nextParams.ms = ms;
                if (nextParams.seconds != null) delete nextParams.seconds;
            } else {
                // si no hay ms, guardo seconds si viene
                if (secondsRaw) nextParams.seconds = secondsRaw;
                else if (nextParams.seconds != null) delete nextParams.seconds;

                if (nextParams.ms != null) delete nextParams.ms;
            }

            if (messageRaw) nextParams.message = messageRaw;
            else if (nextParams.message != null) delete nextParams.message;

            node.params = nextParams;

            ensurePosition(node);
            const elN = nodeEl(node.id);
            if (elN) {
                const t = elN.querySelector('.node__title');
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
        body.appendChild(sMs);
        body.appendChild(sSec);
        body.appendChild(sMsg);
        body.appendChild(rowButtons(bSave, bDel));
    });
})();
