<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Login.aspx.cs" Inherits="Intranet.WorkflowStudio.WebForms.Login" %>

<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Workflow Studio - Login</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link href="Content/bootstrap.min.css" rel="stylesheet" />
</head>
<body class="bg-light">
    <form id="form1" runat="server">
        <div class="container" style="max-width:520px;margin-top:10vh;">
            <div class="card shadow-sm">
                <div class="card-body p-4">
                    <h4 class="mb-3">Workflow Studio</h4>
                    <p class="text-muted mb-4">Ingresá con tu usuario del sistema (WF_User).</p>

                    <asp:Label runat="server" ID="lblError" CssClass="alert alert-danger" Visible="false" />

                    <div class="mb-3">
                        <label class="form-label">Usuario</label>
                        <asp:DropDownList runat="server" ID="ddlUsers" CssClass="form-select" />
                    </div>

                    <div class="mb-3">
                        <label class="form-label">Clave</label>

                        <div class="input-group">
                            <asp:TextBox runat="server" ID="txtPass" TextMode="Password" CssClass="form-control" />
                            <button type="button" class="btn btn-outline-secondary" id="btnTogglePass" title="Ver/Ocultar">
                                👁
                            </button>
                        </div>

                        <div class="form-text">Para demo: la clave es la misma para todos (configurable).</div>
                    </div>

                    <asp:Button runat="server" ID="btnLogin" CssClass="btn btn-primary w-100" Text="Ingresar" OnClick="btnLogin_Click" />
                </div>
            </div>
        </div>

        <script type="text/javascript">
            (function () {
                var btn = document.getElementById('btnTogglePass');
                if (!btn) return;
                btn.addEventListener('click', function () {
                    var inp = document.getElementById('<%= txtPass.ClientID %>');
                    if (!inp) return;
                    inp.type = (inp.type === 'password') ? 'text' : 'password';
                });
            })();
        </script>

    </form>
</body>
</html>