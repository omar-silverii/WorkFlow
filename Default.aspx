<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms._Default" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>EDIsa - Workflow Studio</title>

    <!-- Bootstrap 5 (local) -->
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <style>
         body { background: #f6f7fb; }
         .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
         .ws-card .card-body { padding: 20px; }
         .ws-kpi { border-radius: 16px; border: 1px solid rgba(0,0,0,.08); background: #fff; }
         .ws-muted { color: rgba(0,0,0,.65); }
         .ws-icon { font-size: 28px; line-height: 1; }
         .ws-link { text-decoration: none; }
         .ws-link:hover { text-decoration: underline; }
         .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
         .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
         .ws-section-title { font-weight: 700; }
     </style>

</head>

<body>
    <form id="form1" runat="server">
      
     <!-- Topbar coherente -->
     <ws:Topbar runat="server" ID="Topbar1" />

        <!-- Main -->
        <main class="container-fluid px-3 px-md-4 py-4">

            <!-- Header -->
            <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-4">
                <div>
                    <h3 class="mb-1 ws-section-title">Inicio</h3>
                    <div class="ws-muted">Diseñá workflows, administrá documentos y revisá ejecuciones — todo desde un solo lugar.</div>
                </div>
                
            </div>

            <div class="row g-3 g-md-4">

                <!-- Left: Accesos rápidos -->
                <div class="col-12 col-lg-3">
                    <div class="ws-kpi p-3">
                        <div class="d-flex align-items-center justify-content-between mb-2">
                            <div class="fw-semibold">Accesos rápidos</div>
                            <span class="ws-muted" style="font-size:12px;">Atajos</span>
                        </div>

                        <div class="list-group list-group-flush">
                            <a class="list-group-item list-group-item-action px-0" href="WorkflowUI.aspx">🔀 Editor de Workflow</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_Definiciones.aspx">📋 Definiciones</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_Tarea.aspx">🧑‍💻 Mis tareas</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_Gerente_Tareas.aspx">🧑‍💼 Tareas (Gerencia)</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_Instancias.aspx">▶ Ejecuciones (Instancias)</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_Entidades.aspx">🧾 Entidades (Casos)</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_DocTipo.aspx">📁 Tipos de Documento</a>
                            <a class="list-group-item list-group-item-action px-0" href="WF_DocTipoReglas.aspx">🧠 Reglas Extract</a>
                        </div>
                    </div>

                    <div class="ws-kpi p-3 mt-3">
                        <div class="fw-semibold mb-2">Guía rápida</div>
                        <ol class="mb-0 ws-muted" style="padding-left:18px;">
                            <li>Definí el <b>DocTipo</b> del documento.</li>
                            <li>Cargá las <b>Reglas</b> de extracción.</li>
                            <li>Diseñá el <b>Workflow</b> en el editor.</li>
                            <li>Ejecutá y revisá <b>ejecuciones</b> (instancias/logs).</li>
                            <li>Resolvé <b>tus tareas</b> (bandeja del usuario).</li>
                            <li>Supervisá <b>tareas</b> (gerencia).</li>
                            <li>Asociá <b>documentos</b> al caso (integración DMS).</li>
                        </ol>
                    </div>
                </div>

                <!-- Center/Right: Cards principales -->
                <div class="col-12 col-lg-9">
                    <div class="row g-3 g-md-4">

                        <!-- Card 1 -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🔀</div>
                                    <h5 class="mt-2 mb-1">Crear / Editar Workflow</h5>
                                    <div class="ws-muted">
                                        Diseñá flujos con nodos (Start/If/Http/SQL/DocTipo/Extract, etc.) y guardalos en SQL.
                                    </div>
                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WorkflowUI.aspx">Abrir editor</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 2 -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">📋</div>
                                    <h5 class="mt-2 mb-1">Definiciones de Workflow</h5>
                                    <div class="ws-muted">
                                        Listado de workflows guardados. Consultá el JSON, versioná, analizá estructura
                                        y reutilizá definiciones existentes.
                                    </div>
                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Definiciones.aspx">Abrir definiciones</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 3: Entidades -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧾</div>
                                    <h5 class="mt-2 mb-1">Entidades (Casos)</h5>
                                    <div class="ws-muted mb-3">
                                        Seguimiento global de casos orientados a proceso.
                                    </div>

                                    <div class="row text-center mb-3">

                                        <div class="col-3">
                                            <div class="fw-bold fs-5">
                                                <asp:Label ID="lblEntTotal" runat="server" Text="0" />
                                            </div>
                                            <div class="ws-muted small">Total</div>
                                        </div>

                                        <div class="col-3">
                                            <div class="fw-bold text-primary">
                                                <asp:Label ID="lblEntIniciado" runat="server" Text="0" />
                                            </div>
                                            <div class="ws-muted small">Iniciado</div>
                                        </div>

                                        <div class="col-3">
                                            <div class="fw-bold text-success">
                                                <asp:Label ID="lblEntFinalizado" runat="server" Text="0" />
                                            </div>
                                            <div class="ws-muted small">Finalizado</div>
                                        </div>

                                        <div class="col-3">
                                            <div class="fw-bold text-danger">
                                                <asp:Label ID="lblEntError" runat="server" Text="0" />
                                            </div>
                                            <div class="ws-muted small">Error</div>
                                        </div>

                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Entidades.aspx">
                                            Abrir entidades
                                        </a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 4: Mis tareas -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧑‍💻</div>
                                    <h5 class="mt-2 mb-1">Mis tareas</h5>
                                    <div class="ws-muted mb-3">
                                        Bandeja del usuario logueado. Tareas pendientes, resolución y seguimiento del trabajo diario.
                                    </div>

                                    <div class="row text-center mb-3">
                                        <div class="col-4">
                                            <div class="fw-bold fs-5" id="wsTaskTotalCount">0</div>
                                            <div class="ws-muted small">Pendientes</div>
                                        </div>

                                        <div class="col-4">
                                            <div class="fw-bold text-danger" id="wsTaskBackCount">0</div>
                                            <div class="ws-muted small">Back</div>
                                        </div>

                                        <div class="col-4">
                                            <div class="fw-bold text-primary" id="wsTaskPendingCount">0</div>
                                            <div class="ws-muted small">En bandeja</div>
                                        </div>
                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Tareas.aspx">Abrir mis tareas</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 5: Gerencia de tareas -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧑‍💼</div>
                                    <h5 class="mt-2 mb-1">Gerencia de tareas</h5>
                                    <div class="ws-muted">
                                        Bandeja de supervisión. Asignación/seguimiento de tareas y control por workflow.
                                    </div>
                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Gerente_Tareas.aspx">Ver tareas</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 6 -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">📄</div>
                                    <h5 class="mt-2 mb-1">Administrar Documentos</h5>
                                    <div class="ws-muted">
                                        Catálogo de documentos de la empresa (DocTipo) y reglas de extracción asociadas (sin tocar regex en el editor).
                                    </div>
                                    <div class="d-flex gap-2 mt-3 flex-wrap">
                                        <a class="btn btn-primary" href="WF_DocTipo.aspx">Tipos de documento</a>
                                        <a class="btn btn-outline-secondary" href="WF_DocTipoReglas.aspx">Reglas de extracción</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 7: Actividad documental -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧾</div>
                                    <h5 class="mt-2 mb-1">Actividad documental</h5>
                                    <div class="ws-muted">
                                        Adjuntos y movimientos registrados por el motor (auditoría documental).
                                    </div>

                                    <div class="d-flex gap-3 mt-3 flex-wrap">
                                        <div class="ws-muted" style="font-size:12px;">
                                            <div class="fw-semibold text-dark">
                                                <asp:Label ID="lblDocCount48" runat="server" Text="0"></asp:Label>
                                            </div>
                                            Docs (48h)
                                        </div>

                                        <div class="ws-muted" style="font-size:12px;">
                                            <div class="fw-semibold text-dark">
                                                <asp:Label ID="lblInstCount48" runat="server" Text="0"></asp:Label>
                                            </div>
                                            Instancias (48h)
                                        </div>
                                    </div>

                                    <div class="mt-3">
                                        <asp:Repeater ID="rptDocLast" runat="server">
                                            <HeaderTemplate>
                                                <div class="list-group">
                                            </HeaderTemplate>

                                            <ItemTemplate>
                                                <div class="list-group-item d-flex justify-content-between align-items-center">
                                                    <div>
                                                        <div class="fw-semibold">
                                                            <%# Eval("Tipo") %>
                                                            <span class="text-muted small">docId: <%# Eval("DocumentoId") %></span>
                                                        </div>
                                                        <div class="text-muted small">
                                                            Instancia: <%# Eval("WF_InstanciaId") %> · <%# Eval("FechaAltaFmt") %>
                                                        </div>
                                                    </div>

                                                    <div class="d-flex gap-2">
                                                        <a class="btn btn-sm btn-outline-secondary"
                                                           href='WF_Instancias.aspx?inst=<%# Eval("WF_InstanciaId") %>'>
                                                            Ver
                                                        </a>
                                                    </div>
                                                </div>
                                            </ItemTemplate>

                                            <FooterTemplate>
                                                </div>
                                            </FooterTemplate>
                                        </asp:Repeater>

                                        <asp:Panel ID="pnlDocLastEmpty" runat="server" Visible="false" CssClass="ws-muted small">
                                            Sin actividad documental en las últimas 48 horas.
                                        </asp:Panel>
                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-outline-secondary" href="WF_Instancias.aspx">Ver ejecuciones</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 8 (placeholder futuro): Configuración / Catálogos -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧩</div>
                                    <h5 class="mt-2 mb-1">Configuración / Catálogos</h5>
                                    <div class="ws-muted">
                                        Espacio para crecer (conexiones, parámetros globales, plantillas, colas, etc.).
                                    </div>
                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Seguridad.aspx" tabindex="-1" aria-disabled="true">Seguridad</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                    </div>

                    <!-- Footer mini -->
                    <div class="mt-4 ws-muted" style="font-size:12px;">
                        Workflow Studio • UI WebForms + SQL • Diseño y creación por EDI-SA&reg;
                    </div>
                </div>
            </div>                        


        </main>

        <!-- Bootstrap 5 (local) -->
        <script src="Scripts/bootstrap.bundle.min.js"></script>
    </form>
</body>
</html>
