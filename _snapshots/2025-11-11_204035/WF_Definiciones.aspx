<%@ Page Language="C#" Async="true" AutoEventWireup="true" CodeBehind="WF_Definiciones.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Definiciones" %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflows - Definiciones</title>
    <meta charset="utf-8" />
    <!-- Intranet: sin llamadas externas -->
    <link rel="stylesheet" href="Scripts/bootstrap.min.css" />
    <style>
        body { padding: 12px; }
        .grid-small td, .grid-small th { padding: 4px 6px; font-size: .82rem; }
        pre.json-view { max-height: 280px; overflow: auto; background: #f8f9fa; border: 1px solid #dee2e6; padding: 6px; font-size: .7rem; }
    </style>
</head>
<body>
<form id="form1" runat="server">
    <div class="container-fluid">
        <h4 class="mb-3">Workflows – Definiciones</h4>

        <div class="form-inline mb-2">
            <label class="mr-2">Filtrar por código:</label>
            <asp:TextBox ID="txtFiltro" runat="server" CssClass="form-control form-control-sm mr-2" />
            <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn btn-sm btn-primary mr-2" OnClick="btnBuscar_Click" />
            <asp:Button ID="btnLimpiar" runat="server" Text="Limpiar" CssClass="btn btn-sm btn-secondary mr-2" OnClick="btnLimpiar_Click" />
            <asp:Button ID="btnNuevo" runat="server" Text="Nuevo" CssClass="btn btn-sm btn-success" OnClick="btnNuevo_Click" />
        </div>

        <asp:GridView ID="gvDef" runat="server"
            CssClass="table table-sm table-bordered table-striped grid-small"
            AutoGenerateColumns="False"
            DataKeyNames="Id"
            AllowPaging="True"
            PageSize="20"
            OnPageIndexChanging="gvDef_PageIndexChanging"
            OnRowCommand="gvDef_RowCommand">
            <Columns>
                <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="40px" />
                <asp:BoundField DataField="Codigo" HeaderText="Código" />
                <asp:BoundField DataField="Nombre" HeaderText="Nombre" />
                <asp:BoundField DataField="Version" HeaderText="Ver" ItemStyle-Width="40px" />
                <asp:CheckBoxField DataField="Activo" HeaderText="Activo" ItemStyle-Width="55px" />
                <asp:BoundField DataField="FechaCreacion" HeaderText="Creado" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                <asp:BoundField DataField="CreadoPor" HeaderText="Creado por" />
                <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="240px">
                    <ItemTemplate>
                        <!-- Editar en el editor conservando posiciones -->
                        <asp:HyperLink ID="lnkEditar" runat="server"
                            CssClass="btn btn-sm btn-primary mr-1"
                            NavigateUrl='<%# "WorkflowUI.aspx?defId=" + Eval("Id") %>'>Editar</asp:HyperLink>

                        <!-- Ver JSON -->
                        <asp:LinkButton ID="lnkVerJson" runat="server" CommandName="VerJson" CommandArgument='<%# Eval("Id") %>'
                            CssClass="btn btn-sm btn-info mr-1">JSON</asp:LinkButton>

                        <!-- Ir a instancias -->
                        <asp:LinkButton ID="lnkVerInst" runat="server" CommandName="VerInst" CommandArgument='<%# Eval("Id") %>'
                            CssClass="btn btn-sm btn-secondary mr-1">Instancias</asp:LinkButton>
                    </ItemTemplate>
                </asp:TemplateField>
            </Columns>
        </asp:GridView>

        <!-- panel JSON -->
        <asp:Panel ID="pnlJson" runat="server" Visible="false" CssClass="mt-3">
            <h6>JSON de la definición seleccionada</h6>
            <pre id="preJson" runat="server" class="json-view"></pre>
        </asp:Panel>
    </div>
</form>
</body>
</html>
