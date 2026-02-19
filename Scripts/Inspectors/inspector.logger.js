; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    register('util.logger', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';

        if (title) title.textContent = node.label || 'Logger';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // =======================
        // Label
        // =======================
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // =======================
        // Plantillas
        // =======================
        const selTpl = el('select', 'input');
        (function () {
            const opt0 = document.createElement('option');
            opt0.value = '';
            opt0.textContent = '— Elegir —';
            selTpl.appendChild(opt0);

            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.logger.templates']) || {};
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                selTpl.appendChild(o);
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        // =======================
        // Level
        // =======================
        const selLevel = el('select', 'input');
        ['Trace', 'Debug', 'Info', 'Warning', 'Error', 'Fatal'].forEach(lv => {
            const o = document.createElement('option');
            o.value = lv;
            o.textContent = lv;
            if ((p.level || 'Info') === lv) o.selected = true;
            selLevel.appendChild(o);
        });
        const sLevel = section('Level', selLevel);

        // =======================
        // Message (textarea grande)
        // =======================
        const inpMsg = el('textarea', 'input');
        inpMsg.rows = 5;
        inpMsg.style.resize = 'vertical';
        inpMsg.style.fontFamily = 'monospace';
        inpMsg.style.fontSize = '12px';
        inpMsg.value = (p.message != null ? String(p.message) : '');
        inpMsg.placeholder = 'Podés usar ${...}';

        const btnPickMsg = btn('Elegir…');
        btnPickMsg.style.marginTop = '6px';

        const msgWrap = el('div');
        msgWrap.appendChild(inpMsg);
        msgWrap.appendChild(btnPickMsg);

        const sMsg = section('Message', msgWrap);

        btnPickMsg.onclick = () => {
            if (!window.WF_FieldPicker) { alert('WF_FieldPicker no está cargado'); return; }
            window.WF_FieldPicker.open({
                ctx,
                title: 'Insertar campo en mensaje',
                onPick: (v) => {
                    const ins = '${' + v + '}';
                    const ta = inpMsg;
                    const start = (typeof ta.selectionStart === 'number') ? ta.selectionStart : ta.value.length;
                    const end = (typeof ta.selectionEnd === 'number') ? ta.selectionEnd : ta.value.length;
                    ta.value = ta.value.substring(0, start) + ins + ta.value.substring(end);
                    ta.focus();
                    ta.selectionStart = ta.selectionEnd = start + ins.length;
                }
            });
        };


        // =======================
        // Botones
        // =======================
        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.logger.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.logger']) || {};
            const tpl = (selTpl.value && pack[selTpl.value]) ? pack[selTpl.value] : def;

            selLevel.value = (tpl.level || 'Info');
            inpMsg.value = (tpl.message || 'Mensaje');
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label;
            node.params = {
                level: (selLevel.value || 'Info'),
                message: (inpMsg.value || '')
            };

            ensurePosition(node);

            const elNode = nodeEl(node.id);
            if (elNode) {
                const t = elNode.querySelector('.node__title');
                if (t) t.textContent = node.label;
            }

            window.WF_Inspector.render({ type: 'node', id: node.id }, ctx, dom);

            setTimeout(() => {
                try { ctx.drawEdges(); } catch (e) { console.warn('drawEdges post-save', e); }
            }, 0);
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

        // =======================
        // Render
        // =======================
        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sLevel);
        body.appendChild(sMsg);
        body.appendChild(rowButtons(bTpl, bSave, bDel));
    });
})();
