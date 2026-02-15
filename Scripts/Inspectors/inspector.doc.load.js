(() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, btn } = helpers;

    register('doc.load', (node, ctx, dom) => {

        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;

        body.innerHTML = '';

        // TÍTULO Y SUBTÍTULO
        if (title) title.textContent = node.label || 'Cargar documento';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        // 1) Etiqueta
        const inpLbl = el('input', 'input');
        inpLbl.value = node.label || 'Cargar documento';
        const sLbl = section('Etiqueta', inpLbl);

        // 2) Ruta
        const inpPath = el('input', 'input');
        inpPath.value = p.path || '';
        const sPath = section('Ruta del archivo', inpPath);

        // 3) Modo
        const selModo = el('select', 'input');
        ['auto', 'pdf', 'word', 'text', 'image'].forEach(m => {
            const o = document.createElement('option');
            o.value = m;
            o.textContent = m;
            if ((p.mode || 'auto') === m) o.selected = true;
            selModo.appendChild(o);
        });
        const sModo = section('Modo', selModo);

        // 4) outputPrefix
        const inpOut = el('input', 'input');
        inpOut.placeholder = 'input';
        inpOut.value = p.outputPrefix || 'input';
        const sOut = section('Salida (outputPrefix)', inpOut);

        // 5) Info
        const info = document.createElement('div');
        info.style.fontSize = "12px";
        info.style.opacity = "0.75";
        info.style.marginTop = "4px";
        info.innerHTML = `
            <b>Salida:</b><br>
            {prefix}.filename<br>
            {prefix}.ext<br>
            {prefix}.text<br>
            {prefix}.sizeBytes<br>
            <span class="text-muted">(mode=image: sin OCR, text vacío)</span>
        `;
        const sInfo = section('Información', info);

        body.appendChild(sLbl);
        body.appendChild(sPath);
        body.appendChild(sModo);
        body.appendChild(sOut);
        body.appendChild(sInfo);

        // Botones
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bSave.onclick = () => {
            node.label = inpLbl.value || 'Cargar documento';

            node.params = {
                path: inpPath.value || '',
                mode: selModo.value || 'auto',
                outputPrefix: (inpOut.value || 'input').trim() || 'input'
            };

            ensurePosition(node);

            const nd = nodeEl(node.id);
            if (nd) {
                const t = nd.querySelector('.node__title');
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
                    if (e.from === node.id || e.to === node.id) ctx.edges.splice(i, 1);
                }
            }

            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    if (ctx.nodes[i].id === node.id) { ctx.nodes.splice(i, 1); break; }
                }
            }

            body.innerHTML = '';
            if (title) title.textContent = 'Inspector';
            if (sub) sub.textContent = '';

            try { ctx.renderCanvas(); } catch (e) { console.warn('renderCanvas after delete', e); }
        };

        const footer = document.createElement('div');
        footer.style.display = 'flex';
        footer.style.gap = '8px';
        footer.style.marginTop = '10px';
        footer.appendChild(bSave);
        footer.appendChild(bDel);

        body.appendChild(footer);
    });
})();
