<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Instancias.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Instancias"  %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Instancias</title>
    <meta charset="utf-8" />
    <link rel="stylesheet" href="https://maxcdn.bootstrapcdn.com/bootstrap/4.5.2/css/bootstrap.min.css" />
    <style>
        body { padding: 12px; }
        pre.log-view { max-height: 220px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="container-fluid">
            <div class="d-flex justify-content-between align-items-center mb-2">
                <h4 class="mb-0">Workflows – Instancias</h4>
                <asp:HyperLink ID="lnkBack" runat="server" NavigateUrl="WF_Definiciones.aspx" CssClass="btn btn-sm btn-secondary">← Volver a definiciones</asp:HyperLink>
            </div>

            <div class="form-inline mb-2">
                <label class="mr-2">Definición:</label>
                <asp:DropDownList ID="ddlDef" runat="server" CssClass="form-control form-control-sm mr-2" AutoPostBack="true" OnSelectedIndexChanged="ddlDef_SelectedIndexChanged" />
                <asp:Button ID="btnRefrescar" runat="server" Text="Refrescar" CssClass="btn btn-sm btn-primary mr-2" OnClick="btnRefrescar_Click" />
                <!-- NUEVO: crear una instancia dummy de la definición seleccionada -->
                <asp:Button ID="btnCrearInst" runat="server" Text="Crear instancia (prueba)" CssClass="btn btn-sm btn-success" OnClick="btnCrearInst_Click" />
            </div>

            <asp:GridView ID="gvInst" runat="server"
                CssClass="table table-sm table-bordered table-striped"
                AutoGenerateColumns="False"
                AllowPaging="True"
                PageSize="20"
                DataKeyNames="Id"
                OnPageIndexChanging="gvInst_PageIndexChanging"
                OnRowCommand="gvInst_RowCommand">
                <Columns>
                    <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="60px" />
                    <asp:BoundField DataField="Estado" HeaderText="Estado" />
                    <asp:BoundField DataField="FechaInicio" HeaderText="Inicio" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:BoundField DataField="FechaFin" HeaderText="Fin" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="130px">
                        <ItemTemplate>
                            <asp:LinkButton ID="lnkVerDatos" runat="server" CommandName="VerDatos" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-info mr-1">Datos</asp:LinkButton>
                            <asp:LinkButton ID="lnkVerLog" runat="server" CommandName="VerLog" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-secondary">Log</asp:LinkButton>
                            <asp:LinkButton ID="lnkRetry" runat="server" CommandName="Reejecutar" CommandArgument='<%# Eval("Id") %>' CssClass="btn btn-sm btn-warning">Re-ejecutar</asp:LinkButton>
                        </ItemTemplate>
                    </asp:TemplateField>
                </Columns>
            </asp:GridView>

            <!-- panel datos / log -->
            <asp:Panel ID="pnlDetalle" runat="server" Visible="false" CssClass="mt-3">
                <h6 id="lblTituloDetalle" runat="server">Detalle</h6>
                <pre id="preDetalle" runat="server" class="log-view"></pre>
            </asp:Panel>
        </div>
    </form>
</body>
</html>
