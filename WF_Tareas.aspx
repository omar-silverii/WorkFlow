<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Tareas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Tareas" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" lang="es">
<head runat="server">
    <title>Workflow Studio – Tareas</title>
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
        .grid-small td, .grid-small th { padding: 6px 8px; font-size: .82rem; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }

    </style> 
    
</head>

<body>
    <form id="form1" runat="server">

        <!-- Topbar coherente -->
        <ws:Topbar runat="server" ID="Topbar1" />

        <main class="container-fluid px-3 px-md-4 py-4">

            <!-- Encabezado de página -->
            <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-3">
                <div>
                    <div class="ws-title" style="font-size:1.25rem;">Tareas humanas</div>
                    <div class="ws-muted small">Mis tareas y tareas asignadas por rol/usuario.</div>
                </div>

                <div class="d-flex gap-2">
                    <asp:HyperLink
                        ID="lnkVolverInstancias"
                        runat="server"
                        NavigateUrl="WF_Instancias.aspx"
                        CssClass="btn btn-sm btn-outline-secondary">
                        ← Volver a instancias
                    </asp:HyperLink>
                </div>
            </div>

            <!-- Filtros -->
            <div class="card ws-card mb-3">
                <div class="card-body">
                    <div class="row g-2 align-items-end">
                        <div class="col-12 col-lg-6">
                            <label class="form-label mb-1">Texto a buscar (título / descripción / rol / usuario)</label>
                            <asp:TextBox ID="txtFiltro" runat="server" CssClass="form-control form-control-sm" />
                        </div>

                        <div class="col-12 col-lg-3">
                            <div class="form-check mt-4">
                                <asp:CheckBox ID="chkSoloPendientes" runat="server" CssClass="form-check-input" Checked="true" />
                                <label class="form-check-label" for="chkSoloPendientes">Sólo pendientes</label>
                            </div>
                        </div>

                        <div class="col-12 col-lg-3 d-flex gap-2">
                            <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn btn-sm btn-primary"
                                OnClick="btnBuscar_Click" />
                            <asp:Button ID="btnLimpiar" runat="server" Text="Limpiar" CssClass="btn btn-sm btn-outline-secondary"
                                OnClick="btnLimpiar_Click" />
                        </div>
                    </div>
                </div>
            </div>

            <!-- Grilla -->
            <div class="card ws-card">
                <div class="card-body">
                    <div class="d-flex align-items-center justify-content-between mb-2">
                        <div class="ws-title">Listado</div>
                        <div class="ws-muted small">Paginado 20</div>
                    </div>

                    <div class="ws-grid">
                        <asp:GridView ID="gvTareas" runat="server"
                            CssClass="table table-sm table-striped mb-0 grid-small"
                            AutoGenerateColumns="False"
                            DataKeyNames="Id"
                            AllowPaging="True"
                            PageSize="20"
                            OnRowCommand="gvTareas_RowCommand"
                            OnPageIndexChanging="gvTareas_PageIndexChanging">
                            <Columns>
                                <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="60px" />
                                <asp:BoundField DataField="WF_InstanciaId" HeaderText="Instancia" ItemStyle-Width="90px" />
                                <asp:BoundField DataField="NodoId" HeaderText="Nodo" ItemStyle-Width="70px" />
                                <asp:BoundField DataField="NodoTipo" HeaderText="Tipo" ItemStyle-Width="110px" />
                                <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                <asp:BoundField DataField="RolDestino" HeaderText="Rol destino" />
                                <asp:BoundField DataField="UsuarioAsignado" HeaderText="Usuario" />
                                <asp:BoundField DataField="Estado" HeaderText="Estado" ItemStyle-Width="90px" />
                                <asp:BoundField DataField="FechaCreacion" HeaderText="Creación" DataFormatString="{0:dd/MM/yyyy HH:mm}" ItemStyle-Width="150px" />
                                <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence" DataFormatString="{0:dd/MM/yyyy HH:mm}" ItemStyle-Width="150px" />

                                <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="170px">
                                    <ItemTemplate>
                                        <div class="d-flex gap-2">
                                            <asp:HyperLink ID="lnkAbrir" runat="server"
                                                CssClass="btn btn-sm btn-primary"
                                                NavigateUrl='<%# "WF_Tarea_Detalle.aspx?id=" + Eval("Id") %>'>
                                                Abrir
                                            </asp:HyperLink>

                                            <asp:LinkButton ID="lnkInstancia" runat="server"
                                                CommandName="VerInstancia"
                                                CommandArgument='<%# Eval("WF_InstanciaId") + "|" + Eval("WF_DefinicionId") %>'
                                                CssClass="btn btn-sm btn-outline-info">
                                                Instancia
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
                Workflow Studio • UI WebForms + SQL • Diseño y creación por EDI-SA&reg;
            </div>

        </main>

        <script src="Scripts/bootstrap.bundle.min.js"></script>
    </form>
</body>
</html>
