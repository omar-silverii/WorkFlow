using System;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using Intranet.WorkflowStudio.WebForms;

namespace Intranet.WorkflowStudio.WebForms.Controls
{
    public partial class WsTopbar : System.Web.UI.UserControl
    {
        // "Inicio", "Workflows", "Documentos", "Tareas", "Ejecuciones"
        public string ActiveSection { get; set; }

        protected void Page_Load(object sender, EventArgs e)
        {
            bool auth = (Context?.User?.Identity?.IsAuthenticated == true);
            string userKey = auth ? (Context.User.Identity.Name ?? "").Trim() : "";

            // Menú usuario + logout sólo si está autenticado.
            liUserMenu.Visible = auth;

            ApplyActive();
        }

        private void ApplyActive()
        {
            SetActive(lnkInicio, ActiveSection == "Inicio");
            SetActive(lnkWorkflows, ActiveSection == "Workflows");
            SetActive(lnkDocumentos, ActiveSection == "Documentos");
            SetActive(lnkTareas, ActiveSection == "Tareas");
            SetActive(lnkEjecuciones, ActiveSection == "Ejecuciones");
        }

        private static void SetActive(HyperLink a, bool active)
        {
            if (a == null) return;

            var cls = (a.CssClass ?? "nav-link").Replace(" active", "").Trim();
            if (active) cls = (cls + " active").Trim();
            a.CssClass = cls;
        }

        private static void SetActive(HtmlAnchor a, bool active)
        {
            if (a == null) return;

            var cls = (a.Attributes["class"] ?? "nav-link").Replace(" active", "").Trim();
            if (active) cls = (cls + " active").Trim();
            a.Attributes["class"] = cls;
        }
    }
}
