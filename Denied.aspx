<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Denied.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.Denied" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html>
<head runat="server">
    <title>Acceso denegado</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <link href="Content/Site.css" rel="stylesheet" />
    <style>
        body { background: #f6f7fb; }
        .ws-card { border: 0; border-radius: 16px; box-shadow: 0 10px 24px rgba(16,24,40,.06); }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
        .ws-hero { max-width: 860px; }
        .ws-code { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace; }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <ws:Topbar runat="server" ID="Topbar1" />

    <main class="container-fluid px-3 px-md-4 py-4">
        <div class="container ws-hero">
            <div class="d-flex align-items-center gap-2 mb-3">
                <span class="ws-pill">403</span>
                <h3 class="mb-0">Acceso denegado</h3>
            </div>

            <div class="card ws-card">
                <div class="card-body">
                    <p class="ws-muted mb-3">
                        Tu usuario no tiene permisos para acceder a esta pantalla.
                    </p>

                    <div class="row g-2 mb-3">
                        <div class="col-12 col-md-6">
                            <div class="p-3 border rounded-3 bg-light">
                                <div class="fw-semibold mb-1">Usuario</div>
                                <div class="ws-code">
                                    <asp:Literal runat="server" ID="litUser" />
                                </div>
                            </div>
                        </div>
                        <div class="col-12 col-md-6">
                            <div class="p-3 border rounded-3 bg-light">
                                <div class="fw-semibold mb-1">Recurso</div>
                                <div class="ws-code">
                                    <asp:Literal runat="server" ID="litPath" />
                                </div>
                            </div>
                        </div>
                    </div>

                    <div class="d-flex gap-2 flex-wrap">
                        <a href="Default.aspx" class="btn btn-primary">Ir al inicio</a>
                        <a href="Login.aspx" class="btn btn-outline-secondary">Cambiar usuario</a>
                        <a href="WF_Gerente_Tareas.aspx" class="btn btn-outline-primary">Ir a bandejas</a>
                    </div>

                    <hr class="my-4" />

                    <div class="ws-muted small">
                        Si creés que esto es un error, pedile a un usuario <span class="fw-semibold">ADMIN</span> que te asigne el rol/permisos necesarios desde <span class="fw-semibold">Seguridad</span>.
                    </div>
                </div>
            </div>
        </div>
    </main>

    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>