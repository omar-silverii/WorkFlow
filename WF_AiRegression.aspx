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
                <span class="ws-chip">fix58</span>
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
</form>
</body>
</html>

