<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Instancias.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Instancias"  %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Instancias</title>
    <meta charset="utf-8" />

    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <!-- Si preferís intranet 100% sin Internet, podés copiar bootstrap a Styles/bootstrap.min.css y usar esa ruta local.
         Por ahora, dejo tu CDN tal cual lo enviaste. -->
   <script src="Scripts/bootstrap.bundle.min.js"></script>
    <!-- <link rel="stylesheet" href="Styles/bootstrap.min.css" /> -->

    <style>
        body { padding: 12px; }
        pre.log-view { max-height: 220px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }

         body { background: #f6f7fb; }
         .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
         .ws-card .card-body { padding: 20px; }
         .ws-muted { color: rgba(0,0,0,.65); }
         .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(16,24,40,.06); border-radius: 16px; }
         .ws-title { font-weight: 700; letter-spacing: .2px; }
         .ws-chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.08); color: #0d6efd; font-size: .78rem; font-weight: 600; }
         .ws-chip.ws-chip-muted { background: rgba(108,117,125,.10); color: #6c757d; }
         .ws-grid { border-radius: 14px; overflow: hidden; border: 1px solid rgba(16,24,40,.08); }
         .table> :not(caption)>*>* { vertical-align: middle; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="container-fluid">

            <div class="ws-topbar p-3 mb-3 ws-card">
                <div class="d-flex align-items-center justify-content-between">
                    <div>
                        <div class="ws-title">Instancias</div>
                        <div class="ws-muted small">Seguimiento, datos y logs</div>
                    </div>

                    <div class="d-flex gap-2">
                        <asp:HyperLink ID="lnkBackTareas" runat="server" Visible="false" CssClass="btn btn-sm btn-outline-secondary">
                            Volver a tareas
                        </asp:HyperLink>
                    </div>
                </div>
            </div>

           <div class="row g-2 align-items-end mb-2">
    <div class="col-md-4">
        <label class="form-label mb-0">Definición:</label>
        <asp:DropDownList ID="ddlDef" runat="server"
            CssClass="form-select form-select-sm"
            AutoPostBack="true"
            OnSelectedIndexChanged="ddlDef_SelectedIndexChanged" />
    </div>

    <div class="col-md-2">
        <label class="form-label mb-0">Estado:</label>
        <div class="btn-group btn-group-sm w-100 mb-1" role="group" aria-label="Filtro de estado">
            <asp:LinkButton ID="lnkEstadoTodos" runat="server" CssClass="btn btn-outline-secondary" OnClick="lnkEstado_Click" CommandArgument="">Todos</asp:LinkButton>
            <asp:LinkButton ID="lnkEstadoEnCurso" runat="server" CssClass="btn btn-outline-primary" OnClick="lnkEstado_Click" CommandArgument="EnCurso">En curso</asp:LinkButton>
            <asp:LinkButton ID="lnkEstadoError" runat="server" CssClass="btn btn-outline-danger" OnClick="lnkEstado_Click" CommandArgument="Error">Error</asp:LinkButton>
            <asp:LinkButton ID="lnkEstadoFinalizado" runat="server" CssClass="btn btn-outline-dark" OnClick="lnkEstado_Click" CommandArgument="Finalizado">Finalizado</asp:LinkButton>
        </div>

        <asp:DropDownList ID="ddlEstado" runat="server"
            CssClass="form-select form-select-sm"
            AutoPostBack="true"
            OnSelectedIndexChanged="ddlEstado_SelectedIndexChanged">
            <asp:ListItem Text="(Todos)" Value="" />
            <asp:ListItem Text="EnCurso" Value="EnCurso" />
            <asp:ListItem Text="Finalizado" Value="Finalizado" />
            <asp:ListItem Text="Error" Value="Error" />
        </asp:DropDownList>
    </div>

    <div class="col-md-3">
        <label class="form-label mb-0">Buscar:</label>
        <asp:TextBox ID="txtBuscar" runat="server"
            CssClass="form-control form-control-sm"
            placeholder="Id / texto (OC, proveedor, etc.)" />
    </div>

    <div class="col-md-2">
        <div class="form-check mt-3">
            <asp:CheckBox ID="chkMostrarFinalizados" runat="server"
                CssClass="form-check-input"
                AutoPostBack="true"
                OnCheckedChanged="chkMostrarFinalizados_CheckedChanged" />
            <label class="form-check-label" for="chkMostrarFinalizados">
                Mostrar finalizados
            </label>
        </div>
    </div>

    <div class="col-md-1 d-grid">
        <asp:Button ID="btnRefrescar" runat="server"
            Text="Refrescar"
            CssClass="btn btn-sm btn-primary"
            OnClick="btnRefrescar_Click" />
    </div>

    <div class="col-md-12 d-flex gap-2">
        <asp:Button ID="btnBuscar" runat="server"
            Text="Buscar"
            CssClass="btn btn-sm btn-outline-primary"
            OnClick="btnBuscar_Click" />
        <asp:Button ID="btnCrearInst" runat="server"
            Text="Ejecutar (sin input)"
            CssClass="btn btn-sm btn-outline-success"
            OnClick="btnCrearInst_Click" />
    </div>
</div>

            <div class="row g-3">
                <div class="col-md-7">
                    <div class="card ws-card">
                        <div class="card-body">
                            <div class="d-flex align-items-center justify-content-between mb-2">
                                <div class="ws-title">Listado</div>
                                <div class="ws-chip ws-chip-muted">TOP 500</div>
                            </div>

                            <div class="ws-grid">
                                <asp:GridView ID="gvInst" runat="server"
                                    CssClass="table table-sm table-striped mb-0"
                                    AutoGenerateColumns="false"
                                    AllowPaging="true"
                                    PageSize="20"
                                    OnPageIndexChanging="gvInst_PageIndexChanging"
                                    OnRowCommand="gvInst_RowCommand">
                                    <Columns>
                                        <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="70px" />
                                        <asp:BoundField DataField="Estado" HeaderText="Estado" ItemStyle-Width="100px" />
                                        <asp:BoundField DataField="FechaInicio" HeaderText="Inicio" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" ItemStyle-Width="160px" />
                                        <asp:BoundField DataField="FechaFin" HeaderText="Fin" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" ItemStyle-Width="160px" />

                                        <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="170px">
                                            <ItemTemplate>
                                                <asp:LinkButton ID="lnkDatos" runat="server"
                                                    CommandName="Datos"
                                                    CommandArgument='<%# Eval("Id") %>'
                                                    CssClass="btn btn-sm btn-outline-primary">Datos</asp:LinkButton>

                                                <asp:LinkButton ID="lnkLogs" runat="server"
                                                    CommandName="Logs"
                                                    CommandArgument='<%# Eval("Id") %>'
                                                    CssClass="btn btn-sm btn-outline-secondary">Logs</asp:LinkButton>

                                                <asp:LinkButton ID="lnkReej" runat="server"
                                                    CommandName="Reejecutar"
                                                    CommandArgument='<%# Eval("Id") %>'
                                                    CssClass="btn btn-sm btn-outline-success"
                                                    OnClientClick="return confirm('Reejecutar la instancia seleccionada?');">Reej.</asp:LinkButton>
                                            </ItemTemplate>
                                        </asp:TemplateField>
                                    </Columns>
                                </asp:GridView>
                            </div>
                        </div>
                    </div>
                </div>

                <div class="col-md-5">
                    <div class="card ws-card mb-3">
                        <div class="card-body">
                            <div class="ws-title mb-2">Datos (DatosContexto)</div>
                            <asp:Panel ID="pnlDatos" runat="server" Visible="false">
                                <pre class="log-view"><asp:Literal ID="litDatos" runat="server"></asp:Literal></pre>
                            </asp:Panel>
                            <asp:Panel ID="pnlDatosEmpty" runat="server" Visible="true" CssClass="ws-muted small">
                                Seleccioná una instancia y presioná “Datos”.
                            </asp:Panel>
                        </div>
                    </div>

                    <div class="card ws-card">
                        <div class="card-body">
                            <div class="ws-title mb-2">Logs</div>
                            <asp:Panel ID="pnlLogs" runat="server" Visible="false">
                                <pre class="log-view"><asp:Literal ID="litLogs" runat="server"></asp:Literal></pre>
                            </asp:Panel>
                            <asp:Panel ID="pnlLogsEmpty" runat="server" Visible="true" CssClass="ws-muted small">
                                Seleccioná una instancia y presioná “Logs”.
                            </asp:Panel>
                        </div>
                    </div>
                </div>
            </div>

        </div>
    </form>
</body>
</html>
