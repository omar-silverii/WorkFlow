<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Instancia_Mapa.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Instancia_Mapa" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow Studio - Mapa de instancia</title>
    <meta charset="utf-8" />
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <style>
        body { padding: 12px; background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.08); color: #0d6efd; font-size: .78rem; font-weight: 600; }
        .ws-chip-ok { background: rgba(25,135,84,.10); color: #198754; }
        .ws-chip-warn { background: rgba(255,193,7,.18); color: #8a6500; }
        .ws-chip-error { background: rgba(220,53,69,.10); color: #dc3545; }
        .ws-chip-muted { background: rgba(108,117,125,.10); color: #6c757d; }
        .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table> :not(caption)>*>* { vertical-align: middle; }
        .ws-flow { position: relative; margin-left: 10px; }
        .ws-step { position: relative; display: flex; gap: 12px; padding: 0 0 14px 0; }
        .ws-step:not(:last-child)::before { content: ""; position: absolute; left: 13px; top: 30px; bottom: -2px; border-left: 2px solid rgba(16,24,40,.12); }
        .ws-dot { width: 28px; height: 28px; min-width: 28px; border-radius: 999px; display: inline-flex; align-items: center; justify-content: center; font-size: 13px; font-weight: 700; border: 1px solid rgba(16,24,40,.12); background: #fff; color: #6c757d; z-index: 1; }
        .ws-dot-ok { background: #198754; color: #fff; border-color: #198754; }
        .ws-dot-warn { background: #ffc107; color: #212529; border-color: #ffc107; }
        .ws-dot-error { background: #dc3545; color: #fff; border-color: #dc3545; }
        .ws-node-card { flex: 1; border: 1px solid rgba(16,24,40,.08); border-radius: 14px; padding: 12px 14px; background: #fff; }
        .ws-node-card-ok { border-left: 4px solid #198754; }
        .ws-node-card-warn { border-left: 4px solid #ffc107; }
        .ws-node-card-error { border-left: 4px solid #dc3545; }
        .ws-node-card-muted { opacity: .75; }
        pre.ws-pre { max-height: 360px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 10px; font-size: .75rem; border-radius: 12px; }
        .ws-kv { display: grid; grid-template-columns: 150px 1fr; gap: 4px 12px; font-size: .9rem; }
        @media (max-width: 768px) { .ws-kv { grid-template-columns: 1fr; } }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="ws-card card mb-3">
            <div class="card-body">
                <div class="d-flex align-items-start justify-content-between flex-wrap gap-2">
                    <div>
                        <div class="ws-title h5 mb-1">Mapa de instancia / expediente</div>
                        <div class="ws-muted small">Recorrido visual, tareas, ramas y logs principales de una ejecución.</div>
                    </div>
                    <div class="d-flex gap-2">
                        <asp:HyperLink ID="lnkVolver" runat="server" CssClass="btn btn-sm btn-outline-secondary">Volver a instancias</asp:HyperLink>
                    </div>
                </div>
            </div>
        </div>

        <asp:Panel ID="pnlError" runat="server" Visible="false" CssClass="alert alert-warning">
            <asp:Literal ID="litError" runat="server" />
        </asp:Panel>

        <asp:Panel ID="pnlContenido" runat="server" Visible="false">
            <div class="row g-3 mb-3">
                <div class="col-lg-4">
                    <div class="card ws-card h-100">
                        <div class="card-body">
                            <div class="ws-title mb-2">Resumen</div>
                            <asp:Literal ID="litResumen" runat="server" />
                        </div>
                    </div>
                </div>
                <div class="col-lg-8">
                    <div class="card ws-card h-100">
                        <div class="card-body">
                            <div class="d-flex align-items-center justify-content-between mb-2">
                                <div>
                                    <div class="ws-title">Camino ejecutado</div>
                                    <div class="ws-muted small">Ordenado por los logs reales de la instancia.</div>
                                </div>
                                <asp:Literal ID="litEstadoChip" runat="server" />
                            </div>
                            <asp:Literal ID="litMapa" runat="server" />
                        </div>
                    </div>
                </div>
            </div>

            <div class="row g-3">
                <div class="col-lg-6">
                    <div class="card ws-card h-100">
                        <div class="card-body">
                            <div class="ws-title mb-2">Tareas humanas</div>
                            <asp:Literal ID="litTareas" runat="server" />
                        </div>
                    </div>
                </div>
                <div class="col-lg-6">
                    <div class="card ws-card h-100">
                        <div class="card-body">
                            <div class="ws-title mb-2">Nodos no ejecutados</div>
                            <asp:Literal ID="litNoEjecutados" runat="server" />
                        </div>
                    </div>
                </div>
            </div>

            <div class="card ws-card mt-3">
                <div class="card-body">
                    <div class="ws-title mb-2">Logs principales</div>
                    <asp:Literal ID="litLogs" runat="server" />
                </div>
            </div>
        </asp:Panel>
    </main>
</form>
<script src="<%= ResolveUrl("~/Scripts/bootstrap.bundle.min.js") %>"></script>
</body>
</html>
