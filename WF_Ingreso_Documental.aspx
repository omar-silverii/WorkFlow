<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Ingreso_Documental.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Ingreso_Documental" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" lang="es">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Ingreso documental - Workflow Studio</title>

    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <style>
        body { background: #f6f7fb; }
        .ws-topbar { background: rgba(255,255,255,.94); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-muted { color: rgba(0,0,0,.62); }
        .ws-kpi { border-radius: 14px; border: 1px solid rgba(0,0,0,.08); background: #fff; padding: 16px; height: 100%; }
        .ws-kpi-value { font-size: 1.6rem; font-weight: 700; line-height: 1; }
        .ws-table td, .ws-table th { vertical-align: middle; font-size: .86rem; }
        .ws-file { max-width: 310px; word-break: break-word; }
        .ws-reason { max-width: 440px; white-space: normal; }
        .ws-section-title { font-weight: 700; }
        .form-hint { color: rgba(0,0,0,.58); font-size: .78rem; }
        .rule-condition { font-family: Consolas, monospace; font-size: .8rem; }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />
    <asp:ScriptManager runat="server" ID="sm1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="d-flex flex-column flex-lg-row align-items-start align-items-lg-center justify-content-between gap-3 mb-4">
            <div>
                <h3 class="mb-1 ws-section-title">Enrutador de Ingreso Documental</h3>
                <div class="ws-muted">
                    Recibe documentos, resuelve qué workflow corresponde y conserva la relación
                    documento → decisión → instancia → estado.
                </div>
            </div>
            <div class="d-flex gap-2">
                <asp:Button runat="server" ID="btnRefrescar" Text="Actualizar" CssClass="btn btn-outline-secondary"
                    OnClick="btnRefrescar_Click" />
                <a class="btn btn-outline-primary" href="WF_Instancias.aspx">Ver instancias</a>
            </div>
        </div>

        <asp:Literal runat="server" ID="litMsg" />

        <asp:Panel runat="server" ID="pnlSchemaMissing" Visible="false" CssClass="alert alert-warning">
            <div class="fw-semibold mb-1">Falta instalar la base del Enrutador.</div>
            <div>
                Ejecutá <code>fix76_ingreso_documental_enrutador_base.sql</code> en la base
                <code>Workflow</code> y volvé a abrir esta página.
            </div>
        </asp:Panel>

        <asp:Panel runat="server" ID="pnlMain">
            <div class="row g-3 mb-4">
                <div class="col-6 col-lg">
                    <div class="ws-kpi">
                        <div class="ws-kpi-value text-warning"><asp:Label runat="server" ID="lblPendientes" Text="0" /></div>
                        <div class="ws-muted small mt-2">Pendientes de ruta</div>
                    </div>
                </div>
                <div class="col-6 col-lg">
                    <div class="ws-kpi">
                        <div class="ws-kpi-value text-info"><asp:Label runat="server" ID="lblResueltos" Text="0" /></div>
                        <div class="ws-muted small mt-2">Ruta resuelta</div>
                    </div>
                </div>
                <div class="col-6 col-lg">
                    <div class="ws-kpi">
                        <div class="ws-kpi-value text-primary"><asp:Label runat="server" ID="lblEnCurso" Text="0" /></div>
                        <div class="ws-muted small mt-2">Con instancia activa</div>
                    </div>
                </div>
                <div class="col-6 col-lg">
                    <div class="ws-kpi">
                        <div class="ws-kpi-value text-success"><asp:Label runat="server" ID="lblFinalizados" Text="0" /></div>
                        <div class="ws-muted small mt-2">Finalizados</div>
                    </div>
                </div>
                <div class="col-6 col-lg">
                    <div class="ws-kpi">
                        <div class="ws-kpi-value text-danger"><asp:Label runat="server" ID="lblErrores" Text="0" /></div>
                        <div class="ws-muted small mt-2">Con error</div>
                    </div>
                </div>
            </div>

            <div class="card ws-card mb-4">
                <div class="card-body">
                    <div class="d-flex flex-column flex-lg-row justify-content-between gap-2 mb-3">
                        <div>
                            <h5 class="mb-1">Bandeja de documentos</h5>
                            <div class="ws-muted small">
                                Los documentos sin una ruta única quedan aquí. Elegir un workflow no duplica el ingreso:
                                el dispatcher continúa con el mismo <code>IngressId</code>.
                            </div>
                        </div>
                    </div>

                    <div class="row g-2 align-items-end mb-3">
                        <div class="col-12 col-md-5">
                            <label class="form-label">Buscar</label>
                            <asp:TextBox runat="server" ID="txtBuscar" CssClass="form-control"
                                placeholder="Archivo, canal, IngressId o workflow..." />
                        </div>
                        <div class="col-12 col-md-4">
                            <label class="form-label">Estado</label>
                            <asp:DropDownList runat="server" ID="ddlFiltroEstado" CssClass="form-select">
                                <asp:ListItem Text="Todos" Value="" />
                                <asp:ListItem Text="Pendiente de ruta" Value="PENDIENTE_RUTA" />
                                <asp:ListItem Text="Ruta resuelta" Value="RUTA_RESUELTA" />
                                <asp:ListItem Text="Instancia creada" Value="INSTANCIA_CREADA" />
                                <asp:ListItem Text="En curso" Value="EN_CURSO" />
                                <asp:ListItem Text="Finalizado" Value="FINALIZADO" />
                                <asp:ListItem Text="Error de workflow" Value="ERROR_WORKFLOW" />
                                <asp:ListItem Text="Error de ingreso" Value="ERROR_INGRESO" />
                            </asp:DropDownList>
                        </div>
                        <div class="col-6 col-md-1 d-grid">
                            <asp:Button runat="server" ID="btnBuscar" Text="Buscar" CssClass="btn btn-primary"
                                OnClick="btnBuscar_Click" />
                        </div>
                        <div class="col-6 col-md-2 d-grid">
                            <asp:Button runat="server" ID="btnLimpiar" Text="Limpiar" CssClass="btn btn-outline-secondary"
                                OnClick="btnLimpiar_Click" />
                        </div>
                    </div>

                    <div class="table-responsive">
                        <asp:GridView runat="server" ID="gvIngresos"
                            CssClass="table table-hover ws-table mb-0"
                            AutoGenerateColumns="False" DataKeyNames="Id"
                            OnRowCommand="gvIngresos_RowCommand">
                            <Columns>
                                <asp:BoundField DataField="Id" HeaderText="Id" />
                                <asp:BoundField DataField="FechaIngresoFmt" HeaderText="Ingreso" />
                                <asp:BoundField DataField="CanalCodigo" HeaderText="Canal" />

                                <asp:TemplateField HeaderText="Documento">
                                    <ItemTemplate>
                                        <div class="fw-semibold ws-file"><%# Eval("ArchivoNombre") %></div>
                                        <div class="text-muted small"><%# Eval("IngressId") %></div>
                                    </ItemTemplate>
                                </asp:TemplateField>

                                <asp:TemplateField HeaderText="Estado">
                                    <ItemTemplate>
                                        <span class='<%# EstadoBadgeClass(Eval("Estado")) %>'>
                                            <%# EstadoTexto(Eval("Estado")) %>
                                        </span>
                                        <div class="text-muted small mt-1"><%# Eval("OrigenDecision") %></div>
                                        <div class="text-muted small"><%# Eval("ConfianzaDisplay") %></div>
                                    </ItemTemplate>
                                </asp:TemplateField>

                                <asp:TemplateField HeaderText="Decisión">
                                    <ItemTemplate>
                                        <div class="fw-semibold"><%# Eval("WorkflowDisplay") %></div>
                                        <div class="text-muted small"><%# Eval("RouteDisplay") %></div>
                                        <div class="text-muted small ws-reason"><%# Eval("MotivoDecision") %></div>
                                        <div class="text-muted small"><%# Eval("DecisionPorDisplay") %></div>
                                        <div class="text-danger small ws-reason"><%# Eval("UltimoError") %></div>
                                    </ItemTemplate>
                                </asp:TemplateField>

                                <asp:TemplateField HeaderText="Instancia">
                                    <ItemTemplate>
                                        <asp:HyperLink runat="server" CssClass="btn btn-sm btn-outline-secondary"
                                            NavigateUrl='<%# "WF_Instancias.aspx?inst=" + Eval("WF_InstanciaId") %>'
                                            Visible='<%# HasInstance(Eval("WF_InstanciaId")) %>'>
                                            <%# Eval("WF_InstanciaId") %>
                                        </asp:HyperLink>
                                        <span runat="server" visible='<%# !HasInstance(Eval("WF_InstanciaId")) %>' class="text-muted">—</span>
                                    </ItemTemplate>
                                </asp:TemplateField>

                                <asp:TemplateField HeaderText="Acción">
                                    <ItemTemplate>
                                        <asp:LinkButton runat="server" CssClass="btn btn-sm btn-primary"
                                            CommandName="RESOLVER" CommandArgument='<%# Eval("Id") %>'
                                            Visible='<%# CanResolve(Eval("Estado"), Eval("WF_InstanciaId")) %>'>
                                            Elegir workflow
                                        </asp:LinkButton>
                                    </ItemTemplate>
                                </asp:TemplateField>
                            </Columns>
                            <EmptyDataTemplate>
                                <div class="p-3 text-muted">No hay ingresos con el filtro seleccionado.</div>
                            </EmptyDataTemplate>
                        </asp:GridView>
                    </div>
                </div>
            </div>

            <asp:Panel runat="server" ID="pnlResolver" Visible="false" CssClass="card ws-card mb-4 border border-warning">
                <div class="card-body">
                    <asp:HiddenField runat="server" ID="hfIngresoId" />
                    <div class="d-flex justify-content-between align-items-start gap-3 mb-3">
                        <div>
                            <h5 class="mb-1">Resolver documento pendiente</h5>
                            <div><strong>Archivo:</strong> <asp:Label runat="server" ID="lblResolverArchivo" /></div>
                            <div><strong>Canal:</strong> <asp:Label runat="server" ID="lblResolverCanal" /></div>
                            <div class="ws-muted small mt-1"><asp:Label runat="server" ID="lblResolverMotivo" /></div>
                        </div>
                        <span class="badge bg-warning text-dark">Decisión humana</span>
                    </div>

                    <div class="row g-3 align-items-end">
                        <div class="col-12 col-lg-6">
                            <label class="form-label">Workflow que debe iniciar</label>
                            <asp:DropDownList runat="server" ID="ddlResolverWorkflow" CssClass="form-select" />
                        </div>
                        <div class="col-12 col-lg-4">
                            <label class="form-label">Motivo de la decisión</label>
                            <asp:TextBox runat="server" ID="txtResolverMotivo" CssClass="form-control"
                                placeholder="Ej.: Es una nota de crédito del proveedor..." />
                        </div>
                        <div class="col-6 col-lg-1 d-grid">
                            <asp:Button runat="server" ID="btnAsignarWorkflow" Text="Asignar" CssClass="btn btn-primary"
                                OnClick="btnAsignarWorkflow_Click" />
                        </div>
                        <div class="col-6 col-lg-1 d-grid">
                            <asp:Button runat="server" ID="btnCancelarResolver" Text="Cancelar" CssClass="btn btn-outline-secondary"
                                CausesValidation="false" OnClick="btnCancelarResolver_Click" />
                        </div>
                    </div>
                </div>
            </asp:Panel>

            <asp:Panel runat="server" ID="pnlRulesAdmin" CssClass="card ws-card">
                <div class="card-body">
                    <div class="d-flex flex-column flex-lg-row justify-content-between gap-2 mb-3">
                        <div>
                            <h5 class="mb-1">Reglas determinísticas</h5>
                            <div class="ws-muted small">
                                Primera capa del Enrutador. Compara canal, extensión y patrón de nombre.
                                A igual prioridad gana la regla más específica; destinos incompatibles quedan pendientes.
                            </div>
                        </div>
                        <asp:Button runat="server" ID="btnNuevaRuta" Text="Nueva regla" CssClass="btn btn-outline-primary"
                            OnClick="btnNuevaRuta_Click" />
                    </div>

                    <asp:HiddenField runat="server" ID="hfRutaId" />
                    <div class="row g-3 mb-4">
                        <div class="col-12 col-md-3">
                            <label class="form-label">Código</label>
                            <asp:TextBox runat="server" ID="txtRutaCodigo" CssClass="form-control" placeholder="NC_PROVEEDORES" />
                        </div>
                        <div class="col-12 col-md-5">
                            <label class="form-label">Nombre</label>
                            <asp:TextBox runat="server" ID="txtRutaNombre" CssClass="form-control" placeholder="Notas de crédito de proveedores" />
                        </div>
                        <div class="col-12 col-md-2">
                            <label class="form-label">Canal</label>
                            <asp:TextBox runat="server" ID="txtRutaCanal" CssClass="form-control" placeholder="GENERAL" />
                            <div class="form-hint">Vacío = todos.</div>
                        </div>
                        <div class="col-12 col-md-2">
                            <label class="form-label">Prioridad</label>
                            <asp:TextBox runat="server" ID="txtRutaPrioridad" CssClass="form-control" Text="100" />
                        </div>

                        <div class="col-12 col-md-3">
                            <label class="form-label">Patrón de archivo</label>
                            <asp:TextBox runat="server" ID="txtRutaPatron" CssClass="form-control rule-condition" placeholder="NC_*.pdf" />
                            <div class="form-hint">Comodines: <code>*</code> y <code>?</code>.</div>
                        </div>
                        <div class="col-12 col-md-2">
                            <label class="form-label">Extensión</label>
                            <asp:TextBox runat="server" ID="txtRutaExtension" CssClass="form-control rule-condition" placeholder=".pdf" />
                        </div>
                        <div class="col-12 col-md-5">
                            <label class="form-label">Workflow de destino</label>
                            <asp:DropDownList runat="server" ID="ddlRutaWorkflow" CssClass="form-select" />
                        </div>
                        <div class="col-6 col-md-1 d-flex align-items-end">
                            <div class="form-check mb-2">
                                <asp:CheckBox runat="server" ID="chkRutaActiva" CssClass="form-check-input" Checked="true" />
                                <label class="form-check-label">Activa</label>
                            </div>
                        </div>
                        <div class="col-6 col-md-1 d-grid align-items-end">
                            <asp:Button runat="server" ID="btnGuardarRuta" Text="Guardar" CssClass="btn btn-primary"
                                OnClick="btnGuardarRuta_Click" />
                        </div>
                    </div>

                    <div class="alert alert-light border small">
                        Una regla sin patrón ni extensión funciona como ruta predeterminada del canal.
                        Solo puede existir una predeterminada activa por canal.
                    </div>

                    <div class="table-responsive">
                        <asp:GridView runat="server" ID="gvRutas"
                            CssClass="table table-hover ws-table mb-0"
                            AutoGenerateColumns="False" DataKeyNames="Id"
                            OnRowCommand="gvRutas_RowCommand">
                            <Columns>
                                <asp:BoundField DataField="Codigo" HeaderText="Código" />
                                <asp:BoundField DataField="Nombre" HeaderText="Nombre" />
                                <asp:BoundField DataField="CanalDisplay" HeaderText="Canal" />
                                <asp:BoundField DataField="PatronDisplay" HeaderText="Patrón" />
                                <asp:BoundField DataField="ExtensionDisplay" HeaderText="Extensión" />
                                <asp:BoundField DataField="Prioridad" HeaderText="Prioridad" />
                                <asp:BoundField DataField="WorkflowDisplay" HeaderText="Workflow" />
                                <asp:TemplateField HeaderText="Estado">
                                    <ItemTemplate>
                                        <span class='<%# Convert.ToBoolean(Eval("Activo")) ? "badge bg-success" : "badge bg-secondary" %>'>
                                            <%# Convert.ToBoolean(Eval("Activo")) ? "Activa" : "Inactiva" %>
                                        </span>
                                    </ItemTemplate>
                                </asp:TemplateField>
                                <asp:TemplateField HeaderText="Acciones">
                                    <ItemTemplate>
                                        <div class="d-flex gap-2">
                                            <asp:LinkButton runat="server" CssClass="btn btn-sm btn-outline-primary"
                                                CommandName="EDITAR_RUTA" CommandArgument='<%# Eval("Id") %>'>
                                                Editar
                                            </asp:LinkButton>
                                            <asp:LinkButton runat="server" CssClass="btn btn-sm btn-outline-secondary"
                                                CommandName="TOGGLE_RUTA" CommandArgument='<%# Eval("Id") %>'>
                                                Activar / desactivar
                                            </asp:LinkButton>
                                        </div>
                                    </ItemTemplate>
                                </asp:TemplateField>
                            </Columns>
                            <EmptyDataTemplate>
                                <div class="p-3 text-muted">
                                    No hay reglas. Los documentos quedarán pendientes hasta que un usuario elija el workflow.
                                </div>
                            </EmptyDataTemplate>
                        </asp:GridView>
                    </div>
                </div>
            </asp:Panel>
        </asp:Panel>
    </main>

    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>

