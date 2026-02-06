<%@ Page Language="C#" AutoEventWireup="true" %>
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
                        <li class="nav-item"><a class="nav-link active" href="Default.aspx">Inicio</a></li>

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
                            <a class="nav-link" href="WF_Definiciones.aspx">Administración</a>
                        </li>
                    </ul>
                </div>
            </div>
        </nav>

        <!-- Main -->
        <main class="container-fluid px-3 px-md-4 py-4">

            <!-- Header -->
            <div class="d-flex flex-column flex-md-row align-items-start align-items-md-center justify-content-between gap-2 mb-4">
                <div>
                    <h3 class="mb-1 ws-section-title">Inicio</h3>
                    <div class="ws-muted">Diseñá workflows, administrá documentos y revisá ejecuciones — todo desde un solo lugar.</div>
                </div>
                <div class="d-flex gap-2">
                    <a class="btn btn-primary" href="WorkflowUI.aspx">➕ Nuevo Workflow</a>
                    <a class="btn btn-outline-secondary" href="WF_Instancias.aspx">▶ Ver ejecuciones</a>
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
                            <a class="list-group-item list-group-item-action px-0" href="WF_Instancias.aspx">▶ Instancias</a>
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
                            <li>Ejecutá y revisá <b>instancias/logs</b>.</li>
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
                                    <div class="d-flex align-items-start justify-content-between">
                                        <div>
                                            <div class="ws-icon">🔀</div>
                                            <h5 class="mt-2 mb-1">Crear / Editar Workflow</h5>
                                            <div class="ws-muted">
                                                Diseñá flujos con nodos (Start/If/Http/SQL/DocTipo/Extract, etc.) y guardalos en SQL.
                                            </div>
                                        </div>
                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WorkflowUI.aspx">Abrir editor</a>
                                        <a class="btn btn-outline-secondary" href="WF_Definiciones.aspx">Ver definiciones</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 2 -->
                        <!-- Card: Definiciones de Workflow -->
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
                                        <a class="btn btn-outline-secondary" href="WorkflowUI.aspx">Crear nuevo</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 3 -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">▶️</div>
                                    <h5 class="mt-2 mb-1">Ejecuciones (Instancias)</h5>
                                    <div class="ws-muted">
                                        Seguimiento de instancias ejecutadas, re-ejecución y logs por paso.
                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-primary" href="WF_Instancias.aspx">Abrir instancias</a>
                                        <a class="btn btn-outline-secondary" href="WF_Instancias.aspx#logs">Ver logs</a>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Card 4 -->
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

                      

                        <!-- Card 5 (placeholder futuro) -->
                        <div class="col-12 col-md-6">
                            <div class="card ws-card h-100">
                                <div class="card-body">
                                    <div class="ws-icon">🧩</div>
                                    <h5 class="mt-2 mb-1">Configuración / Catálogos</h5>
                                    <div class="ws-muted">
                                        Espacio para crecer (conexiones, parámetros globales, plantillas, colas, etc.).
                                    </div>

                                    <div class="d-flex gap-2 mt-3">
                                        <a class="btn btn-outline-secondary disabled" href="#" tabindex="-1" aria-disabled="true">Próximamente</a>
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

                <!-- Footer mini -->
                <div class="mt-4 ws-muted" style="font-size:12px;">
                    Workflow Studio • UI WebForms + SQL • Diseño y creación por EDI-SA&reg;
                </div>
          


        </main>

        <!-- Bootstrap 5 (local) -->
        <script src="Scripts/bootstrap.bundle.min.js"></script>
    </form>
</body>
</html>

