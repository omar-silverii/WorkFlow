<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Tarea_Detalle.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Tarea_Detalle" %>
<%@ Register Src="~/Controls/WsTopbar.ascx" TagPrefix="ws" TagName="Topbar" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow – Tarea humana</title>
    <meta charset="utf-8" />
    <link rel="stylesheet" href="Content/bootstrap.min.css" />
    <style>
        body { background: #f6f7fb; }
        .form-control-plaintext { padding-left: 0; }
        .ws-title { font-weight: 700; letter-spacing: .2px; }
        .ws-muted { color: rgba(0,0,0,.65); }
        .ws-card { border-radius: 12px; box-shadow: 0 8px 22px rgba(0,0,0,.06); }
        .ws-topbar { background: rgba(255,255,255,.9); backdrop-filter: blur(10px); border-bottom: 1px solid rgba(0,0,0,.06); }
        .ws-pill { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: rgba(13,110,253,.10); color: #0d6efd; border: 1px solid rgba(13,110,253,.20); }
    </style>
</head>
<body>
<form id="form1" runat="server">
        <!-- Topbar coherente -->
        <ws:Topbar runat="server" ID="Topbar1" />

        <main class="container-fluid px-3 px-md-4 py-4">

       <div class="ws-topbar p-3 mb-3 ws-card">
          <div class="d-flex align-items-center justify-content-between">
              <div>
                  <div class="ws-title">Tarea humana</div>
                  <div class="ws-muted small">Aceptar o rechazar tares</div>
              </div>
              
          </div>
      </div>

            <asp:ValidationSummary ID="valSummary" runat="server"
                CssClass="text-danger" />

            <asp:Panel ID="pnlDatos" runat="server">
                <asp:Panel ID="pnlPedidosPendientes" runat="server" Visible="false" CssClass="alert alert-warning">
                    <div class="fw-bold mb-1">Observaciones del expediente</div>
                    <div class="small mb-2">
                        Acá se muestra el historial completo de observaciones registradas para esta instancia.
                    </div>
                    <asp:Literal ID="litPedidosPendientes" runat="server" />
                </asp:Panel>

                <a id="adjuntos"></a>

                <!-- Adjuntos solicitados por backtrack (Instancia + Tarea) -->
            <asp:Panel ID="pnlAdjuntos" runat="server" CssClass="card ws-card mb-3">
                <div class="card-body">
                    <div class="d-flex align-items-center justify-content-between mb-2">
                        <div class="fw-bold">Documentación solicitada</div>
                        <span class="ws-pill">Adjuntos</span>
                    </div>

                    <div class="text-muted small mb-3">
                        Adjuntá archivos para responder a lo pedido en el rechazo (se asocian a esta instancia y tarea).
                    </div>

                    <asp:Panel ID="pnlAdjuntosMsg" runat="server" Visible="false" />

                    <div class="row g-2 align-items-end">
                        <div class="col-md-6">
                            <label class="form-label">Archivo</label>
                            <asp:FileUpload ID="fuAdjunto" runat="server" CssClass="form-control" />
                        </div>

                        <div class="col-md-3">
                            <label class="form-label">Tipo (opcional)</label>
                            <asp:TextBox ID="txtAdjTipo" runat="server" CssClass="form-control" placeholder="DNI / Respaldo / ..." />
                        </div>

                        <div class="col-md-3">
                            <asp:Button ID="btnAdjuntar" runat="server"
                                Text="Adjuntar"
                                CssClass="btn btn-outline-primary w-100"
                                CausesValidation="false"
                                OnClick="btnAdjuntar_Click" />
                        </div>
                    </div>

                    <hr class="my-3" />

                    <asp:Panel ID="pnlAdjuntosEmpty" runat="server" CssClass="text-muted small" Visible="true">
                        (Sin adjuntos cargados para esta tarea)
                    </asp:Panel>

                    <asp:Repeater ID="rptAdjuntos" runat="server">
                        <HeaderTemplate>
                            <div class="table-responsive">
                            <table class="table table-sm align-middle mb-0">
                                <thead>
                                    <tr>
                                        <th>Tipo</th>
                                        <th>Archivo</th>
                                        <th>Subido</th>
                                        <th></th>
                                    </tr>
                                </thead>
                                <tbody>
                        </HeaderTemplate>
                        <ItemTemplate>
                            <tr>
                                <td><%# Eval("Tipo") %></td>
                                <td><%# Eval("FileName") %></td>
                                <td><%# Eval("Fecha") %></td>
                                <td class="text-end">
                                    <a class="btn btn-sm btn-outline-secondary"
                                       href="<%# Eval("Url") %>" target="_blank">Ver</a>
                                </td>
                            </tr>
                        </ItemTemplate>
                        <FooterTemplate>
                                </tbody>
                            </table>
                            </div>
                        </FooterTemplate>
                    </asp:Repeater>
                </div>
            </asp:Panel>

                <div class="row mb-2">
                    <div class="col-md-2">
                        <label>Id tarea</label>
                        <asp:Label ID="lblId" runat="server"
                                   CssClass="form-control-plaintext" />
                    </div>
                    <div class="col-md-2">
                        <label>Instancia</label><br />
                        <asp:Label ID="lblInstancia" runat="server"
                                   CssClass="form-control-plaintext" />
                    </div>
                    <div class="col-md-2">
                        <label>Estado</label>
                        <asp:Label ID="lblEstado" runat="server"
                                   CssClass="form-control-plaintext" />
                    </div>
                    <div class="col-md-2">
                        <label>Tipo</label>
                        <asp:Label ID="lblTipo" runat="server"
                                   CssClass="form-control-plaintext" />
                    </div>
                </div>

                <div class="form-group">
                    <label>Título</label>
                    <asp:TextBox ID="txtTitulo" runat="server"
                                 CssClass="form-control" ReadOnly="true" />
                </div>

                <div class="form-group">
                    <label>Descripción</label>
                    <asp:TextBox ID="txtDescripcion" runat="server"
                                 CssClass="form-control"
                                 TextMode="MultiLine"
                                 Rows="3"
                                 ReadOnly="true" />
                </div>

                <div class="row">
                    <div class="col-md-6">
                        <label>Rol destino</label>
                        <asp:TextBox ID="txtRol" runat="server"
                                     CssClass="form-control" ReadOnly="true" />
                    </div>
                    <div class="col-md-6">
                        <label>Usuario asignado</label>
                        <asp:TextBox ID="txtUsuario" runat="server"
                                     CssClass="form-control" ReadOnly="true" />
                    </div>
                </div>

                <hr />

                <div class="row">
                    <div class="col-md-4">
                        <label>Resultado</label>
                        <asp:DropDownList ID="ddlResultado" runat="server"
                                          CssClass="form-control">
                            <asp:ListItem Text="-- elegir --" Value=""></asp:ListItem>
                            <asp:ListItem Text="apto" Value="apto"></asp:ListItem>
                            <asp:ListItem Text="no_apto" Value="no_apto"></asp:ListItem>
                            <asp:ListItem Text="rechazado" Value="rechazado"></asp:ListItem>
                        </asp:DropDownList>
                        <asp:RequiredFieldValidator ID="rfvResultado" runat="server"
                            ControlToValidate="ddlResultado"
                            InitialValue=""
                            ErrorMessage="Seleccione un resultado."
                            CssClass="text-danger"
                            Display="Dynamic" />
                    </div>
                </div>

                <div class="form-group mt-2">
                    <label>Observaciones</label>
                    <asp:TextBox ID="txtObs" runat="server"
                                 CssClass="form-control"
                                 TextMode="MultiLine"
                                 Rows="4" />
                </div>

                <div class="mt-3">
                    <asp:Button ID="btnCompletar" runat="server"
                                Text="Completar tarea"
                                CssClass="btn btn-primary"
                                OnClick="btnCompletar_Click" />
                    <asp:Button ID="btnVolver" runat="server"
                                Text="Volver"
                                CssClass="btn btn-secondary ms-2"
                                CausesValidation="false"
                                OnClick="btnVolver_Click" />
                </div>

                <asp:Label ID="lblInfo" runat="server"
                           CssClass="text-success mt-3 d-block" />
            </asp:Panel>

            <asp:Panel ID="pnlError" runat="server"
                       Visible="false"
                       CssClass="alert alert-danger mt-3">
                <asp:Literal ID="litError" runat="server" />
            </asp:Panel>
       
    </main>
    <script src="Scripts/bootstrap.bundle.min.js"></script>
</form>
</body>
</html>