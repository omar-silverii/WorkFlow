<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Gerente_Tareas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Gerente_Tareas" %>

<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Workflow Studio - Bandejas Gerente</title>

    <!-- Bootstrap 5 (local) -->
    <link href="Content/bootstrap.min.css" rel="stylesheet" />

    <style>
        body { background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-card .card-body { padding: 20px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .ws-section-title { font-weight: 700; }
        .table thead th { white-space: nowrap; }
        .ws-kpi { border-radius: 16px; border: 1px solid rgba(0,0,0,.08); background: #fff; }
        .ws-badge { font-size: 12px; }
    </style>
</head>

<body>
<form id="form1" runat="server">

    <!-- Topbar -->
    <nav class="navbar navbar-expand-lg ws-topbar sticky-top">
        <div class="container-fluid px-3 px-md-4">
            <a class="navbar-brand fw-bold" href="Default.aspx">
                Workflow Studio <span class="ws-pill ms-2">Intranet</span>
            </a>

            <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#wsNav"
                aria-controls="wsNav" aria-expanded="false" aria-label="Toggle navigation">
                <span class="navbar-toggler-icon"></span>
            </button>

            <div class="collapse navbar-collapse" id="wsNav">
                <ul class="navbar-nav ms-auto gap-lg-2">
                    <li class="nav-item"><a class="nav-link" href="Default.aspx">Inicio</a></li>

                    <li class="nav-item dropdown">
                        <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">Workflows</a>
                        <ul class="dropdown-menu">
                            <li><a class="dropdown-item" href="WorkflowUI.aspx">➕ Nuevo / Editor</a></li>
                            <li><a class="dropdown-item" href="WF_Definiciones.aspx">📋 Definiciones</a></li>
                        </ul>
                    </li>

                    <li class="nav-item dropdown">
                        <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">Documentos</a>
                        <ul class="dropdown-menu">
                            <li><a class="dropdown-item" href="WF_DocTipo.aspx">📁 Tipos de documento</a></li>
                            <li><a class="dropdown-item" href="WF_DocTipoReglas.aspx">🧠 Reglas de extracción</a></li>
                        </ul>
                    </li>

                    <li class="nav-item dropdown">
                        <a class="nav-link dropdown-toggle" href="#" role="button" data-bs-toggle="dropdown">Ejecuciones</a>
                        <ul class="dropdown-menu">
                            <li><a class="dropdown-item" href="WF_Instancias.aspx">▶ Instancias</a></li>
                            <li><a class="dropdown-item" href="WF_Instancias.aspx#logs">📜 Logs</a></li>
                        </ul>
                    </li>

                    <li class="nav-item">
                        <a class="nav-link active" href="WF_Gerente_Tareas.aspx">Gerencia</a>
                    </li>
                </ul>
            </div>
        </div>
    </nav>

    <!-- Main -->
    <main class="container-fluid px-3 px-md-4 py-4">

        <!-- Header -->
        <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-3">
            <div>
                <h3 class="mb-1 ws-section-title">Bandejas de Gerencia</h3>
                <div class="ws-muted">
                    Mis tareas, tareas por mi rol, y pendientes en mi alcance.
                    <span class="ms-2 ws-pill">Usuario: <asp:Label ID="lblUser" runat="server" /></span>
                </div>
            </div>

            <div class="d-flex gap-2">
                <asp:Button ID="btnRefresh" runat="server" CssClass="btn btn-outline-secondary" Text="🔄 Actualizar" OnClick="btnRefresh_Click" />
            </div>
        </div>

        <asp:Label ID="lblError" runat="server" CssClass="alert alert-danger" Visible="false" />

        <!-- Tabs -->
        <div class="card ws-card">
            <div class="card-body">

                <ul class="nav nav-tabs" id="tabsGerencia" role="tablist">
                    <li class="nav-item" role="presentation">
                        <button class="nav-link active" id="tab-mis" data-bs-toggle="tab" data-bs-target="#pane-mis" type="button" role="tab">
                            ✅ Mis tareas
                            <span class="badge rounded-pill text-bg-primary ms-1 ws-badge"><asp:Label ID="lblCountMis" runat="server" Text="0" /></span>
                        </button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" id="tab-rol" data-bs-toggle="tab" data-bs-target="#pane-rol" type="button" role="tab">
                            🧑‍💼 Por mi rol
                            <span class="badge rounded-pill text-bg-primary ms-1 ws-badge"><asp:Label ID="lblCountRol" runat="server" Text="0" /></span>
                        </button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" id="tab-alcance" data-bs-toggle="tab" data-bs-target="#pane-alcance" type="button" role="tab">
                            🧭 Mi alcance
                            <span class="badge rounded-pill text-bg-primary ms-1 ws-badge"><asp:Label ID="lblCountAlcance" runat="server" Text="0" /></span>
                        </button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" id="tab-cerradas" data-bs-toggle="tab" data-bs-target="#pane-cerradas" type="button" role="tab">
                            ✅ Cerradas
                            <span class="badge rounded-pill text-bg-primary ms-1 ws-badge"><asp:Label ID="lblCountCerradas" runat="server" Text="0" /></span>
                        </button>
                    </li>
                </ul>

                <div class="tab-content pt-3">
                    <!-- Mis tareas -->
                    <div class="tab-pane fade show active" id="pane-mis" role="tabpanel" aria-labelledby="tab-mis">
                        <div class="table-responsive">
                            <asp:GridView ID="gvMis" runat="server" CssClass="table table-sm table-hover align-middle"
                                AutoGenerateColumns="false" EmptyDataText="No hay tareas asignadas." 
                                GridLines="None" OnRowCommand="gvMis_RowCommand" OnRowDataBound="gv_RowDataBound">
                                <Columns>
                                    <asp:TemplateField HeaderText="">
                                        <ItemTemplate>
                                            <a class="btn btn-sm btn-outline-primary"
                                               href='<%# "WF_Tarea_Ver.aspx?tareaId=" + Eval("Id") %>'>
                                                Abrir
                                            </a>
                                            <asp:LinkButton
                                                ID="btnLiberar"
                                                runat="server"
                                                CssClass="btn btn-sm btn-outline-warning"
                                                Text="Liberar"
                                                CommandName="Liberar"
                                                CommandArgument='<%# Eval("Id") %>' />
                                        </ItemTemplate>
                                        <ItemStyle Width="180px" />
                                    </asp:TemplateField>
                                    <asp:BoundField DataField="Id" HeaderText="TareaId" />
                                    <asp:BoundField DataField="WF_InstanciaId" HeaderText="InstanciaId" />
                                    <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                    <asp:BoundField DataField="RolDestino" HeaderText="Rol" />
                                    <asp:BoundField DataField="ScopeKey" HeaderText="Scope" />
                                    <asp:BoundField DataField="TareaEstado" HeaderText="TareaEstado" />
                                    <asp:BoundField DataField="FechaCreacion" HeaderText="Creada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                    <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                   
                                    <asp:TemplateField HeaderText="Escalamiento">
                                        <ItemTemplate>
                                            <%# (Convert.ToBoolean(Eval("Escalada"))
                                                ? "<span class='badge text-bg-warning'>⚠ Escalada</span>"
                                                : "") %>
                                        </ItemTemplate>
                                        <ItemStyle Width="120px" />
                                    </asp:TemplateField>
                                    <asp:TemplateField HeaderText="SLA">
                                        <ItemTemplate>
                                            <span class='<%# (Convert.ToBoolean(Eval("SlaVencida")) 
                                                ? "badge text-bg-danger" 
                                                : "badge text-bg-secondary") %>'>
                                                <%# Eval("SlaTexto") %>
                                            </span>
                                        </ItemTemplate>
                                        <ItemStyle Width="130px" />
                                    </asp:TemplateField>                                    
                                    <asp:BoundField DataField="FechaCierre" HeaderText="Cerrada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                </Columns>
                            </asp:GridView>
                        </div>
                    </div>

                    <!-- Por mi rol -->
                    <div class="tab-pane fade" id="pane-rol" role="tabpanel" aria-labelledby="tab-rol">
                        <div class="table-responsive">
                            <asp:GridView ID="gvRol" runat="server" CssClass="table table-sm table-hover align-middle"
                                AutoGenerateColumns="false" EmptyDataText="No hay tareas por rol." 
                                GridLines="None" OnRowCommand="gvRol_RowCommand" OnRowDataBound="gv_RowDataBound">
                                <Columns>
                                    <asp:TemplateField HeaderText="">
                                        <ItemTemplate>
                                            <a class="btn btn-sm btn-outline-primary"
                                               href='<%# "WF_Tarea_Ver.aspx?tareaId=" + Eval("Id") %>'>
                                                Abrir
                                            </a>

                                            <asp:LinkButton
                                                ID="btnTomarRol"
                                                runat="server"
                                                CssClass="btn btn-sm btn-outline-success"
                                                Text="Tomar"
                                                CommandName="TomarRol"
                                                CommandArgument='<%# Eval("Id") %>' />
                                        </ItemTemplate>
                                        <ItemStyle Width="160px" />
                                    </asp:TemplateField>
                                    <asp:BoundField DataField="Id" HeaderText="TareaId" />
                                    <asp:BoundField DataField="WF_InstanciaId" HeaderText="InstanciaId" />
                                    <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                    <asp:BoundField DataField="RolDestino" HeaderText="Rol" />
                                    <asp:BoundField DataField="ScopeKey" HeaderText="Scope" />                                   
                                    <asp:BoundField DataField="TareaEstado" HeaderText="TareaEstado" />
                                    <asp:BoundField DataField="FechaCreacion" HeaderText="Creada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                    <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                   <asp:TemplateField HeaderText="Escalamiento">
                                        <ItemTemplate>
                                            <%# (Convert.ToBoolean(Eval("Escalada"))
                                                ? "<span class='badge text-bg-warning'>⚠ Escalada</span>"
                                                : "") %>
                                        </ItemTemplate>
                                        <ItemStyle Width="120px" />
                                    </asp:TemplateField>
                                    <asp:TemplateField HeaderText="SLA">
                                        <ItemTemplate>
                                            <span class='<%# (Convert.ToBoolean(Eval("SlaVencida")) 
                                                ? "badge text-bg-danger" 
                                                : "badge text-bg-secondary") %>'>
                                                <%# Eval("SlaTexto") %>
                                            </span>
                                        </ItemTemplate>
                                        <ItemStyle Width="130px" />
                                    </asp:TemplateField>         
                                    <asp:BoundField DataField="FechaCierre" HeaderText="Cerrada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                </Columns>
                            </asp:GridView>
                        </div>
                    </div>

                    <!-- Mi alcance -->
                    <div class="tab-pane fade" id="pane-alcance" role="tabpanel" aria-labelledby="tab-alcance">
                        <div class="table-responsive">
                            <asp:GridView ID="gvAlcance" runat="server" CssClass="table table-sm table-hover align-middle"
                                AutoGenerateColumns="false" EmptyDataText="No hay tareas pendientes en tu alcance." 
                                GridLines="None" OnRowCommand="gvAlcance_RowCommand" OnRowDataBound="gv_RowDataBound" >
                                <Columns>
                                    <asp:TemplateField HeaderText="">
                                        <ItemTemplate>
                                            <a class="btn btn-sm btn-outline-primary me-1"
                                               href='<%# "WF_Tarea_Ver.aspx?tareaId=" + Eval("Id") %>'>
                                                Abrir
                                            </a>

                                            <asp:LinkButton
                                                ID="btnTomar"
                                                runat="server"
                                                CssClass="btn btn-sm btn-outline-success"
                                                Text="Tomar"
                                                CommandName="Tomar"
                                                CommandArgument='<%# Eval("Id") %>' />
                                        </ItemTemplate>

                                        <ItemStyle Width="150px" />
                                    </asp:TemplateField>
                                    <asp:BoundField DataField="Id" HeaderText="TareaId" />
                                    <asp:BoundField DataField="WF_InstanciaId" HeaderText="InstanciaId" />
                                    <asp:BoundField DataField="ProcesoKey" HeaderText="Proceso" />
                                    <asp:BoundField DataField="EmpresaKey" HeaderText="Empresa" />
                                    <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                    <asp:BoundField DataField="RolDestino" HeaderText="Rol" />
                                    <asp:BoundField DataField="ScopeKey" HeaderText="Scope" />                                    
                                    <asp:BoundField DataField="TareaEstado" HeaderText="TareaEstado" />
                                    <asp:BoundField DataField="FechaCreacion" HeaderText="Creada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                    <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                   <asp:TemplateField HeaderText="Escalamiento">
                                        <ItemTemplate>
                                            <%# (Convert.ToBoolean(Eval("Escalada"))
                                                ? "<span class='badge text-bg-warning'>⚠ Escalada</span>"
                                                : "") %>
                                        </ItemTemplate>
                                        <ItemStyle Width="120px" />
                                    </asp:TemplateField>
                                    <asp:TemplateField HeaderText="SLA">
                                        <ItemTemplate>
                                            <span class='<%# (Convert.ToBoolean(Eval("SlaVencida")) 
                                                ? "badge text-bg-danger" 
                                                : "badge text-bg-secondary") %>'>
                                                <%# Eval("SlaTexto") %>
                                            </span>
                                        </ItemTemplate>
                                        <ItemStyle Width="130px" />
                                    </asp:TemplateField>         
                                    <asp:BoundField DataField="FechaCierre" HeaderText="Cerrada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                </Columns>
                            </asp:GridView>
                        </div>
                    </div>

                     <!-- Cerradas -->
                    <div class="tab-pane fade" id="pane-cerradas" role="tabpanel" aria-labelledby="tab-cerradas">
                        <div class="table-responsive">
                            <asp:GridView ID="gvCerradas" runat="server" CssClass="table table-sm table-hover align-middle"
                                AutoGenerateColumns="false" EmptyDataText="No hay tareas cerradas." GridLines="None">
                                <Columns>
                                    <asp:TemplateField HeaderText="">
                                        <ItemTemplate>
                                            <a class="btn btn-sm btn-outline-primary"
                                               href='<%# "WF_Tarea_Ver.aspx?tareaId=" + Eval("Id") %>'>
                                                Abrir
                                            </a>
                                        </ItemTemplate>
                                        <ItemStyle Width="70px" />
                                    </asp:TemplateField>

                                    <asp:BoundField DataField="Id" HeaderText="TareaId" />
                                    <asp:BoundField DataField="WF_InstanciaId" HeaderText="InstanciaId" />
                                    <asp:BoundField DataField="Titulo" HeaderText="Título" />
                                    <asp:BoundField DataField="RolDestino" HeaderText="Rol" />
                                    <asp:BoundField DataField="ScopeKey" HeaderText="Scope" />
                                    <asp:BoundField DataField="TareaEstado" HeaderText="TareaEstado" />
                                    <asp:BoundField DataField="TareaResultado" HeaderText="Resultado" />
                                    <asp:BoundField DataField="FechaCierre" HeaderText="Cerrada" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                                </Columns>
                            </asp:GridView>
                        </div>
                    </div>

                </div>
            </div>
        </div>

        <div class="mt-3 ws-muted" style="font-size:12px;">
            Workflow Studio • Bandejas Gerencia
        </div>

    </main>

    <!-- Bootstrap 5 (local) -->
    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>
