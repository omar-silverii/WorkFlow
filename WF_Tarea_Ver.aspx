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

                        <hr class="my-3" />

                        <div>
                            <div class="ws-muted mb-2">Documentos del caso</div>

                            <asp:Panel ID="pnlDocs" runat="server" Visible="false">
                                <div class="list-group">
                                    <asp:Repeater ID="rptDocs" runat="server">
                                        <ItemTemplate>
                                            <div class="list-group-item d-flex justify-content-between align-items-center">
                                                <div>
                                                    <div class="fw-semibold">
                                                        <%# Eval("Tipo") %>
                                                        <span class="text-muted small">docId: <%# Eval("DocumentoId") %></span>
                                                    </div>
                                                    <div class="text-muted small">
                                                        carpetaId: <%# Eval("CarpetaId") %> | ficheroId: <%# Eval("FicheroId") %>
                                                        <%# string.IsNullOrWhiteSpace(Convert.ToString(Eval("Scope"))) ? "" : (" | " + Eval("Scope")) %>
                                                    </div>
                                                </div>
                                                <asp:HyperLink runat="server" CssClass="btn btn-sm btn-outline-primary"
                                                    NavigateUrl='<%# Eval("ViewerUrl") %>' Target="_blank"
                                                    Visible='<%# !string.IsNullOrWhiteSpace(Convert.ToString(Eval("ViewerUrl"))) %>'>
                                                    Ver (visor)
                                                </asp:HyperLink>
                                            </div>
                                        </ItemTemplate>
                                    </asp:Repeater>
                                </div>
                            </asp:Panel>

                            <asp:Panel ID="pnlDocsEmpty" runat="server" Visible="true" CssClass="ws-muted small">
                                (Sin documentos asociados)
                            </asp:Panel>
                        </div>

                            <div class="mt-3 p-3 rounded-3 border bg-white">
  <div class="d-flex justify-content-between align-items-center mb-2">
    <div><b>Auditoría documental</b></div>
    <span class="badge text-bg-secondary">WF_InstanciaDocumento</span>
  </div>

  <asp:Panel ID="pnlDocAudit" runat="server" Visible="false">
      <div class="d-flex align-items-center gap-2 mb-2">
  <asp:CheckBox ID="chkDocAuditDedup" runat="server" AutoPostBack="true"
    OnCheckedChanged="chkDocAuditDedup_CheckedChanged" />
  <span class="small text-muted">Mostrar deduplicado (último por documento/scope)</span>
</div>

    <asp:GridView ID="gvDocAudit" runat="server"
      CssClass="table table-sm table-hover align-middle"
      AutoGenerateColumns="false"
      GridLines="None">
      <Columns>
        <asp:BoundField DataField="FechaAlta" HeaderText="Fecha" DataFormatString="{0:dd/MM/yyyy HH:mm:ss}" />
        <asp:BoundField DataField="Accion" HeaderText="Acción" />
        <asp:BoundField DataField="Scope" HeaderText="Scope" />
        <asp:BoundField DataField="NodoTipo" HeaderText="Nodo" />
        <asp:BoundField DataField="Tipo" HeaderText="Tipo" />
        <asp:BoundField DataField="DocumentoId" HeaderText="DocumentoId" />

        <asp:TemplateField HeaderText="Visor">
          <ItemTemplate>
            <asp:HyperLink runat="server"
              NavigateUrl='<%# Eval("ViewerUrl") %>'
              Text="Ver (visor)"
              Target="_blank"
              Visible='<%# !String.IsNullOrWhiteSpace(Convert.ToString(Eval("ViewerUrl"))) %>'
              CssClass="btn btn-sm btn-outline-primary" />
          </ItemTemplate>
        </asp:TemplateField>

        <asp:TemplateField HeaderText="Índices">
          <ItemTemplate>
            <span class="text-muted" style="font-size:12px;">
              <%# Server.HtmlEncode(Convert.ToString(Eval("IndicesJson"))) %>
            </span>
          </ItemTemplate>
        </asp:TemplateField>
      </Columns>
    </asp:GridView>
  </asp:Panel>

  <asp:Panel ID="pnlDocAuditEmpty" runat="server" Visible="true" CssClass="ws-muted">
    Sin auditoría documental para esta instancia.
  </asp:Panel>
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

