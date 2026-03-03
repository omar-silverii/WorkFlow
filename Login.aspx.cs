using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.Security;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class Login : Page
    {
        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (User != null && User.Identity != null && User.Identity.IsAuthenticated)
            {
                Response.Redirect("Default.aspx", false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            if (!IsPostBack)
            {
                CargarUsuarios();
            }
        }

        private void CargarUsuarios()
        {
            ddlUsers.Items.Clear();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT UserKey, ISNULL(NULLIF(DisplayName,''), UserKey) AS Nom
FROM dbo.WF_User
WHERE Activo = 1
ORDER BY Nom;";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        var key = dr.GetString(0);
                        var nom = dr.GetString(1);
                        ddlUsers.Items.Add(new System.Web.UI.WebControls.ListItem(nom, key));
                    }
                }
            }

            if (ddlUsers.Items.Count == 0)
            {
                lblError.Visible = true;
                lblError.Text = "No hay usuarios activos en WF_User. Cargá al menos 1 usuario.";
            }
        }

        protected void btnLogin_Click(object sender, EventArgs e)
        {
            lblError.Visible = false;
            lblError.Text = "";

            var userKey = (ddlUsers.SelectedValue ?? "").Trim();
            var pass = (txtPass.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(userKey))
            {
                lblError.Visible = true;
                lblError.Text = "Seleccioná un usuario.";
                return;
            }

            var demoSecret = (ConfigurationManager.AppSettings["WF_DEMO_SECRET"] ?? "").Trim();
            if (string.IsNullOrEmpty(demoSecret)) demoSecret = "solo-para-demo-workflow";

            if (!string.Equals(pass, demoSecret, StringComparison.Ordinal))
            {
                lblError.Visible = true;
                lblError.Text = "Clave incorrecta.";
                return;
            }

            if (!UsuarioActivo(userKey))
            {
                lblError.Visible = true;
                lblError.Text = "Usuario inválido o inactivo.";
                return;
            }

            // ===== FormsAuth con Nonce de arranque (B: siempre pedir login al arrancar app) =====
            var nonce = (string)(Application["WF_AUTH_NONCE"] ?? "");
            if (string.IsNullOrEmpty(nonce))
            {
                nonce = Guid.NewGuid().ToString("N");
                Application["WF_AUTH_NONCE"] = nonce;
            }

            var ticket = new FormsAuthenticationTicket(
                1,
                userKey,
                DateTime.Now,
                DateTime.Now.AddHours(8),
                false,          // NO persistente
                nonce,          // UserData = nonce de arranque
                FormsAuthentication.FormsCookiePath
            );

            var enc = FormsAuthentication.Encrypt(ticket);
            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, enc)
            {
                HttpOnly = true,
                Secure = FormsAuthentication.RequireSSL
            };
            Response.Cookies.Add(cookie);

            Response.Redirect("Default.aspx", false);
            Context.ApplicationInstance.CompleteRequest();
        }

        private bool UsuarioActivo(string userKey)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.WF_User WHERE UserKey=@U AND Activo=1;";
                cmd.Parameters.Add("@U", SqlDbType.NVarChar, 200).Value = userKey;
                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}