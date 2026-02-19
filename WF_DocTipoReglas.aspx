<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_DocTipoReglas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_DocTipoReglas" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <title>WF DocTipo - Reglas de Extracción</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />

    <!-- Bootstrap 5 (local) -->
    <link rel="stylesheet" href="Content/bootstrap.min.css" />

    <style>
        body { background: #f6f7fb; }

        /* look & feel coherente */
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); background:#fff; }
        .ws-card .card-body { padding: 18px; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-chip { display:inline-flex; align-items:center; gap:6px; padding:4px 10px; border-radius:999px; background: rgba(13,110,253,.08); color:#0d6efd; font-size:.78rem; font-weight:600; }

        /* layout propio (SIN pisar Bootstrap) */
        .ws-wrap { display: grid; grid-template-columns: 520px 1fr; gap: 12px; }
        @media (max-width: 1200px) { .ws-wrap { grid-template-columns: 1fr; } }

        .ws-row { display:flex; gap:8px; align-items:center; }
        .ws-row > * { flex: 1; }

        label { font-weight: 600; font-size: 13px; }

        select, input, textarea { width:100%; box-sizing:border-box; padding:8px; border-radius:10px; border:1px solid rgba(148,163,184,.55); }
        textarea { font-family: Consolas, Monaco, monospace; font-size: 12px; }

        .ws-grid { width:100%; border-collapse: collapse; margin-top: 10px; }
        .ws-grid th, .ws-grid td { border-bottom: 1px solid rgba(148,163,184,.25); padding: 8px; font-size: 13px; vertical-align: middle; }
        .ws-grid th { color: rgba(0,0,0,.7); font-weight: 700; }

        pre { white-space: pre-wrap; margin:0; font-family: Consolas, Monaco, monospace; font-size: 12px; }

        .ws-preview { height: 520px; overflow:auto; border: 1px solid rgba(148,163,184,.35); border-radius: 12px; padding: 10px; background:#fff; }

        .pill { display:inline-block; padding:4px 8px; border-radius: 999px; border: 1px solid rgba(148,163,184,.35); font-size: 12px; background:#fff; }
        .ok { color: #065f46; }
        .bad { color: #991b1b; }

        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
    </style>
</head>

<body>
<form id="form1" runat="server">

    <!-- Topbar coherente -->
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">

        <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-3">
            <div>
                <div class="ws-title" style="font-size:1.25rem;">Reglas de extracción</div>
                <div class="ws-muted small">Definición de reglas por DocTipo (generación de regex + prueba sobre preview).</div>
            </div>
            <div class="d-flex gap-2">
                <span class="ws-chip">DocTipo</span>
                <span class="ws-chip">Regex</span>
                <span class="ws-chip">Preview</span>
            </div>
        </div>

        <div class="ws-wrap">

            <!-- IZQUIERDA -->
            <div class="card ws-card">
                <div class="card-body">

                    <div class="ws-row">
                        <div>
                            <label>DocTipo</label>
                            <select id="selDocTipo"></select>
                            <div class="ws-muted small">Se carga desde WF_DocTipo.</div>
                        </div>
                        <div style="max-width:140px;">
                            <label>&nbsp;</label>
                            <button type="button" id="btnReload" class="btn btn-outline-secondary w-100">Recargar</button>
                        </div>
                    </div>

                    <hr class="my-3" style="border-color: rgba(148,163,184,.25);" />

                    <div class="ws-row">
                        <div>
                            <label>Campo</label>
                            <input id="inpCampo" type="text" placeholder="Ej: Total" />
                        </div>

                        <div style="max-width:160px;">
                            <label>TipoDato</label>
                            <select id="selTipoDato">
                                <option value="Texto">Texto</option>
                                <option value="Fecha">Fecha</option>
                                <option value="CUIT">CUIT</option>
                                <option value="Importe">Importe</option>
                                <option value="Numero">Numero</option>
                                <option value="Codigo">Codigo</option>
                                <option value="Email">Email</option>
                            </select>
                        </div>
                    </div>

                    <div class="ws-row mt-2">
                        <div style="max-width:120px;">
                            <label>Orden</label>
                            <input id="inpOrden" type="number" value="10" />
                        </div>
                        <div style="max-width:120px;">
                            <label>Grupo</label>
                            <input id="inpGrupo" type="number" value="1" />
                        </div>
                        <div style="max-width:120px;">
                            <label>Activo</label>
                            <select id="selActivo">
                                <option value="1">Sí</option>
                                <option value="0">No</option>
                            </select>
                        </div>
                    </div>

                    <label class="mt-3 d-block">Ejemplo (seleccioná texto en el preview)</label>
                    <input id="inpEjemplo" type="text" placeholder="Se llena al seleccionar texto" />

                    <label class="mt-2 d-block">HintContext (auto)</label>
                    <textarea id="taHint" rows="4" placeholder="Se llena al seleccionar texto (60 antes + ejemplo + 60 después)"></textarea>

                    <div class="d-flex gap-2 mt-3">
                        <button type="button" id="btnGuardar" class="btn btn-primary w-100">Guardar (genera regex)</button>
                        <button type="button" id="btnProbar" class="btn btn-outline-primary w-100">Probar</button>
                    </div>

                    <div class="mt-2">
                        <span class="pill" id="lblEstado">Listo</span>
                    </div>

                    <table class="ws-grid" id="tblReglas">
                        <thead>
                            <tr>
                                <th style="width:70px;">Orden</th>
                                <th>Campo</th>
                                <th style="width:120px;">Tipo</th>
                                <th style="width:80px;">Activo</th>
                                <th style="width:190px;">Acciones</th>
                            </tr>
                        </thead>
                        <tbody></tbody>
                    </table>

                </div>
            </div>

            <!-- DERECHA -->
            <div class="card ws-card">
                <div class="card-body">

                    <div class="ws-row">
                        <div>
                            <label>Texto de preview</label>
                            <textarea id="taPreviewSrc" rows="6" placeholder="Pegá texto acá, o cargá un .txt abajo..."></textarea>
                        </div>
                    </div>

                    <div class="ws-row mt-2">
                        <div>
                            <input id="fileTxt" type="file" accept=".txt,.pdf,.docx" />
                            <div class="ws-muted small">Soporta .txt, .docx y .pdf (extrae texto, sin OCR).</div>
                        </div>
                        <div style="max-width:160px;">
                            <button type="button" id="btnLoadPreview" class="btn btn-outline-secondary w-100">Cargar preview</button>
                        </div>
                    </div>

                    <label class="mt-3 d-block">Preview (seleccioná texto con el mouse)</label>
                    <div class="ws-preview"><pre id="prePreview"></pre></div>

                    <div class="ws-muted small mt-2">
                        Tip: seleccioná el valor exacto (ej “20-12345678-9”) → se guarda como Ejemplo + contexto.
                    </div>

                </div>
            </div>

        </div>

    </main>

<script>
    (() => {
        const API = '/Api/Generico.ashx';

        const selDocTipo = document.getElementById('selDocTipo');
        const btnReload = document.getElementById('btnReload');

        const inpCampo = document.getElementById('inpCampo');
        
        const selTipoDato = document.getElementById('selTipoDato');
        const inpOrden = document.getElementById('inpOrden');
        const inpGrupo = document.getElementById('inpGrupo');
        const selActivo = document.getElementById('selActivo');
        const inpEjemplo = document.getElementById('inpEjemplo');
        const taHint = document.getElementById('taHint');

        const btnGuardar = document.getElementById('btnGuardar');
        const btnProbar = document.getElementById('btnProbar');
        const lblEstado = document.getElementById('lblEstado');

        const tblBody = document.querySelector('#tblReglas tbody');

        const taPreviewSrc = document.getElementById('taPreviewSrc');
        const fileTxt = document.getElementById('fileTxt');
        const btnLoadPreview = document.getElementById('btnLoadPreview');
        const prePreview = document.getElementById('prePreview');

        let reglas = [];
        let editingId = 0;
        let lastPickedLabel = '';

        function normalizeCampo(s) {
            s = (s || '').trim();
            if (!s) return 'campo';
            s = s.toLowerCase();
            s = s.normalize('NFD').replace(/\p{Diacritic}/gu, '');
            // permitimos letras, números, underscore y punto
            s = s.replace(/[^a-z0-9._]+/g, '_');
            s = s.replace(/_+/g, '_').replace(/^_+|_+$/g, '');
            // evita ".."
            s = s.replace(/\.+/g, '.').replace(/^\.+|\.+$/g, '');
            return s || 'campo';
        }

        function setStatus(msg, ok) {
            lblEstado.textContent = msg;
            lblEstado.className = 'pill ' + (ok ? 'ok' : 'bad');
        }

        async function apiListDocTipos() {
            const r = await fetch(`${API}?action=doctipo.list`, { cache: 'no-store' });
            if (!r.ok) throw new Error('doctipo.list ' + r.status);
            return await r.json();
        }

        async function apiListReglas(codigo) {
            const r = await fetch(`${API}?action=doctipo.reglas.list&codigo=${encodeURIComponent(codigo || '')}`, { cache: 'no-store' });
            if (!r.ok) throw new Error('doctipo.reglas.list ' + r.status);
            return await r.json();
        }

        async function apiSaveRegla(payload) {
            const r = await fetch(`${API}?action=doctipo.reglas.save`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!r.ok) throw new Error('doctipo.reglas.save ' + r.status);
            return await r.json();
        }

        async function apiTestRegex(payload) {
            const r = await fetch(`${API}?action=doctipo.reglas.test`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!r.ok) throw new Error('doctipo.reglas.test ' + r.status);
            return await r.json();
        }

        function escapeHtml(s) {
            return (s || '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
        }

        function escRegex(s) {
            return (s || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        }

        // Genera regex de forma "segura" y automática.
        // - Si detecta label (ej Motivo), captura la 1ra línea no vacía después del label.
        // - Si no hay label, captura EXACTAMENTE lo seleccionado (con límites de línea) para no “derivar”.
        function buildAutoRegex(selectedValue, labelDetected) {
            const val = (selectedValue || '').trim();
            const lab = (labelDetected || '').trim();

            if (!val) return '';

            // Caso "label: valor en líneas siguientes"
            if (lab) {
                // Captura una sola línea (no vacía) después del label
                // Ej: Motivo:\nReposición automática...
                return escRegex(lab) + "\\s*:\\s*(?:\\r?\\n)+\\s*([^\\r\\n]+)";
            }

            // Default: exact match de la selección (para evitar que capture otra cosa)
            const ex = escRegex(val);
            return "(?:^|\\r?\\n)\\s*(" + ex + ")\\s*(?:\\r?\\n|$)";
        }

        function renderReglas() {
            tblBody.innerHTML = '';
            reglas.forEach(r => {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                <td>${r.orden}</td>
                <td>${escapeHtml(r.campo || '')}</td>
                <td>${escapeHtml(r.tipoDato || '')}</td>
                <td>${r.activo ? 'Sí' : 'No'}</td>
                <td>
                  <button type="button" class="btn btn-sm btn-outline-secondary me-1" data-edit="${r.id}">Editar</button>
                  <button type="button" class="btn btn-sm btn-outline-primary" data-test="${r.id}">Probar</button>
                </td>
            `;
                tblBody.appendChild(tr);
            });

            tblBody.querySelectorAll('[data-edit]').forEach(b => {
                b.onclick = () => loadToForm(parseInt(b.getAttribute('data-edit'), 10));
            });
            tblBody.querySelectorAll('[data-test]').forEach(b => {
                b.onclick = () => testRule(parseInt(b.getAttribute('data-test'), 10));
            });
        }

        function loadToForm(id) {
            const r = reglas.find(x => x.id === id);
            if (!r) return;
            editingId = r.id;

            inpCampo.value = r.campo || '';
            selTipoDato.value = r.tipoDato || 'Texto';
            inpOrden.value = r.orden || 10;
            inpGrupo.value = r.grupo || 1;
            selActivo.value = r.activo ? '1' : '0';
            inpEjemplo.value = r.ejemplo || '';
            taHint.value = r.hintContext || '';

            setStatus('Editando regla Id=' + id, true);
        }

        function clearForm() {
            editingId = 0;
            inpCampo.value = '';
            selTipoDato.value = 'Texto';
            inpOrden.value = 10;
            inpGrupo.value = 1;
            selActivo.value = '1';
            inpEjemplo.value = '';
            taHint.value = '';
        }

        function getPreviewText() {
            return prePreview.textContent || '';
        }

        function captureSelectionFromPreview() {
            const sel = window.getSelection();
            if (!sel || sel.rangeCount === 0) return;

            const anchor = sel.anchorNode;
            if (!anchor) return;
            const root = document.getElementById('prePreview');
            if (!root.contains(anchor)) return;

            const selected = (sel.toString() || '').trim();
            if (!selected) return;

            const full = getPreviewText();
            const idx = full.indexOf(selected);
            if (idx < 0) {
                inpEjemplo.value = selected;
                taHint.value = selected;
                return;
            }

            const beforeStart = Math.max(0, idx - 60);
            const afterEnd = Math.min(full.length, idx + selected.length + 60);
            const ctx = full.substring(beforeStart, afterEnd);

            inpEjemplo.value = selected;
            taHint.value = ctx;

            // Detecta label anterior tipo "Motivo:" (una línea que termina en ":")
            lastPickedLabel = '';
            try {
                const upto = full.substring(0, idx);
                const lines = upto.replace(/\r\n/g, "\n").split("\n");

                for (let i = lines.length - 1; i >= 0; i--) {
                    const ln = (lines[i] || '').trim();
                    if (!ln) continue;

                    // Ej: "Motivo:" / "Aprobación requerida:" / "Solicitante:"
                    if (/^[A-Za-zÁÉÍÓÚÑáéíóúñ][^:\n]{0,40}:\s*$/.test(ln)) {
                        lastPickedLabel = ln.replace(/:\s*$/, '');
                        break;
                    }

                    // si encontramos una línea “normal” y ya estamos lejos, cortamos
                    if (ln.length > 0 && ln.indexOf(':') === -1 && (lines.length - i) > 10) break;
                }
            } catch { /* no rompe */ }


            setStatus('Ejemplo capturado ✅', true);
        }

        document.getElementById('prePreview').addEventListener('mouseup', () => {
            setTimeout(captureSelectionFromPreview, 0);
        });

        async function apiLoadPreviewFile(file) {
            const fd = new FormData();
            fd.append('file', file);

            const r = await fetch('/Api/DocPreview.ashx', {
                method: 'POST',
                body: fd
            });

            if (!r.ok) throw new Error('DocPreview ' + r.status);
            return await r.json();
        }

        btnLoadPreview.onclick = async () => {
            try {
                // 1) Si hay archivo, lo mandamos al server para extraer texto real
                if (fileTxt.files && fileTxt.files.length > 0) {
                    const f = fileTxt.files[0];
                    const resp = await apiLoadPreviewFile(f);
                    if (!resp.ok) { setStatus(resp.error || 'Error preview', false); return; }

                    prePreview.textContent = resp.text || '';
                    taPreviewSrc.value = resp.text || '';
                    setStatus('Preview cargado ✅ (' + (resp.modeUsed || 'auto') + ')', true);
                    return;
                }

                // 2) Si no hay archivo, usamos texto pegado
                prePreview.textContent = taPreviewSrc.value || '';
                setStatus('Preview cargado desde texto ✅', true);

            } catch (e) {
                console.warn(e);
                setStatus('Error cargando preview: ' + e.message, false);
            }
        };


        btnGuardar.onclick = async () => {
            const codigo = (selDocTipo.value || '').trim();
            if (!codigo) { setStatus('Elegí un DocTipo', false); return; }

            const campoRaw = (inpCampo.value || '').trim();
            if (!campoRaw) { setStatus('Falta Campo', false); return; }

            const campoFinal = normalizeCampo(campoRaw);
            const ejemplo = (inpEjemplo.value || '').trim();
            const labelDetected = lastPickedLabel || '';
            const autoRegex = buildAutoRegex(ejemplo, labelDetected);

            const payload = {
                id: editingId,
                docTipoCodigo: codigo,
                campo: campoFinal,            // <- relativo SIEMPRE
                tipoDato: selTipoDato.value,
                orden: parseInt(inpOrden.value || '0', 10) || 0,
                grupo: parseInt(inpGrupo.value || '1', 10) || 1,
                activo: selActivo.value === '1',
                ejemplo: (inpEjemplo.value || '').trim(),
                hintContext: (taHint.value || '').trim(),
                modo: 'LabelValue',
                 // ✅ NUEVO: la página manda regex final (usuario NO ve regex)
                regex: autoRegex,

                // ✅ NUEVO: útil para debug/auditoría (opcional)
                labelDetected: labelDetected
            };

            try {
                const resp = await apiSaveRegla(payload);
                if (!resp.ok) { setStatus(resp.error || 'Error guardando', false); return; }

                setStatus('Guardado ✅ (regex generado)', true);
                await reloadReglas();
                clearForm();
            } catch (e) {
                console.warn(e);
                setStatus('Error guardando: ' + e.message, false);
            }
        };

        async function testRule(id) {
            const r = reglas.find(x => x.id === id);
            if (!r) return;

            const text = getPreviewText();
            if (!text.trim()) { setStatus('Cargá preview primero', false); return; }

            try {
                const resp = await apiTestRegex({
                    regex: r.regex,
                    grupo: r.grupo || 1,
                    text: text
                });
                if (!resp.ok) { setStatus(resp.error || 'Error', false); return; }
                if (!resp.success) { setStatus('No match ❌', false); return; }

                setStatus('Match ✅ Valor: ' + (resp.value || ''), true);
            } catch (e) {
                console.warn(e);
                setStatus('Error probando: ' + e.message, false);
            }
        }

        btnProbar.onclick = async () => {
            if (editingId > 0) return await testRule(editingId);
            if (reglas.length === 0) { setStatus('No hay reglas para probar', false); return; }
            return await testRule(reglas[0].id);
        };

        async function reloadReglas() {
            const codigo = (selDocTipo.value || '').trim();
            if (!codigo) { reglas = []; renderReglas(); return; }

            const resp = await apiListReglas(codigo);
            if (!resp.ok) { setStatus(resp.error || 'Error listando reglas', false); return; }

            reglas = (resp.reglas || []);
            renderReglas();
        }

        btnReload.onclick = async () => {
            await reloadReglas();
            clearForm();
            setStatus('Recargado ✅', true);
        };

        selDocTipo.addEventListener('change', async () => {
            await reloadReglas();
            clearForm();
        });

        (async function init() {
            try {
                const items = await apiListDocTipos();
                selDocTipo.innerHTML = '';
                const o0 = document.createElement('option');
                o0.value = '';
                o0.textContent = '(seleccioná DocTipo)';
                selDocTipo.appendChild(o0);

                (items || []).forEach(it => {
                    const o = document.createElement('option');
                    o.value = it.codigo || '';
                    o.textContent = (it.codigo || '') + (it.nombre ? ' — ' + it.nombre : '');
                    selDocTipo.appendChild(o);
                });

                setStatus('Listo ✅', true);
            } catch (e) {
                console.warn(e);
                setStatus('Error init: ' + e.message, false);
            }
        })();

    })();
</script>

    <!-- Bootstrap 5 (local) -->
    <script src="Scripts/bootstrap.bundle.min.js"></script>

</form>
</body>
</html>
