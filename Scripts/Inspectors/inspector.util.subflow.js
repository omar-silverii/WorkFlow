; (() => {
    const { register, helpers } = window.WF_Inspector;
    const { el, section, rowButtons, btn } = helpers;

    function slugAlias(s) {
        s = (s || '').toString().trim();
        if (!s) return '';
        // slug simple: letras/numeros/_  (similar a tu regex)
        s = s
            .normalize ? s.normalize('NFD').replace(/[\u0300-\u036f]/g, '') : s; // s/acentos si soporta
        s = s.replace(/[^a-zA-Z0-9_ ]+/g, ' ').trim();
        s = s.replace(/\s+/g, '_');
        // si empieza con numero, prefijo _
        if (/^[0-9]/.test(s)) s = '_' + s;
        return s;
    }

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

    async function crearSubflow(nombre, prefix) {
        const payload = {
            nombre: (nombre || '').toString().trim() || 'Subflow',
            prefix: (prefix || '').toString().trim() || 'WF-'
        };

        const resp = await fetch('/Api/WfDefiniciones.ashx?action=create', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify(payload)
        });

        if (!resp.ok) {
            const txt = await resp.text();
            throw new Error(`HTTP ${resp.status}: ${txt}`);
        }

        const json = await resp.json();
        if (!json || json.ok !== true || !json.key) {
            throw new Error('Respuesta inválida del servidor al crear subflow.');
        }
        return json; // { ok:true, id, key, nombre, version }
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

        // Alias (para múltiples subflows)
        const inpAs = el('input', 'input');
        inpAs.placeholder = 'Ej: validar, notificar';
        inpAs.value = (p.as || '').toString().trim();
        const sAs = section('Alias (opcional) → ${subflows.<alias>.*}', inpAs);

        // Botones: recargar + crear
        const bReload = btn('Recargar lista');
        bReload.onclick = async () => { await cargarDefiniciones(selRef, selRef.value || p.ref || ''); };

        const bCreate = btn('Crear subflujo');
        bCreate.onclick = async () => {
            try {
                // sugerir nombre desde label o desde alias
                const suggestedName = (inpLbl.value || '').toString().trim() || 'Subflow';
                const nombre = prompt('Nombre del subflujo a crear:', suggestedName);
                if (nombre === null) return;

                // prefijo: si querés diferenciar subflows, podrías usar "SUB-"
                // mantenemos WF- para no inventar estructuras nuevas
                const res = await crearSubflow(nombre, 'WF-');

                await cargarDefiniciones(selRef, res.key);
                selRef.value = res.key;

                // sugerir alias si está vacío
                if (!(inpAs.value || '').toString().trim()) {
                    const a = slugAlias(nombre);
                    if (a && /^[a-zA-Z_][a-zA-Z0-9_]*$/.test(a)) inpAs.value = a;
                }

                alert(`Subflujo creado: ${res.key}`);
            } catch (e) {
                console.warn('No se pudo crear subflow', e);
                alert('No se pudo crear el subflujo. Ver consola/log.');
            }
        };

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
Compatibilidad (último subflow ejecutado):
\${subflow.instanceId}
\${subflow.childState}
\${subflow.ref}
\${subflow.logs}
\${subflow.estado}

Múltiples subflows (si definís Alias):
\${subflows.&lt;alias&gt;.instanceId}
\${subflows.&lt;alias&gt;.childState}
\${subflows.&lt;alias&gt;.ref}
\${subflows.&lt;alias&gt;.logs}
\${subflows.&lt;alias&gt;.estado}
          </pre>`;

        const bTpl = btn('Insertar plantilla');
        const bSave = btn('Guardar');
        const bDel = btn('Eliminar nodo');

        bTpl.onclick = () => {
            const pack = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.subflow.templates']) || {};
            const def = (window.PARAM_TEMPLATES && window.PARAM_TEMPLATES['util.subflow']) || {};
            const tpl = selTpl.value && pack[selTpl.value] ? pack[selTpl.value] : def;

            if (tpl.ref) selRef.value = tpl.ref;

            if (tpl.as) inpAs.value = String(tpl.as).trim();
            else inpAs.value = '';

            taInput.value = JSON.stringify((tpl.input || {}), null, 2);
            if (v && v.validate) v.validate();
        };

        bSave.onclick = () => {
            node.label = inpLbl.value || node.label || 'Subflujo';

            if (!selRef.value || !selRef.value.trim()) {
                alert('Debe seleccionar un subflujo (WF_Definicion.Key) o crearlo.');
                return;
            }

            let inputObj = {};
            try { inputObj = taInput.value.trim() ? JSON.parse(taInput.value) : {}; }
            catch { alert('JSON inválido en Input'); return; }

            const alias = (inpAs.value || '').trim();

            if (alias && !/^[a-zA-Z_][a-zA-Z0-9_]*$/.test(alias)) {
                alert('Alias inválido. Usá letras/números/_ y que no empiece con número. Ej: validar, hijo1, subflow_A');
                return;
            }

            const nextParams = Object.assign({}, node.params, {
                ref: selRef.value.trim(),
                input: inputObj
            });

            if (alias) nextParams.as = alias;
            else if (nextParams.as) delete nextParams.as;

            node.params = nextParams;

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

        // fila de botones chicos (crear + recargar)
        body.appendChild(rowButtons(bCreate, bReload));

        body.appendChild(sAs);
        body.appendChild(sInput);
        body.appendChild(outBox);
        body.appendChild(rowButtons(bTpl, bSave, bDel));

        cargarDefiniciones(selRef, p.ref || '');
    });
})();
