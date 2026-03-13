using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Definicion_Adjuntos : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "WF_ADMIN" };

        private string Cnn => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        private int DefIdActual
        {
            get { return (ViewState["DefIdActual"] is int v) ? v : 0; }
            set { ViewState["DefIdActual"] = value; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Workflows"; } catch { }

            if (!IsPostBack)
            {
                int defId;
                if (!int.TryParse(Convert.ToString(Request.QueryString["defId"]), out defId) || defId <= 0)
                {
                    MostrarMsg("Definición inválida.", true);
                    btnGuardar.Enabled = false;
                    return;
                }

                DefIdActual = defId;
                CargarDefinicion(defId);
            }
        }

        private void CargarDefinicion(int defId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
SELECT Id, Codigo, Nombre, JsonDef
FROM dbo.WF_Definicion
WHERE Id=@Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                    {
                        MostrarMsg("No se encontró la definición.", true);
                        btnGuardar.Enabled = false;
                        return;
                    }

                    txtDefId.Text = Convert.ToString(dr["Id"]);
                    txtCodigo.Text = Convert.ToString(dr["Codigo"]);
                    txtNombre.Text = Convert.ToString(dr["Nombre"]);

                    var jsonDef = dr["JsonDef"] as string;
                    CargarMetaAdjuntos(jsonDef);
                }
            }
        }

        private void CargarMetaAdjuntos(string jsonDef)
        {
            chkHabilitado.Checked = true;
            ddlDestinoTipo.SelectedValue = "INSTANCIA";
            txtDestinoTexto.Text = "";

            if (string.IsNullOrWhiteSpace(jsonDef))
                return;

            JObject root = null;
            try { root = JObject.Parse(jsonDef); } catch { root = null; }
            if (root == null) return;

            var meta = root["Meta"] as JObject;
            var attachments = meta?["attachments"] as JObject;
            if (attachments == null) return;

            bool habilitado;
            if (bool.TryParse(Convert.ToString(attachments["enabled"] ?? "true"), out habilitado))
                chkHabilitado.Checked = habilitado;

            var tipo = Convert.ToString(attachments["destinoTipo"] ?? "INSTANCIA").Trim().ToUpperInvariant();
            if (ddlDestinoTipo.Items.FindByValue(tipo) != null)
                ddlDestinoTipo.SelectedValue = tipo;

            txtDestinoTexto.Text = Convert.ToString(attachments["destinoTexto"] ?? "");
        }

        protected void btnGuardar_Click(object sender, EventArgs e)
        {
            var defId = DefIdActual;
            if (defId <= 0)
            {
                MostrarMsg("No se pudo resolver la definición.", true);
                return;
            }

            string jsonActual = "";
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand("SELECT JsonDef FROM dbo.WF_Definicion WHERE Id=@Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;
                cn.Open();
                jsonActual = Convert.ToString(cmd.ExecuteScalar() ?? "");
            }

            JObject root = null;
            try { root = string.IsNullOrWhiteSpace(jsonActual) ? new JObject() : JObject.Parse(jsonActual); }
            catch { root = new JObject(); }

            var meta = root["Meta"] as JObject;
            if (meta == null)
            {
                meta = new JObject();
                root["Meta"] = meta;
            }

            var attachments = meta["attachments"] as JObject;
            if (attachments == null)
            {
                attachments = new JObject();
                meta["attachments"] = attachments;
            }

            attachments["enabled"] = chkHabilitado.Checked;
            attachments["destinoTipo"] = (ddlDestinoTipo.SelectedValue ?? "INSTANCIA").Trim().ToUpperInvariant();
            attachments["destinoTexto"] = (txtDestinoTexto.Text ?? "").Trim();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Definicion
SET JsonDef=@J
WHERE Id=@Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;
                cmd.Parameters.Add("@J", SqlDbType.NVarChar).Value = root.ToString(Newtonsoft.Json.Formatting.None);
                cn.Open();
                cmd.ExecuteNonQuery();
            }

            MostrarMsg("Configuración de adjuntos guardada.", false);
        }

        private void MostrarMsg(string msg, bool isError)
        {
            pnlMsg.Visible = true;
            pnlMsg.CssClass = isError ? "alert alert-danger" : "alert alert-success";
            pnlMsg.Controls.Clear();
            pnlMsg.Controls.Add(new System.Web.UI.LiteralControl(Server.HtmlEncode(msg)));
        }
    }
}