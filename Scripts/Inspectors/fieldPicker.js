; (() => {
    function el(tag, cls) {
        const x = document.createElement(tag);
        if (cls) x.className = cls;
        return x;
    }

    function ensureStyles() {
        if (document.getElementById('wfFieldPickerStyles')) return;
        const st = document.createElement('style');
        st.id = 'wfFieldPickerStyles';
        st.textContent = `
      .wfFP_overlay{position:fixed;inset:0;background:rgba(0,0,0,.35);z-index:9999;display:flex;align-items:center;justify-content:center}
      .wfFP_modal{width:860px;max-width:96vw;max-height:82vh;background:#fff;border:1px solid #d0d7de;border-radius:10px;box-shadow:0 10px 25px rgba(0,0,0,.15);display:flex;flex-direction:column;overflow:hidden}
      .wfFP_head{padding:12px 14px;border-bottom:1px solid #e5e7eb;display:flex;justify-content:space-between;align-items:center}
      .wfFP_title{font-weight:600}
      .wfFP_body{display:flex;gap:10px;padding:12px 14px;overflow:hidden;flex:1;min-height:380px}
      .wfFP_left{width:240px;border-right:1px solid #f1f5f9;padding-right:10px;overflow:auto}
      .wfFP_right{flex:1;overflow:hidden;display:flex;flex-direction:column}
      .wfFP_search{margin-bottom:8px}
      .wfFP_hint{font-size:12px;color:#6b7280;margin-bottom:8px}
      .wfFP_ns{padding:8px;border-radius:8px;cursor:pointer;margin-bottom:6px;border:1px solid #e5e7eb;background:#fff}
      .wfFP_nsActive{background:#eef2ff}
      .wfFP_list{flex:1;overflow:auto;border:1px solid #e5e7eb;border-radius:8px;padding:6px}
      .wfFP_item{padding:8px;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;cursor:pointer}
      .wfFP_itemActive{border-color:#93c5fd;background:#eff6ff}
      .wfFP_path{font-family:monospace;font-size:12px}
      .wfFP_meta{font-size:12px;color:#6b7280;margin-top:2px}
      .wfFP_footer{padding:10px 14px;border-top:1px solid #e5e7eb;display:flex;justify-content:space-between;align-items:center;gap:10px}
      .wfFP_chosen{font-family:monospace;font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:62%}
    `;
        document.head.appendChild(st);
    }

    function fetchRegistry(ctx) {
        const defId = (ctx && ctx.definitionId) ? ctx.definitionId : 0;
        return fetch('/Api/WorkflowFieldRegistry.ashx?definitionId=' + encodeURIComponent(defId))
            .then(r => r.json());
    }

    function normalizeRegistry(data) {
        // Esperamos: { namespaces:[ { name:"biz.oc", fields:[{path:"biz.oc.numero", label?, docTipo?}] } ] }
        const ns = (data && Array.isArray(data.namespaces)) ? data.namespaces : [];
        return ns.map(x => ({
            name: String(x.name || '').trim(),
            fields: (x.fields || []).map(f => ({
                path: String(f.path || '').trim(),
                label: f.label ? String(f.label) : '',
                docTipo: f.docTipo ? String(f.docTipo) : ''
            })).filter(f => f.path)
        })).filter(n => n.name);
    }

    function norm(s) { return (s || '').toString().toLowerCase().trim(); }

    function open(options) {
        ensureStyles();

        const ctx = options && options.ctx;
        const title = (options && options.title) || 'Elegir campo';
        const onPick = (options && options.onPick) || function () { };

        // --- overlay (único)
        const prev = document.getElementById('wfFP_overlay');
        if (prev) prev.remove();

        const overlay = el('div', 'wfFP_overlay');
        overlay.id = 'wfFP_overlay';

        const modal = el('div', 'wfFP_modal');

        const head = el('div', 'wfFP_head');
        const hTitle = el('div', 'wfFP_title');
        hTitle.textContent = title;

        const bClose = el('button', 'btn');
        bClose.type = 'button';
        bClose.textContent = 'Cerrar';
        bClose.onclick = () => overlay.remove();

        head.appendChild(hTitle);
        head.appendChild(bClose);

        const body = el('div', 'wfFP_body');
        const left = el('div', 'wfFP_left');
        const right = el('div', 'wfFP_right');

        const search = el('input', 'input');
        search.classList.add('wfFP_search');
        search.placeholder = 'Buscar… (ej: empresa, proveedor, total, fecha, biz.oc, etc.)';

        const hint = el('div', 'wfFP_hint');
        hint.textContent = 'Enter=Usar · Esc=Cerrar · ↑↓=Mover · Doble click=Usar';

        const list = el('div', 'wfFP_list');

        const footer = el('div', 'wfFP_footer');
        const chosen = el('div', 'wfFP_chosen');
        chosen.textContent = '';

        const bUse = el('button', 'btn');
        bUse.type = 'button';
        bUse.textContent = 'Usar';

        footer.appendChild(chosen);
        footer.appendChild(bUse);

        right.appendChild(search);
        right.appendChild(hint);
        right.appendChild(list);

        body.appendChild(left);
        body.appendChild(right);

        modal.appendChild(head);
        modal.appendChild(body);
        modal.appendChild(footer);

        overlay.appendChild(modal);
        document.body.appendChild(overlay);

        overlay.addEventListener('click', (ev) => { if (ev.target === overlay) overlay.remove(); });

        // --- estado
        let namespaces = [];
        let selectedNs = '';
        let currentItems = []; // items filtrados visibles
        let currentIndex = -1;

        function setChosen(v) { chosen.textContent = v || ''; }

        function applySelected() {
            const v = (chosen.textContent || '').trim();
            if (!v) return;
            onPick(v);
            overlay.remove();
        }

        bUse.onclick = applySelected;

        function renderNamespaces() {
            left.innerHTML = '';
            if (!namespaces.length) {
                const empty = el('div');
                empty.style.color = '#6b7280';
                empty.style.fontSize = '13px';
                empty.textContent = 'Sin namespaces (registry vacío).';
                left.appendChild(empty);
                return;
            }

            // item especial "Todos"
            const allItem = el('div', 'wfFP_ns' + (selectedNs === '__ALL__' ? ' wfFP_nsActive' : ''));
            allItem.textContent = 'Todos';
            allItem.onclick = () => { selectedNs = '__ALL__'; renderNamespaces(); renderList(); search.focus(); };
            left.appendChild(allItem);

            namespaces.forEach(n => {
                const item = el('div', 'wfFP_ns' + (n.name === selectedNs ? ' wfFP_nsActive' : ''));
                item.textContent = n.name;
                item.onclick = () => { selectedNs = n.name; renderNamespaces(); renderList(); search.focus(); };
                left.appendChild(item);
            });
        }

        function flattenAll() {
            const all = [];
            namespaces.forEach(ns => {
                (ns.fields || []).forEach(f => {
                    all.push({
                        ns: ns.name,
                        path: f.path,
                        label: f.label || '',
                        docTipo: f.docTipo || ''
                    });
                });
            });
            return all;
        }

        function getItemsBase() {
            if (selectedNs === '__ALL__') return flattenAll();
            const ns = namespaces.find(x => x.name === selectedNs);
            return ns ? (ns.fields || []).map(f => ({
                ns: ns.name,
                path: f.path,
                label: f.label || '',
                docTipo: f.docTipo || ''
            })) : [];
        }

        function renderList() {
            list.innerHTML = '';

            const q = norm(search.value);
            const base = getItemsBase();

            currentItems = base.filter(it => {
                if (!q) return true;
                return (
                    norm(it.path).includes(q) ||
                    norm(it.label).includes(q) ||
                    norm(it.docTipo).includes(q) ||
                    norm(it.ns).includes(q)
                );
            });

            if (!currentItems.length) {
                const msg = el('div');
                msg.textContent = 'No hay resultados.';
                msg.style.padding = '10px';
                msg.style.color = '#6b7280';
                msg.style.fontSize = '13px';
                list.appendChild(msg);
                currentIndex = -1;
                setChosen('');
                return;
            }

            currentIndex = 0;
            setChosen(currentItems[0].path);

            currentItems.forEach((it, idx) => {
                const row = el('div', 'wfFP_item' + (idx === currentIndex ? ' wfFP_itemActive' : ''));

                const line1 = el('div', 'wfFP_path');
                line1.textContent = it.path;

                const meta = [];
                if (it.ns && selectedNs === '__ALL__') meta.push(it.ns);
                if (it.label) meta.push(it.label);
                if (it.docTipo) meta.push(it.docTipo);

                if (meta.length) {
                    const line2 = el('div', 'wfFP_meta');
                    line2.textContent = meta.join(' · ');
                    row.appendChild(line2);
                }

                row.insertBefore(line1, row.firstChild);

                row.onmouseenter = () => { currentIndex = idx; setChosen(it.path); paintActive(); };
                row.onclick = () => { currentIndex = idx; setChosen(it.path); paintActive(); };
                row.ondblclick = () => { setChosen(it.path); applySelected(); };

                list.appendChild(row);
            });
        }

        function paintActive() {
            const rows = list.querySelectorAll('.wfFP_item');
            rows.forEach((r, i) => {
                if (i === currentIndex) r.classList.add('wfFP_itemActive');
                else r.classList.remove('wfFP_itemActive');
            });

            // scroll into view
            const rows2 = list.querySelectorAll('.wfFP_item');
            const active = rows2[currentIndex];
            if (active) active.scrollIntoView({ block: 'nearest' });
        }

        function moveSelection(delta) {
            if (!currentItems.length) return;
            currentIndex += delta;
            if (currentIndex < 0) currentIndex = 0;
            if (currentIndex >= currentItems.length) currentIndex = currentItems.length - 1;
            setChosen(currentItems[currentIndex].path);
            paintActive();
        }

        function onKeyDown(ev) {
            if (!document.body.contains(overlay)) {
                document.removeEventListener('keydown', onKeyDown, true);
                return;
            }
            if (ev.key === 'Escape') { ev.preventDefault(); overlay.remove(); return; }
            if (ev.key === 'Enter') { ev.preventDefault(); applySelected(); return; }
            if (ev.key === 'ArrowDown') { ev.preventDefault(); moveSelection(+1); return; }
            if (ev.key === 'ArrowUp') { ev.preventDefault(); moveSelection(-1); return; }
        }

        document.addEventListener('keydown', onKeyDown, true);

        search.oninput = () => renderList();

        // --- cargar
        fetchRegistry(ctx).then(raw => {
            namespaces = normalizeRegistry(raw);

            // default: arrancamos en biz si existe, sino en primero, y siempre habilitamos "Todos"
            selectedNs = namespaces.find(n => n.name === 'biz') ? 'biz' : (namespaces[0] ? namespaces[0].name : '__ALL__');
            if (!selectedNs) selectedNs = '__ALL__';

            renderNamespaces();
            renderList();
            search.focus();
        }).catch(err => {
            console.warn('WF_FieldPicker error', err);
            list.innerHTML = '';
            const msg = el('div');
            msg.textContent = 'No se pudo cargar el registro de campos.';
            msg.style.padding = '10px';
            msg.style.color = '#6b7280';
            msg.style.fontSize = '13px';
            list.appendChild(msg);
        });
    }

    window.WF_FieldPicker = { open };
})();
