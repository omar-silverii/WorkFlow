<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="WF_Tareas.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.WF_Tareas" %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow – Tareas humanas</title>
    <meta charset="utf-8" />
    <link rel="stylesheet" href="Content/bootstrap.min.css" />
    <style>
        body { padding: 12px; }
        .grid-small td, .grid-small th { padding: 4px 6px; font-size: .82rem; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <div class="container-fluid">
            <h4 class="mb-3">Workflow – Tareas humanas</h4>

            <div class="form-inline mb-2">
                <label class="mr-2">Texto a buscar (título / descripción / rol / usuario):</label>
                <asp:TextBox ID="txtFiltro" runat="server" CssClass="form-control form-control-sm mr-2" />
                <asp:CheckBox ID="chkSoloPendientes" runat="server" CssClass="mr-1" Checked="true" />
                <label class="mr-2">Sólo pendientes</label>
                <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn btn-sm btn-primary mr-2"
                            OnClick="btnBuscar_Click" />
                <asp:Button ID="btnLimpiar" runat="server" Text="Limpiar" CssClass="btn btn-sm btn-secondary"
                            OnClick="btnLimpiar_Click" />
            </div>

            <asp:GridView ID="gvTareas" runat="server"
                CssClass="table table-sm table-bordered table-striped grid-small"
                AutoGenerateColumns="False"
                DataKeyNames="Id"
                AllowPaging="True"
                PageSize="20"
                OnPageIndexChanging="gvTareas_PageIndexChanging">
                <Columns>
                    <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="50px" />
                    <asp:BoundField DataField="WF_InstanciaId" HeaderText="Instancia" ItemStyle-Width="70px" />
                    <asp:BoundField DataField="NodoId" HeaderText="Nodo" ItemStyle-Width="60px" />
                    <asp:BoundField DataField="NodoTipo" HeaderText="Tipo" ItemStyle-Width="90px" />
                    <asp:BoundField DataField="Titulo" HeaderText="Título" />
                    <asp:BoundField DataField="RolDestino" HeaderText="Rol destino" />
                    <asp:BoundField DataField="UsuarioAsignado" HeaderText="Usuario" />
                    <asp:BoundField DataField="Estado" HeaderText="Estado" ItemStyle-Width="80px" />
                    <asp:BoundField DataField="FechaCreacion" HeaderText="Creación" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:BoundField DataField="FechaVencimiento" HeaderText="Vence" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
                    <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="80px">
                         <ItemTemplate>
                            <asp:HyperLink ID="lnkAbrir" runat="server"
                                CssClass="btn btn-sm btn-primary"
                                NavigateUrl='<%# "WF_Tarea_Detalle.aspx?id=" + Eval("Id") %>'>
                                Abrir
                            </asp:HyperLink>
                        </ItemTemplate>
                    </asp:TemplateField>
                </Columns>
            </asp:GridView>
        </div>
    </form>
</body>
</html>

