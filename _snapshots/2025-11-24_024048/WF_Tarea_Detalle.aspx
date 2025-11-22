<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Tarea_Detalle.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Tarea_Detalle" %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow – Tarea humana</title>
    <meta charset="utf-8" />
    <link rel="stylesheet" href="Content/bootstrap.min.css" />
    <style>
        body { padding: 12px; }
        .form-control-plaintext { padding-left: 0; }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <div class="container-fluid">
        <h4 class="mb-3">Workflow – Tarea humana</h4>

        <asp:ValidationSummary ID="valSummary" runat="server"
            CssClass="text-danger" />

        <asp:Panel ID="pnlDatos" runat="server">
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
                            CssClass="btn btn-secondary ml-2"
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
    </div>
</form>
</body>
</html>
