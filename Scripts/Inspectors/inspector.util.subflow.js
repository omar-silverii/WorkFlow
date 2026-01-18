; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    async function cargarDefiniciones(sel, currentKey) {
        sel.innerHTML = '';
        const opt0 = document.createElement('option');
        opt0.value = '';
        opt0.textContent = '— Elegir subflujo —';
        sel.appendChild(opt0);

        let list = [];
        try {
            const resp = await fetch('/Api/WfDefiniciones.ashx?activo=1', { credentials: 'same-origin' });
            list = await resp.json();
        } catch (e) {
            console.warn('No se pudo cargar definiciones', e);
        }

        list.forEach(d => {
            const o = document.createElement('option');
            o.value = d.key;
            o.textContent = `${d.key} — ${d.nombre} (v${d.version})`;
            if (currentKey && String(currentKey).toLowerCase() === String(d.key).toLowerCase()) o.selected = true;
            sel.appendChild(o);
        });
    }

    register('util.subflow', (node, ctx, dom) => {
        const { ensurePosition, nodeEl } = ctx;
        const { body, title, sub } = dom;
        body.innerHTML = '';
        if (title) title.textContent = node.label || 'Subflujo';
        if (sub) sub.textContent = node.key || '';

        const p = node.params || {};

        const inpLbl = el('input', 'input'); inpLbl.value = node.label || '';
        const sLbl = section('Etiqueta (label)', inpLbl);

        // Plantillas
        const selTpl = el('select', 'input');
        (function () {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.subflow.templates']) || {};
            const opt0 = document.createElement('option'); opt0.value = ''; opt0.textContent = '— Elegir —'; selTpl.appendChild(opt0);
            Object.keys(pack).forEach(k => {
                const o = document.createElement('option');
                o.value = k;
                o.textContent = (pack[k].label || k);
                selTpl.appendChild(o);
            });
        })();
        const sTpl = section('Plantilla', selTpl);

        // Selector de Key
        const selRef = el('select', 'input');
        const sRef = section('Subflujo (WF_Definicion.Key)', selRef);

        const bReload = btn('Recargar lista');
        bReload.onclick = async () => { await cargarDefiniciones(selRef, selRef.value || p.ref || ''); };

        // Input JSON
        const taInput = el('textarea', 'textarea');
        taInput.value = JSON.stringify((p.input || {}), null, 2);
        const sInput = section('Input (JSON) → DatosEntrada del subflow', taInput);

        const v = (window.JsonValidator && window.JsonValidator.attach)
            ? window.JsonValidator.attach(taInput)
            : null;

        // “Documentación viva” de outputs
        const outBox = el('div', 'section');
        outBox.innerHTML = `
      <div class="label">Outputs (para usar en \${...})</div>
      <pre style="background:#0f172a;color:#e5e7eb;padding:8px;border-radius:4px;font-size:12px;white-space:pre-wrap">
\${subflow.instanceId}  → Id de la instancia hija
\${subflow.childState}  → Finalizado | EnCurso | Error
\${subflow.ref}         → Key ejecutado
\${subflow.logs}        → array de logs del subflow
\${subflow.estado}      → snapshot del estado (objeto)
      </pre>`;

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.subflow.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.subflow']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;

            // Si la plantilla trae ref, lo seteamos
            if (tpl.ref) selRef.value = tpl.ref;

            taInput.value = JSON.stringify((tpl.input || {}), null, 2);
            if (v && v.validate) v.validate();
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Subflujo';

            if (!selRef.value || !selRef.value.trim()) {
                alert('Debe seleccionar un subflujo (WF_Definicion.Key).');
                return;
            }

            let inputObj = {};
            try { inputObj = taInput.value.trim() ? JSON.parse(taInput.value) : {}; }
            catch { alert('JSON inválido en Input'); return; }

            node.params = Object.assign({}, node.params, {
                ref: selRef.value.trim(),
                input: inputObj
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
                    if (e && (e.from === node.id || e.to === node.id)) ctx.edges.splice(i, 1);
                }
            }
            if (Array.isArray(ctx.nodes)) {
                for (let i = ctx.nodes.length - 1; i >= 0; i--) {
                    const n = ctx.nodes[i];
                    if (n && n.id === node.id) ctx.nodes.splice(i, 1);
                }
            }
            const elN = ctx.nodeEl(node.id);
            if (elN) elN.remove();
            ctx.drawEdges();
            ctx.select(null);
        };

        body.appendChild(sLbl);
        body.appendChild(sTpl);
        body.appendChild(sRef);
        body.appendChild(bReload);
        body.appendChild(sInput);
        body.appendChild(outBox);
        body.appendChild(rowButtons(bTpl, bSave, bDel));

        // cargar lista al abrir
        cargarDefiniciones(selRef, p.ref || '');
    });
})();
