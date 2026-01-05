<%@ Page Language="C#" AutoEventWireup="true" Async="true" CodeBehind="WF_Tarea_Ver.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Tarea_Ver" %>
<!DOCTYPE html>
<html lang="es">
<head runat="server">
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Workflow Studio - Tarea</title>
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
    <style>
        body { background:#f6f7fb; }
        .ws-card { border:0; border-radius:16px; box-shadow:0 10px 24px rgba(16,24,40,.06); }
        .ws-muted { color:rgba(0,0,0,.65); }
        .ws-topbar { background:rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom:1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size:12px; padding:4px 10px; border-radius:999px; background:rgba(13,110,253,.10); color:#0d6efd; border:1px solid rgba(13,110,253,.20); }
    </style>
</head>
<body>
<form id="form1" runat="server">

    <nav class="navbar ws-topbar sticky-top">
        <div class="container-fluid px-3 px-md-4">
            <a class="navbar-brand fw-bold" href="Default.aspx">Workflow Studio <span class="ws-pill ms-2">Intranet</span></a>

            <div class="ms-auto d-flex align-items-center gap-2">
                <span class="ws-muted">Usuario:</span>
                <span class="badge text-bg-primary" id="lblUser" runat="server" clientidmode="Static"></span>
            </div>
        </div>
    </nav>

    <main class="container-fluid px-3 px-md-4 py-4">

        <div class="d-flex align-items-center justify-content-between mb-3">
            <div>
                <h3 class="mb-1">Tarea</h3>
                <div class="ws-muted">Revisá el detalle y tomá acción.</div>
            </div>
            <div class="d-flex gap-2">
                <a class="btn btn-outline-secondary" href="WF_Gerente_Tareas.aspx">Volver</a>
            </div>
        </div>

        <asp:Literal ID="litMsg" runat="server" />

        <div class="card ws-card">
            <div class="card-body">

                <div class="row g-3">
                    <div class="col-12 col-lg-8">
                        <div class="mb-2"><span class="ws-muted">Título</span></div>
                        <h5 class="mb-3"><asp:Label ID="lblTitulo" runat="server" /></h5>

                        <div class="mb-2"><span class="ws-muted">Descripción</span></div>
                        <div class="p-3 rounded-3 border bg-light">
                            <asp:Label ID="lblDesc" runat="server" />
                        </div>
                    </div>

                    <div class="col-12 col-lg-4">
                        <div class="p-3 rounded-3 border bg-white">
                            <div class="d-flex justify-content-between">
                                <span class="ws-muted">TareaId</span>
                                <b><asp:Label ID="lblTareaId" runat="server" /></b>
                            </div>
                            <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Instancia</span>
                                <b><asp:Label ID="lblInstanciaId" runat="server" /></b>
                            </div>                            
                            <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Rol</span>
                                <b><asp:Label ID="lblRol" runat="server" /></b>
                            </div>
                            <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Estado</span>
                                <b><asp:Label ID="lblEstado" runat="server" /></b>
                            </div>
                            <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Resultado</span>
                                <b><asp:Label ID="lblResultado" runat="server" /></b>
                            </div>
                            <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Vence</span>
                                <b><asp:Label ID="lblVence" runat="server" /></b>
                            </div>
                                <div class="d-flex justify-content-between mt-2">
                                <span class="ws-muted">Cerrada</span>
                                <b><asp:Label ID="lblCerrada" runat="server" /></b>
                            </div>
                        </div>

                        <div class="mt-3">
                            <label class="form-label ws-muted">Observaciones</label>
                            <asp:TextBox ID="txtObs" runat="server" CssClass="form-control" TextMode="MultiLine" Rows="4" />
                        </div>

                        <div class="d-flex gap-2 mt-3">
                            <asp:Button ID="btnAprobar" runat="server" CssClass="btn btn-success w-100" Text="Aprobar" OnClick="btnAprobar_Click" />
                            <asp:Button ID="btnRechazar" runat="server" CssClass="btn btn-danger w-100" Text="Rechazar" OnClick="btnRechazar_Click" />
                        </div>

                        <div class="mt-2 ws-muted" style="font-size:12px;">
                            * La acción reanuda la instancia en el motor sobre la misma definición.
                        </div>
                    </div>
                </div>

            </div>
        </div>
    </main>

    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>

