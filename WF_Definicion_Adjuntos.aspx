<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Definicion_Adjuntos.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Definicion_Adjuntos" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow - Adjuntos por definición</title>
    <meta charset="utf-8" />
    <link rel="stylesheet" href="Content/bootstrap.min.css" />
    <style>
        body { background: #f6f7fb; }
        .ws-card { border: 1px solid rgba(0,0,0,.08); border-radius: 16px; box-shadow: 0 10px 30px rgba(0,0,0,.05); }
        .ws-title { font-weight: 600; font-size: 1.05rem; }
        .ws-muted { color: #6c757d; }
        .ws-topbar { background:rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom:1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size:12px; padding:4px 10px; border-radius:999px; background:rgba(13,110,253,.10); color:#0d6efd; border:1px solid rgba(13,110,253,.20); }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container py-4">
        <div class="d-flex justify-content-between align-items-center mb-3">
            <div>
                <h4 class="mb-1">Adjuntos por definición</h4>
                <div class="text-muted small">Configuración funcional de adjuntos para este workflow.</div>
            </div>
            <asp:HyperLink ID="lnkVolver" runat="server" CssClass="btn btn-outline-secondary" NavigateUrl="~/WF_Definiciones.aspx">
                Volver
            </asp:HyperLink>
        </div>

        <asp:Panel ID="pnlMsg" runat="server" Visible="false" CssClass="alert" />

        <div class="card ws-card">
            <div class="card-body">
                <div class="row g-3 mb-4">
                    <div class="col-md-2">
                        <label class="form-label">Id</label>
                        <asp:TextBox ID="txtDefId" runat="server" CssClass="form-control" ReadOnly="true" />
                    </div>
                    <div class="col-md-4">
                        <label class="form-label">Código</label>
                        <asp:TextBox ID="txtCodigo" runat="server" CssClass="form-control" ReadOnly="true" />
                    </div>
                    <div class="col-md-6">
                        <label class="form-label">Nombre</label>
                        <asp:TextBox ID="txtNombre" runat="server" CssClass="form-control" ReadOnly="true" />
                    </div>
                </div>

                <div class="border rounded p-3 bg-light">
                    <div class="ws-title mb-3">Configuración</div>

                    <div class="form-check form-switch mb-3">
                        <asp:CheckBox ID="chkHabilitado" runat="server" CssClass="form-check-input" />
                        <label class="form-check-label" for="<%= chkHabilitado.ClientID %>">
                            Habilitar carga manual de adjuntos para esta definición
                        </label>
                    </div>

                    <div class="row g-3">
                        <div class="col-md-4">
                            <label class="form-label">Destino lógico</label>
                            <asp:DropDownList ID="ddlDestinoTipo" runat="server" CssClass="form-select">
                                <asp:ListItem Text="Instancia" Value="INSTANCIA" />
                                <asp:ListItem Text="Expediente" Value="EXPEDIENTE" />
                                <asp:ListItem Text="Legajo" Value="LEGAJO" />
                                <asp:ListItem Text="Otro" Value="OTRO" />
                            </asp:DropDownList>
                        </div>

                        <div class="col-md-8">
                            <label class="form-label">Detalle / descripción del destino</label>
                            <asp:TextBox ID="txtDestinoTexto" runat="server" CssClass="form-control"
                                placeholder="Ej.: Respaldo administrativo de compras / Legajo documental del caso" />
                        </div>
                    </div>

                    <div class="text-muted small mt-3">
                        Esta configuración queda guardada dentro de la definición del workflow y se usa para informar
                        el destino lógico de los adjuntos subidos por los usuarios.
                    </div>
                </div>

                <div class="mt-4 d-flex gap-2">
                    <asp:Button ID="btnGuardar" runat="server" Text="Guardar" CssClass="btn btn-primary" OnClick="btnGuardar_Click" />
                    <asp:HyperLink ID="lnkVolver2" runat="server" CssClass="btn btn-outline-secondary" NavigateUrl="~/WF_Definiciones.aspx">
                        Cancelar
                    </asp:HyperLink>
                </div>
            </div>
        </div>
    </main>

    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>
