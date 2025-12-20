<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_DocTipoReglas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_DocTipoReglas" %>

<!DOCTYPE html>
<html>
<head runat="server">
    <title>WF DocTipo - Reglas de Extracción</title>
    <meta charset="utf-8" />

   <style>
      body { font-family: Segoe UI, Arial; margin: 0;   }
     .wrap { display: grid; grid-template-columns: 520px 1fr; gap: 12px; padding: 12px; }
     .card { border: 1px solid rgba(148,163,184,.35); border-radius: 12px; padding: 12px; }
     .row { display:flex; gap:8px; align-items:center; }
     .row > * { flex: 1; }
     label { font-weight: 600; font-size: 13px; }
     select, input, textarea, button { width:100%; box-sizing:border-box; padding:8px; border-radius:10px; border:1px solid rgba(148,163,184,.55); }
     textarea { font-family: Consolas, Monaco, monospace; font-size: 12px; }
     .btn { cursor:pointer; background:#fff; }
     .btn-small { width:auto; padding:8px 10px; }
     .grid { width:100%; border-collapse: collapse; margin-top: 10px; }
     .grid th, .grid td { border-bottom: 1px solid rgba(148,163,184,.25); padding: 8px; font-size: 13px; }
     .muted { color:#475569; font-size: 12px; }
     pre { white-space: pre-wrap; margin:0; font-family: Consolas, Monaco, monospace; font-size: 12px; }
     .preview { height: 520px; overflow:auto; border: 1px solid rgba(148,163,184,.35); border-radius: 12px; padding: 10px; }
     .pill { display:inline-block; padding:4px 8px; border-radius: 999px; border: 1px solid rgba(148,163,184,.35); font-size: 12px; }
     .ok { color: #065f46; }
     .bad { color: #991b1b; }
 </style>

   
</head>
<body>
<form id="form1" runat="server">
    <div class="wrap">
        <!-- IZQUIERDA -->
        <div class="card">
            <div class="row">
                <div>
                    <label>DocTipo</label>
                    <select id="selDocTipo"></select>
                    <div class="muted">Se carga desde WF_DocTipo.</div>
                </div>
                <div style="max-width:140px;">
                    <label>&nbsp;</label>
                    <button type="button" id="btnReload" class="btn">Recargar</button>
                </div>
            </div>

            <hr style="border:none;border-top:1px solid rgba(148,163,184,.25);margin:12px 0;" />

            <div class="row">
                <div>
                    <label>Campo</label>
                    <input id="inpCampo" type="text" placeholder="Ej: ProveedorCUIT" />
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

            <div class="row">
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

            <label style="margin-top:8px;display:block;">Ejemplo (seleccioná texto en el preview)</label>
            <input id="inpEjemplo" type="text" placeholder="Se llena al seleccionar texto" />

            <label style="margin-top:8px;display:block;">HintContext (auto)</label>
            <textarea id="taHint" rows="4" placeholder="Se llena al seleccionar texto (60 antes + ejemplo + 60 después)"></textarea>

            <div class="row" style="margin-top:10px;">
                <button type="button" id="btnGuardar" class="btn">Guardar (genera regex)</button>
                <button type="button" id="btnProbar" class="btn">Probar</button>
            </div>

            <div style="margin-top:8px;">
                <span class="pill" id="lblEstado">Listo</span>
            </div>

            <table class="grid" id="tblReglas">
                <thead>
                    <tr>
                        <th>Orden</th>
                        <th>Campo</th>
                        <th>Tipo</th>
                        <th>Activo</th>
                        <th style="width:110px;">Acciones</th>
                    </tr>
                </thead>
                <tbody></tbody>
            </table>
        </div>

        <!-- DERECHA -->
        <div class="card">
            <div class="row">
                <div>
                    <label>Texto de preview</label>
                    <textarea id="taPreviewSrc" rows="6" placeholder="Pegá texto acá, o cargá un .txt abajo..."></textarea>
                </div>
            </div>

            <div class="row" style="margin-top:8px;">
                <div>
                    <input id="fileTxt" type="file" accept=".txt" />
                    <div class="muted">Por ahora .txt (simple y confiable). Docx/PDF lo integramos después con tu pipeline real.</div>
                </div>
                <div style="max-width:160px;">
                    <button type="button" id="btnLoadPreview" class="btn">Cargar preview</button>
                </div>
            </div>

            <label style="margin-top:10px;display:block;">Preview (seleccioná texto con el mouse)</label>
            <div class="preview"><pre id="prePreview"></pre></div>

            <div class="muted" style="margin-top:8px;">
                Tip: seleccioná el valor exacto (ej “20-12345678-9”) → se guarda como Ejemplo + contexto.
            </div>
        </div>
    </div>

<script>
(() => {
    const API = '/Api/Generico.ashx';

    const selDocTipo = document.getElementById('selDocTipo');
    const btnReload  = document.getElementById('btnReload');

    const inpCampo   = document.getElementById('inpCampo');
    const selTipoDato= document.getElementById('selTipoDato');
    const inpOrden   = document.getElementById('inpOrden');
    const inpGrupo   = document.getElementById('inpGrupo');
    const selActivo  = document.getElementById('selActivo');
    const inpEjemplo = document.getElementById('inpEjemplo');
    const taHint     = document.getElementById('taHint');

    const btnGuardar = document.getElementById('btnGuardar');
    const btnProbar  = document.getElementById('btnProbar');
    const lblEstado  = document.getElementById('lblEstado');

    const tblBody    = document.querySelector('#tblReglas tbody');

    const taPreviewSrc = document.getElementById('taPreviewSrc');
    const fileTxt      = document.getElementById('fileTxt');
    const btnLoadPreview = document.getElementById('btnLoadPreview');
    const prePreview   = document.getElementById('prePreview');

    let reglas = [];
    let editingId = 0;

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
        const r = await fetch(`${API}?action=doctipo.reglas.list&codigo=${encodeURIComponent(codigo||'')}`, { cache: 'no-store' });
        if (!r.ok) throw new Error('doctipo.reglas.list ' + r.status);
        return await r.json();
    }

    async function apiSaveRegla(payload) {
        const r = await fetch(`${API}?action=doctipo.reglas.save`, {
            method: 'POST',
            headers: { 'Content-Type':'application/json' },
            body: JSON.stringify(payload)
        });
        if (!r.ok) throw new Error('doctipo.reglas.save ' + r.status);
        return await r.json();
    }

    async function apiTestRegex(payload) {
        const r = await fetch(`${API}?action=doctipo.reglas.test`, {
            method: 'POST',
            headers: { 'Content-Type':'application/json' },
            body: JSON.stringify(payload)
        });
        if (!r.ok) throw new Error('doctipo.reglas.test ' + r.status);
        return await r.json();
    }

    function renderReglas() {
        tblBody.innerHTML = '';
        reglas.forEach(r => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>${r.orden}</td>
                <td>${escapeHtml(r.campo||'')}</td>
                <td>${escapeHtml(r.tipoDato||'')}</td>
                <td>${r.activo ? 'Sí' : 'No'}</td>
                <td>
                  <button type="button" class="btn btn-small" data-edit="${r.id}">Editar</button>
                  <button type="button" class="btn btn-small" data-test="${r.id}">Probar</button>
                </td>
            `;
            tblBody.appendChild(tr);
        });

        tblBody.querySelectorAll('[data-edit]').forEach(b => {
            b.onclick = () => loadToForm(parseInt(b.getAttribute('data-edit'),10));
        });
        tblBody.querySelectorAll('[data-test]').forEach(b => {
            b.onclick = () => testRule(parseInt(b.getAttribute('data-test'),10));
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

    function escapeHtml(s){
        return (s||'').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
    }

    function getPreviewText(){
        return prePreview.textContent || '';
    }

    // ========= CAPTURA DE SELECCIÓN (Ejemplo + HintContext) =========
    function captureSelectionFromPreview() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        // Solo si la selección está dentro del preview
        const anchor = sel.anchorNode;
        if (!anchor) return;
        const root = document.getElementById('prePreview');
        if (!root.contains(anchor)) return;

        const selected = (sel.toString() || '').trim();
        if (!selected) return;

        const full = getPreviewText();
        const idx = full.indexOf(selected);
        if (idx < 0) {
            // si no encontramos literal, igual guardamos ejemplo
            inpEjemplo.value = selected;
            taHint.value = selected;
            return;
        }

        const beforeStart = Math.max(0, idx - 60);
        const afterEnd = Math.min(full.length, idx + selected.length + 60);
        const ctx = full.substring(beforeStart, afterEnd);

        inpEjemplo.value = selected;
        taHint.value = ctx;

        setStatus('Ejemplo capturado ✅', true);
    }

    document.getElementById('prePreview').addEventListener('mouseup', () => {
        setTimeout(captureSelectionFromPreview, 0);
    });

    // ========= Preview loader =========
    btnLoadPreview.onclick = async () => {
        try {
            if (fileTxt.files && fileTxt.files.length > 0) {
                const f = fileTxt.files[0];
                const txt = await f.text();
                prePreview.textContent = txt;
                setStatus('Preview cargado desde archivo ✅', true);
                return;
            }
            prePreview.textContent = taPreviewSrc.value || '';
            setStatus('Preview cargado desde texto ✅', true);
        } catch (e) {
            console.warn(e);
            setStatus('Error cargando preview', false);
        }
    };

    // ========= Guardar (genera regex en server) =========
    btnGuardar.onclick = async () => {
        const codigo = (selDocTipo.value || '').trim();
        if (!codigo) { setStatus('Elegí un DocTipo', false); return; }

        const campo = (inpCampo.value || '').trim();
        if (!campo) { setStatus('Falta Campo', false); return; }

        const payload = {
            id: editingId,
            docTipoCodigo: codigo,
            campo: campo,
            tipoDato: selTipoDato.value,
            orden: parseInt(inpOrden.value||'0',10) || 0,
            grupo: parseInt(inpGrupo.value||'1',10) || 1,
            activo: selActivo.value === '1',
            ejemplo: (inpEjemplo.value || '').trim(),
            hintContext: (taHint.value || '').trim(),
            modo: 'LabelValue'
        };

        try {
            const resp = await apiSaveRegla(payload);
            if (!resp.ok) { setStatus(resp.error || 'Error guardando', false); return; }

            setStatus('Guardado ✅ (regex generado)', true);

            // recargar grilla
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
        // prueba “la regla actual del form” si estamos editando, sino la primera
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

    // init
    (async function init(){
        try {
            const items = await apiListDocTipos();
            selDocTipo.innerHTML = '';
            const o0 = document.createElement('option');
            o0.value = '';
            o0.textContent = '(seleccioná DocTipo)';
            selDocTipo.appendChild(o0);

            (items||[]).forEach(it => {
                const o = document.createElement('option');
                o.value = it.codigo || '';
                o.textContent = (it.codigo||'') + (it.nombre ? ' — ' + it.nombre : '');
                selDocTipo.appendChild(o);
            });

            setStatus('Listo ✅', true);
        } catch(e) {
            console.warn(e);
            setStatus('Error init: ' + e.message, false);
        }
    })();

})();
</script>

</form>
</body>
</html>
