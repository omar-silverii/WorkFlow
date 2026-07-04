<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_AiRegression.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_AiRegression" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Banco de regresión IA</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />

    <link href="Content/bootstrap.min.css" rel="stylesheet" />

    <style>
        body { padding: 12px; background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(16,24,40,.06); border-radius: 16px; }
        .ws-chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.08); color: #0d6efd; font-size: .78rem; font-weight: 600; }
        .ws-badge-ok { background: rgba(25,135,84,.12); color: #198754; border: 1px solid rgba(25,135,84,.22); }
        .ws-badge-fail { background: rgba(220,53,69,.12); color: #dc3545; border: 1px solid rgba(220,53,69,.22); }
        .ws-badge-skip { background: rgba(108,117,125,.12); color: #6c757d; border: 1px solid rgba(108,117,125,.22); }
        .ws-pre { max-height: 360px; overflow: auto; background: #111827; color: #e5e7eb; border-radius: 12px; padding: 12px; font-size: .76rem; white-space: pre-wrap; }
        .ws-check-ok { color: #198754; font-weight: 600; }
        .ws-check-fail { color: #dc3545; font-weight: 600; }
        .ws-table-wrap { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table > :not(caption) > * > * { vertical-align: middle; }
        details summary { cursor: pointer; }
        .ws-stat-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; }
        .ws-stat { border: 1px solid rgba(16,24,40,.08); border-radius: 14px; padding: 12px; background: #fff; }
        .ws-stat .num { font-size: 1.35rem; font-weight: 800; line-height: 1; }
        .ws-filterbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 12px; border: 1px solid rgba(16,24,40,.08); border-radius: 14px; background: rgba(248,250,252,.9); }
        .ws-filterbar .btn.active { box-shadow: inset 0 0 0 1px rgba(13,110,253,.45); background: rgba(13,110,253,.10); color: #0d6efd; }
        .ws-node-chip { display: inline-flex; align-items: center; padding: 3px 8px; margin: 1px 2px 1px 0; border-radius: 999px; background: rgba(15,23,42,.06); color: #334155; font-size: .72rem; font-weight: 600; }
        .ws-semantic-chip { background: rgba(111,66,193,.10); color: #6f42c1; border: 1px solid rgba(111,66,193,.18); }
        .ws-case-actions { display: flex; flex-wrap: wrap; gap: 6px; }
        .ws-phrase-box { background: rgba(248,250,252,.95); border: 1px solid rgba(16,24,40,.08); border-radius: 12px; padding: 10px 12px; line-height: 1.45; }
        .ws-hidden-by-filter { display: none !important; }
        .ws-copy-toast { position: fixed; right: 18px; bottom: 18px; z-index: 9999; max-width: 360px; padding: 10px 14px; border-radius: 12px; background: #111827; color: #fff; box-shadow: 0 10px 24px rgba(16,24,40,.18); font-size: .85rem; display: none; }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="ws-topbar p-3 mb-3 ws-card">
            <div class="d-flex align-items-center justify-content-between flex-wrap gap-2">
                <div>
                    <div class="ws-title">Banco de regresión IA</div>
                    <div class="ws-muted small">Prueba frases patrón del Constructor IA y valida nodos, conexiones y auditoría semántica.</div>
                </div>
                <span class="ws-chip">fix70</span>
            </div>
        </div>

        <div class="card ws-card mb-3">
            <div class="card-body">
                <div class="row g-2 align-items-end">
                    <div class="col-md-6 col-lg-5">
                        <label class="form-label mb-1">Caso</label>
                        <asp:DropDownList ID="ddlCases" runat="server" CssClass="form-select form-select-sm" />
                    </div>
                    <div class="col-md-auto d-grid">
                        <asp:Button ID="btnRunSelected" runat="server" CssClass="btn btn-sm btn-primary" Text="Probar caso" OnClick="btnRunSelected_Click" />
                    </div>
                    <div class="col-md-auto d-grid">
                        <asp:Button ID="btnRunAll" runat="server" CssClass="btn btn-sm btn-outline-primary" Text="Probar todos" OnClick="btnRunAll_Click" />
                    </div>
                    <div class="col-md d-grid d-md-flex justify-content-md-end">
                        <asp:HyperLink ID="lnkWorkflowUI" runat="server" CssClass="btn btn-sm btn-outline-secondary" NavigateUrl="~/WorkflowUI.aspx" Text="Volver al Constructor" />
                    </div>
                </div>
                <div class="small ws-muted mt-2">
                    Los casos se leen desde <code>App_Data/WF_AI/ai_regression_cases.json</code>. Esta pantalla no aplica al canvas ni ejecuta motor.
                </div>
                <asp:Label ID="lblMessage" runat="server" CssClass="small mt-2 d-block" EnableViewState="false" />
            </div>
        </div>

        <asp:Panel ID="pnlIntro" runat="server" CssClass="card ws-card mb-3">
            <div class="card-body">
                <div class="fw-bold mb-1">Uso recomendado</div>
                <div class="ws-muted small">
                    Usá esta pantalla antes y después de cada cambio del Phrase Engine. Si un caso falla, no avances con nuevos nodos hasta revisar el JSON técnico.
                </div>
            </div>
        </asp:Panel>

        <asp:Literal ID="litSummary" runat="server" />
        <asp:Literal ID="litDetails" runat="server" />
    </main>
    <div id="wsAiCopyToast" class="ws-copy-toast" role="status" aria-live="polite"></div>
</form>
<script>
    (function () {
        function qsa(selector) {
            return Array.prototype.slice.call(document.querySelectorAll(selector));
        }

        function matches(el, selector) {
            var p = Element.prototype;
            var f = p.matches || p.msMatchesSelector || p.webkitMatchesSelector;
            return f && f.call(el, selector);
        }

        function closest(el, selector) {
            while (el && el !== document) {
                if (matches(el, selector)) return el;
                el = el.parentNode;
            }
            return null;
        }

        function showToast(text) {
            var toast = document.getElementById('wsAiCopyToast');
            if (!toast) return;
            toast.textContent = text || 'Copiado.';
            toast.style.display = 'block';
            window.clearTimeout(showToast._timer);
            showToast._timer = window.setTimeout(function () { toast.style.display = 'none'; }, 2200);
        }

        function fallbackCopy(text, done) {
            var ta = document.createElement('textarea');
            ta.value = text || '';
            ta.setAttribute('readonly', 'readonly');
            ta.style.position = 'fixed';
            ta.style.left = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); } catch (ex) { }
            document.body.removeChild(ta);
            if (done) done();
        }

        function copyText(text, done) {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text || '').then(function () {
                    if (done) done();
                }, function () {
                    fallbackCopy(text, done);
                });
                return;
            }
            fallbackCopy(text, done);
        }

        function applyFilters() {
            var active = document.querySelector('.ws-ai-filter-btn.active');
            var status = active ? (active.getAttribute('data-status') || '') : '';
            var nodeSelect = document.getElementById('wsAiNodeFilter');
            var nodeType = nodeSelect ? (nodeSelect.value || '') : '';
            var visibleRows = 0;
            var visibleCards = 0;

            qsa('.ws-ai-case-row, .ws-ai-case-detail').forEach(function (el) {
                var itemStatus = el.getAttribute('data-status') || '';
                var itemTypes = el.getAttribute('data-node-types') || '';
                var okStatus = !status || itemStatus === status;
                var okType = !nodeType || itemTypes.indexOf('|' + nodeType + '|') >= 0;
                var visible = okStatus && okType;
                if (visible) {
                    el.classList.remove('ws-hidden-by-filter');
                    if (el.className.indexOf('ws-ai-case-row') >= 0) visibleRows++;
                    if (el.className.indexOf('ws-ai-case-detail') >= 0) visibleCards++;
                } else {
                    el.classList.add('ws-hidden-by-filter');
                }
            });

            var count = document.getElementById('wsAiVisibleCount');
            if (count) count.textContent = visibleCards + ' caso(s) visibles';
        }

        document.addEventListener('click', function (ev) {
            var filterBtn = closest(ev.target, '.ws-ai-filter-btn');
            if (filterBtn) {
                ev.preventDefault();
                qsa('.ws-ai-filter-btn').forEach(function (b) { b.classList.remove('active'); });
                filterBtn.classList.add('active');
                applyFilters();
                return;
            }

            var copyBtn = closest(ev.target, '.ws-ai-copy-btn');
            if (copyBtn) {
                ev.preventDefault();
                var targetId = copyBtn.getAttribute('data-copy-target') || '';
                var value = copyBtn.getAttribute('data-copy-value');
                if (targetId) {
                    var target = document.getElementById(targetId);
                    value = target ? (target.textContent || target.innerText || '') : '';
                }
                copyText(value || '', function () { showToast(copyBtn.getAttribute('data-copy-ok') || 'Copiado.'); });
                return;
            }

            var openBtn = closest(ev.target, '.ws-ai-open-constructor-btn');
            if (openBtn) {
                ev.preventDefault();
                var phrase = openBtn.getAttribute('data-phrase') || '';
                copyText(phrase, function () {
                    try { window.sessionStorage.setItem('WF_AI_REGRESSION_PHRASE', phrase); } catch (ex) { }
                    showToast('Frase copiada. Abriendo Constructor IA...');
                    window.setTimeout(function () { window.location.href = 'WorkflowUI.aspx?aiPhrase=' + encodeURIComponent(phrase); }, 250);
                });
            }
        });

        document.addEventListener('change', function (ev) {
            if (ev.target && ev.target.id === 'wsAiNodeFilter') applyFilters();
        });

        applyFilters();
    })();
</script>
</body>
</html>

