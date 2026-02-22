<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_DocTipoReglas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_DocTipoReglas" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>
<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <title>WF DocTipo - Reglas de Extracción</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />

    <link rel="stylesheet" href="Content/bootstrap.min.css" />

    <style>
        body { background: #f6f7fb; }

        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); background:#fff; }
        .ws-card .card-body { padding: 18px; }

        .ws-title { font-weight: 700; letter-spacing: .2px; margin-bottom: 2px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-chip { display:inline-flex; align-items:center; gap:6px; padding:4px 10px; border-radius:999px; background: rgba(13,110,253,.08); color:#0d6efd; font-size:.78rem; font-weight:600; }

        .ws-wrap { display: grid; grid-template-columns: 520px 1fr; gap: 12px; }
        @media (max-width: 1200px) { .ws-wrap { grid-template-columns: 1fr; } }

        label { font-weight: 600; font-size: 13px; }

        /* ⚠️ IMPORTANTE: NO PISAR CHECKBOX/RADIO/FILE */
        select,
        textarea,
        input:not([type="checkbox"]):not([type="radio"]):not([type="file"]):not([type="button"]):not([type="submit"]) {
            width: 100%;
            box-sizing: border-box;
            padding: 6px 8px;
            border-radius: 8px;
            border: 1px solid rgba(148,163,184,.55);
            font-size: 13px;
            line-height: 1.2;
            background: #fff;
        }

        input:not([type="checkbox"]):not([type="radio"]):not([type="file"]):not([type="button"]):not([type="submit"]),
        select {
            height: 34px;
        }

        textarea {
            font-family: Consolas, Monaco, monospace;
            font-size: 12px;
            min-height: 70px;
        }

        /* file input: dejarlo Bootstrap (no tocar height/padding global) */
        input[type="file"] { width: 100%; }

        .ws-grid { width:100%; border-collapse: collapse; margin-top: 10px; }
        .ws-grid th, .ws-grid td { border-bottom: 1px solid rgba(148,163,184,.25); padding: 6px; font-size: 13px; vertical-align: middle; }
        .ws-grid th { color: rgba(0,0,0,.7); font-weight: 700; }

        pre { white-space: pre-wrap; margin:0; font-family: Consolas, Monaco, monospace; font-size: 12px; }
        .ws-preview { height: 520px; overflow:auto; border: 1px solid rgba(148,163,184,.35); border-radius: 12px; padding: 10px; background:#fff; }

        .pill { display:inline-block; padding:4px 8px; border-radius: 999px; border: 1px solid rgba(148,163,184,.35); font-size: 12px; background:#fff; }
        .ok { color: #065f46; }
        .bad { color: #991b1b; }

        .ws-actions { display: flex; gap: 6px; justify-content: flex-start; align-items: center; }
        .ws-iconbtn { width: 30px; height: 30px; padding: 0; display: inline-flex; align-items: center; justify-content: center; border-radius: 10px; }
        .ws-iconbtn svg { width: 16px; height: 16px; }

        .ws-help { font-size: 12px; color: rgba(0,0,0,.55); min-height: 14px; line-height: 14px; margin-top: 2px; }

        /* filas “pro” */
        .ws-row-2col { display: grid; grid-template-columns: 1fr 140px; gap: 10px; align-items: end; }
        .ws-row-3col { display: grid; grid-template-columns: 110px 140px 110px; gap: 10px; align-items: end; }

        .ws-rightbtn { width: 140px; }
        .ws-label-spacer { display:block; height: 17px; } /* alinea con label */
        .ws-btn-34 { height: 34px; padding-top: 0; padding-bottom: 0; display: inline-flex; align-items: center; justify-content: center; }
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
                <div class="ws-muted small">Definición de reglas por DocTipo (modo asistido por contexto + prueba sobre preview).</div>
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

                    <div class="mb-3">
                        <!-- El label arriba de todo -->
                        <label for="selDocTipo" class="form-label">DocTipo</label>
    
                        <!-- Contenedor Flex para alinear select y botón -->
                        <div class="d-flex gap-2"> 
                            <select id="selDocTipo" class="form-select"></select>
                            <button type="button" id="btnReload" class="btn btn-outline-secondary text-nowrap">
                                Recargar
                            </button>
                        </div>

                        <!-- La leyenda debajo de los dos elementos -->
                        <div class="form-text">Se carga desde WF_DocTipo.</div>
                    </div>

                    <div style="height:8px;"></div>

                    <div class="d-flex gap-2 align-items-start">
                        <div style="flex:1;">
                            <label>Campo</label>
                            <input id="inpCampo" type="text" placeholder="Ej: empresa / numero / fecha / solicitante" />

                            <div class="form-check mt-2">
                                <input class="form-check-input" type="checkbox" id="chkItemBlock" />
                                <label class="form-check-label" for="chkItemBlock">
                                    Regla ItemBlock (define el bloque repetible de ítems)
                                </label>
                                <div class="ws-help">Se guarda como <code>items[].__block</code>. Esta regla permite iterar y armar <code>items[]</code>.</div>
                            </div>

                            <div class="form-check mt-2">
                                <input class="form-check-input" type="checkbox" id="chkItem" />
                                <label class="form-check-label" for="chkItem">
                                    Es campo de Item (repetible) → guarda como <code>items[].campo</code>
                                </label>
                            </div>

                            <div id="lblCampoFinal" class="ws-help"></div>
                        </div>

                        <div style="width:160px;">
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

                    <div class="ws-help">Para ItemBlock se usa 0 (match completo).</div>
                    <div class="ws-row-3col mt-2">
     
                        <div>
                            <label>Orden</label>
                            <input id="inpOrden" type="number" value="10" />
         
                        </div>

                        <div>
                            <label>Grupo</label>
                            <input id="inpGrupo" type="number" value="1" />
         
                        </div>

                        <div>
                            <label>Activo</label>
                            <select id="selActivo">
                                <option value="1">Sí</option>
                                <option value="0">No</option>
                            </select>
         
                        </div>
                    </div>

                    <label class="mt-3 d-block">Ejemplo (seleccioná el VALOR en el preview)</label>
                    <input id="inpEjemplo" type="text" placeholder="Se llena al seleccionar texto (valor)" />

                    <label class="mt-2 d-block">HintContext (auto)</label>
                    <textarea id="taHint" rows="4" placeholder="Se llena al seleccionar texto (contexto alrededor)"></textarea>

                    <div class="d-flex gap-2 mt-3">
                        <button type="button" id="btnGuardar" class="btn btn-primary w-100">Guardar</button>
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
                                <th style="width:120px;">Acciones</th>
                            </tr>
                        </thead>
                        <tbody></tbody>
                    </table>

                </div>
            </div>

            <!-- DERECHA -->
            <div class="card ws-card">
                <div class="card-body">

                    <div>
                        <label>Texto de preview</label>
                        <textarea id="taPreviewSrc" rows="6" placeholder="Pegá texto acá, o cargá un .txt/.pdf/.docx abajo..."></textarea>
                    </div>

                    <div class="ws-row-2col mt-2">
                        <div>
                            <label class="ws-label-spacer">&nbsp;</label>
                            <input id="fileTxt" type="file" accept=".txt,.pdf,.docx" />
                            <div class="ws-muted small">Soporta .txt, .docx y .pdf (extrae texto, sin OCR).</div>
                        </div>

                        <div class="ws-rightbtn">
                            <label class="ws-label-spacer">&nbsp;</label>
                            <button type="button" id="btnLoadPreview" class="btn btn-outline-secondary w-100 ws-btn-34">Cargar preview</button>
                        </div>
                    </div>

                    <label class="mt-3 d-block">Preview (seleccioná texto con el mouse)</label>
                    <div class="ws-preview"><pre id="prePreview"></pre></div>

                    <div class="ws-muted small mt-2">
                        Tip: seleccioná el <b>valor</b> (ej “ACME S.A.”, “NPC-2025-003812”, “120”) → el sistema genera regex por contexto.
                    </div>

                </div>
            </div>

        </div>


    </main>

    <script>
        (() => {
            // ==== TU JS (igual al que pegaste) ====
            const API = '/Api/Generico.ashx';

            const selDocTipo = document.getElementById('selDocTipo');
            const btnReload = document.getElementById('btnReload');

            const inpCampo = document.getElementById('inpCampo');
            const chkItem = document.getElementById('chkItem');
            const chkItemBlock = document.getElementById('chkItemBlock');

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

            const lblCampoFinal = document.getElementById('lblCampoFinal');

            let reglas = [];
            let editingId = 0;

            function normalizeCampo(s) {
                s = (s || '').trim();
                if (!s) return 'campo';
                s = s.toLowerCase();
                s = s.normalize('NFD').replace(/\p{Diacritic}/gu, '');
                s = s.replace(/[^a-z0-9._]+/g, '_');
                s = s.replace(/_+/g, '_').replace(/^_+|_+$/g, '');
                s = s.replace(/\.+/g, '.').replace(/^\.+|\.+$/g, '');
                return s || 'campo';
            }

            function setStatus(msg, ok) {
                lblEstado.textContent = msg;
                lblEstado.className = 'pill ' + (ok ? 'ok' : 'bad');
            }

            function escapeHtml(s) {
                return (s || '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
            }

            function updateCampoFinalPreview() {
                if (!lblCampoFinal) return;

                const raw = (inpCampo.value || '').trim();
                let base = normalizeCampo(raw);

                if (chkItemBlock && chkItemBlock.checked) {
                    lblCampoFinal.innerHTML = 'Se guardará como: <code>items[].__block</code>';
                    return;
                }

                if (!base) { lblCampoFinal.textContent = ''; return; }

                let finalValue = base;
                if (chkItem && chkItem.checked) finalValue = 'items[].' + base;

                lblCampoFinal.innerHTML = 'Se guardará como: <code>' + finalValue + '</code>';
            }

            function applyItemBlockRulesUI() {
                if (!chkItemBlock) return;

                if (chkItemBlock.checked) {
                    if (chkItem) chkItem.checked = false;
                    if (chkItem) chkItem.disabled = true;
                    inpGrupo.value = 0;
                } else {
                    if (chkItem) chkItem.disabled = false;
                    if (parseInt(inpGrupo.value || '0', 10) === 0) inpGrupo.value = 1;
                }
                updateCampoFinalPreview();
            }

            inpCampo.addEventListener('input', updateCampoFinalPreview);
            if (chkItem) chkItem.addEventListener('change', updateCampoFinalPreview);
            if (chkItemBlock) chkItemBlock.addEventListener('change', applyItemBlockRulesUI);

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
            async function apiDeleteRegla(docTipoCodigo, id) {
                const r = await fetch(`${API}?action=doctipo.reglas.delete`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ docTipoCodigo: docTipoCodigo, id: id })
                });
                if (!r.ok) throw new Error('doctipo.reglas.delete ' + r.status);
                return await r.json();
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
                  <div class="ws-actions">
                    <button type="button" class="btn btn-sm btn-outline-secondary ws-iconbtn" data-edit="${r.id}" title="Editar">✎</button>
                    <button type="button" class="btn btn-sm btn-outline-primary ws-iconbtn" data-test="${r.id}" title="Probar">▶</button>
                    <button type="button" class="btn btn-sm btn-outline-danger ws-iconbtn" data-del="${r.id}" title="Eliminar">🗑</button>
                  </div>
                </td>`;
                    tblBody.appendChild(tr);
                });

                tblBody.querySelectorAll('[data-edit]').forEach(b => b.onclick = () => loadToForm(parseInt(b.getAttribute('data-edit'), 10)));
                tblBody.querySelectorAll('[data-test]').forEach(b => b.onclick = () => testRule(parseInt(b.getAttribute('data-test'), 10)));
                tblBody.querySelectorAll('[data-del]').forEach(b => b.onclick = () => deleteRule(parseInt(b.getAttribute('data-del'), 10)));
            }

            function loadToForm(id) {
                const r = reglas.find(x => x.id === id);
                if (!r) return;
                editingId = r.id;

                const c = (r.campo || '').trim();
                const isItemBlock = c.toLowerCase() === 'items[].__block';
                const isItem = /^items\[\]\./i.test(c) && !isItemBlock;

                if (chkItemBlock) chkItemBlock.checked = isItemBlock;
                if (chkItem) chkItem.checked = isItem;

                if (isItemBlock) {
                    inpCampo.value = '__block';
                    inpGrupo.value = 0;
                } else if (isItem) {
                    inpCampo.value = c.replace(/^items\[\]\./i, '');
                } else {
                    inpCampo.value = c;
                }

                applyItemBlockRulesUI();
                updateCampoFinalPreview();

                selTipoDato.value = r.tipoDato || 'Texto';
                inpOrden.value = r.orden || 10;
                inpGrupo.value = (r.grupo != null ? r.grupo : 1);
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
                if (chkItem) chkItem.checked = false;
                if (chkItemBlock) chkItemBlock.checked = false;

                applyItemBlockRulesUI();
                updateCampoFinalPreview();
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

                inpEjemplo.value = selected;

                if (idx < 0) {
                    taHint.value = selected;
                    setStatus('Ejemplo capturado ✅', true);
                    return;
                }

                const beforeStart = Math.max(0, idx - 120);
                const afterEnd = Math.min(full.length, idx + selected.length + 120);
                taHint.value = full.substring(beforeStart, afterEnd);

                setStatus('Ejemplo capturado ✅', true);
            }

            document.getElementById('prePreview').addEventListener('mouseup', () => setTimeout(captureSelectionFromPreview, 0));

            async function apiLoadPreviewFile(file) {
                const fd = new FormData();
                fd.append('file', file);

                const r = await fetch('/Api/DocPreview.ashx', { method: 'POST', body: fd });
                if (!r.ok) throw new Error('DocPreview ' + r.status);
                return await r.json();
            }

            btnLoadPreview.onclick = async () => {
                try {
                    if (fileTxt.files && fileTxt.files.length > 0) {
                        const f = fileTxt.files[0];
                        const resp = await apiLoadPreviewFile(f);
                        if (!resp.ok) { setStatus(resp.error || 'Error preview', false); return; }

                        prePreview.textContent = resp.text || '';
                        taPreviewSrc.value = resp.text || '';
                        setStatus('Preview cargado ✅ (' + (resp.modeUsed || 'auto') + ')', true);
                        return;
                    }

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

                const ejemplo = (inpEjemplo.value || '').trim();
                const hintContext = (taHint.value || '').trim();
                if (!ejemplo) { setStatus('Seleccioná un VALOR en el preview (Ejemplo)', false); return; }
                if (!hintContext) { setStatus('Falta HintContext (seleccioná desde el preview)', false); return; }

                let campoFinal = '';
                let grupoFinal = parseInt(inpGrupo.value || '1', 10) || 1;

                if (chkItemBlock && chkItemBlock.checked) {
                    campoFinal = 'items[].__block';
                    grupoFinal = 0;
                } else {
                    const campoRaw = (inpCampo.value || '').trim();
                    if (!campoRaw) { setStatus('Falta Campo', false); return; }

                    campoFinal = normalizeCampo(campoRaw);
                    if (chkItem && chkItem.checked) campoFinal = 'items[].' + String(campoFinal).replace(/^items\[\]\./i, '');
                }

                const payload = {
                    id: editingId,
                    docTipoCodigo: codigo,
                    campo: campoFinal,
                    tipoDato: selTipoDato.value,
                    orden: parseInt(inpOrden.value || '0', 10) || 0,
                    grupo: grupoFinal,
                    activo: selActivo.value === '1',
                    ejemplo: ejemplo,
                    hintContext: hintContext,
                    modo: 'Assisted'
                };

                try {
                    const resp = await apiSaveRegla(payload);
                    if (!resp.ok) { setStatus(resp.error || 'Error guardando', false); return; }

                    setStatus('Guardado ✅', true);
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
                    const resp = await apiTestRegex({ regex: r.regex, grupo: (r.grupo != null ? r.grupo : 1), text: text });
                    if (!resp.ok) { setStatus(resp.error || 'Error', false); return; }
                    if (!resp.success) { setStatus('No match ❌', false); return; }

                    setStatus('Match ✅ Valor: ' + (resp.value || ''), true);
                } catch (e) {
                    console.warn(e);
                    setStatus('Error probando: ' + e.message, false);
                }
            }

            async function deleteRule(id) {
                const r = reglas.find(x => x.id === id);
                if (!r) return;

                const codigo = (selDocTipo.value || '').trim();
                if (!codigo) { setStatus('Elegí un DocTipo', false); return; }

                const ok = confirm(`¿Eliminar la regla "${r.campo}" (Id=${id})?`);
                if (!ok) return;

                try {
                    const resp = await apiDeleteRegla(codigo, id);
                    if (!resp.ok) { setStatus(resp.error || 'Error eliminando', false); return; }

                    if (editingId === id) clearForm();
                    setStatus('Eliminado ✅', true);
                    await reloadReglas();
                } catch (e) {
                    console.warn(e);
                    setStatus('Error eliminando: ' + e.message, false);
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
                    updateCampoFinalPreview();
                } catch (e) {
                    console.warn(e);
                    setStatus('Error init: ' + e.message, false);
                }
            })();
        })();
    </script>

    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>