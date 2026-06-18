<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Notificaciones.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Notificaciones" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" lang="es">
<head runat="server">
    <title>Workflow Studio – Notificaciones</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />

    <link rel="stylesheet" href="Content/bootstrap.min.css" />

    <style>
        body { background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
        .table> :not(caption)>*>* { vertical-align: middle; }
        .grid-small td, .grid-small th { padding: 7px 8px; font-size: .84rem; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .ws-prio { display:inline-flex; align-items:center; border-radius:999px; padding:3px 8px; font-size:.75rem; font-weight:700; }
        .ws-prio-normal { background:#e0f2fe; color:#075985; }
        .ws-prio-alta { background:#fef3c7; color:#92400e; }
        .ws-prio-critica { background:#fee2e2; color:#991b1b; }
        .ws-prio-baja { background:#dcfce7; color:#166534; }
        .ws-unread { font-weight:700; }
    </style>
</head>
<body>
    <form id="form1" runat="server">

        <ws:Topbar runat="server" ID="Topbar1" />

        <main class="container-fluid px-3 px-md-4 py-4">
            <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-3">
                <div>
                    <div class="ws-title" style="font-size:1.25rem;">Mis notificaciones</div>
                    <div class="ws-muted small">Avisos internos generados por workflows, tareas o eventos del sistema.</div>
                </div>
                <div class="d-flex gap-2">
                    <asp:Button ID="btnMarcarTodas" runat="server" Text="Marcar visibles como leídas" CssClass="btn btn-sm btn-outline-success" OnClick="btnMarcarTodas_Click" />
                    <asp:HyperLink ID="lnkVolver" runat="server" NavigateUrl="Default.aspx" CssClass="btn btn-sm btn-outline-secondary">← Inicio</asp:HyperLink>
                </div>
            </div>

            <asp:Panel ID="pnlAviso" runat="server" CssClass="alert alert-warning" Visible="false">
                <asp:Literal ID="litAviso" runat="server" />
            </asp:Panel>

            <div class="card ws-card mb-3">
                <div class="card-body">
                    <div class="row g-2 align-items-end">
                        <div class="col-12 col-lg-6">
                            <label class="form-label mb-1">Buscar</label>
                            <asp:TextBox ID="txtFiltro" runat="server" CssClass="form-control form-control-sm" placeholder="Título, mensaje, rol, usuario, instancia" />
                        </div>
                        <div class="col-12 col-lg-3">
                            <div class="form-check mt-4">
                                <asp:CheckBox ID="chkSoloNoLeidas" runat="server" CssClass="form-check-input" Checked="true" />
                                <label class="form-check-label" for="chkSoloNoLeidas">Sólo no leídas</label>
                            </div>
                        </div>
                        <div class="col-12 col-lg-3 d-flex gap-2">
                            <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn btn-sm btn-primary" OnClick="btnBuscar_Click" />
                            <asp:Button ID="btnLimpiar" runat="server" Text="Limpiar" CssClass="btn btn-sm btn-outline-secondary" OnClick="btnLimpiar_Click" />
                        </div>
                    </div>
                </div>
            </div>

            <div class="card ws-card">
                <div class="card-body">
                    <div class="d-flex align-items-center justify-content-between mb-2">
                        <div class="ws-title">Listado</div>
                        <div class="ws-muted small">Paginado 20</div>
                    </div>

                    <div class="ws-grid">
                        <asp:GridView ID="gvNotificaciones" runat="server"
                            CssClass="table table-sm table-striped mb-0 grid-small"
                            AutoGenerateColumns="False"
                            DataKeyNames="Id"
                            AllowPaging="True"
                            PageSize="20"
                            OnRowCommand="gvNotificaciones_RowCommand"
                            OnPageIndexChanging="gvNotificaciones_PageIndexChanging"
                            OnRowDataBound="gvNotificaciones_RowDataBound">
                            <Columns>
                                <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="70px" />
                                <asp:BoundField DataField="FechaCreacion" HeaderText="Fecha" DataFormatString="{0:dd/MM/yyyy HH:mm}" ItemStyle-Width="145px" />
                                <asp:TemplateField HeaderText="Estado" ItemStyle-Width="95px">
                                    <ItemTemplate>
                                        <asp:Literal ID="litEstado" runat="server" Text='<%# GetEstadoHtml(Eval("Leido")) %>' />
                                    </ItemTemplate>
                                </asp:TemplateField>
                                <asp:TemplateField HeaderText="Prioridad" ItemStyle-Width="95px">
                                    <ItemTemplate>
                                        <asp:Literal ID="litPrioridad" runat="server" Text='<%# GetPrioridadHtml(Eval("Prioridad")) %>' />
                                    </ItemTemplate>
                                </asp:TemplateField>
                                <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                <asp:BoundField DataField="Mensaje" HeaderText="Mensaje" />
                                <asp:BoundField DataField="UsuarioDestino" HeaderText="Usuario" ItemStyle-Width="140px" />
                                <asp:BoundField DataField="RolDestino" HeaderText="Rol" ItemStyle-Width="100px" />
                                <asp:BoundField DataField="WF_InstanciaId" HeaderText="Instancia" ItemStyle-Width="90px" />
                                <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="210px">
                                    <ItemTemplate>
                                        <div class="d-flex gap-2">
                                            <asp:HyperLink ID="lnkAbrir" runat="server"
                                                CssClass="btn btn-sm btn-outline-primary"
                                                NavigateUrl='<%# GetUrlAccion(Eval("UrlAccion"), Eval("WF_DefinicionId"), Eval("WF_InstanciaId")) %>'
                                                Visible='<%# TieneUrlAccion(Eval("UrlAccion"), Eval("WF_DefinicionId"), Eval("WF_InstanciaId")) %>'>
                                                Abrir
                                            </asp:HyperLink>
                                            <asp:LinkButton ID="lnkLeida" runat="server"
                                                CommandName="MarcarLeida"
                                                CommandArgument='<%# Eval("Id") %>'
                                                CssClass="btn btn-sm btn-success"
                                                Visible='<%# !Convert.ToBoolean(Eval("Leido")) %>'>
                                                Marcar leída
                                            </asp:LinkButton>
                                        </div>
                                    </ItemTemplate>
                                </asp:TemplateField>
                            </Columns>
                        </asp:GridView>
                    </div>
                </div>
            </div>

            <div class="mt-4 ws-muted" style="font-size:12px;">
                Workflow Studio • Notificaciones internas • Diseño y creación por EDI-SA&reg;
            </div>
        </main>

        <script src="Scripts/bootstrap.bundle.min.js"></script>
    </form>
</body>
</html>
