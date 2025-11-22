<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Poliza_Bandeja.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.Poliza_Bandeja" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
  <meta charset="utf-8" />
  <title>Bandeja de pólizas</title>
  <style>
    body{font-family:Segoe UI,Arial; margin:12px}
    .row{margin-bottom:8px}
    table{border-collapse:collapse; width:100%}
    th,td{border:1px solid #ddd; padding:6px; font-size:13px}
    th{background:#f5f5f5; text-align:left}
    .btn{padding:4px 8px; font-size:12px; border:1px solid #bbb; background:#fff; border-radius:6px; cursor:pointer}
    .btn:hover{background:#f0f0f0}
    input[type=text]{padding:4px 6px; font-size:13px}
  </style>
</head>
<body>
<form id="form1" runat="server">
  <h3>Bandeja de pólizas</h3>

  <div class="row">
    <span>Filtro por número:</span>
    <asp:TextBox ID="txtFiltro" runat="server" />
    <asp:Button ID="btnBuscar" runat="server" Text="Buscar" CssClass="btn" OnClick="btnBuscar_Click" />
  </div>

  <asp:GridView ID="gv" runat="server" AutoGenerateColumns="False" DataKeyNames="Id"
      OnRowCommand="gv_RowCommand" AllowPaging="True" PageSize="20"
      OnPageIndexChanging="gv_PageIndexChanging">
    <Columns>
      <asp:BoundField DataField="Id" HeaderText="Id" ItemStyle-Width="60px" />
      <asp:BoundField DataField="Numero" HeaderText="Número" />
      <asp:BoundField DataField="Asegurado" HeaderText="Asegurado" />
      <asp:BoundField DataField="FechaAlta" HeaderText="Fecha" DataFormatString="{0:dd/MM/yyyy HH:mm}" />
      <asp:TemplateField HeaderText="Acciones" ItemStyle-Width="120px">
        <ItemTemplate>
          <asp:LinkButton ID="lnkInst" runat="server" CommandName="VerInst"
              CommandArgument='<%# Eval("Numero") %>' CssClass="btn">Instancias</asp:LinkButton>
        </ItemTemplate>
      </asp:TemplateField>
    </Columns>
  </asp:GridView>
</form>
</body>
</html>
