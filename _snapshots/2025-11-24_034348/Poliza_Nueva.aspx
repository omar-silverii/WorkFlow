<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Poliza_Nueva.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.Poliza_Nueva" Async="true"  %>
<!DOCTYPE html>
<html>
<head runat="server">
    <title>Nueva póliza</title>
    <link rel="stylesheet" href="~/Content/bootstrap.min.css" />
</head>
<body>
<form id="form1" runat="server" class="p-3">
    <h4>Nueva póliza (demo workflow)</h4>

    <div class="form-group">
        <label>Número póliza</label>
        <asp:TextBox ID="txtPoliza" runat="server" CssClass="form-control form-control-sm" />
    </div>

    <div class="form-group">
        <label>Asegurado</label>
        <asp:TextBox ID="txtAsegurado" runat="server" CssClass="form-control form-control-sm" />
    </div>

    <div class="form-group">
        <label>Workflow a usar</label>
        <asp:DropDownList ID="ddlWF" runat="server" CssClass="form-control form-control-sm" />
    </div>

    <asp:Button ID="btnEnviar" runat="server" Text="Enviar al workflow"
        CssClass="btn btn-primary btn-sm" OnClick="btnEnviar_Click" />

    <asp:Label ID="lblMsg" runat="server" CssClass="d-block mt-3 text-info"></asp:Label>
</form>
</body>
</html>

