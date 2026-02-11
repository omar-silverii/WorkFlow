using System;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms.Controls
{
    public partial class WsTopbar : System.Web.UI.UserControl
    {
        // "Inicio", "Workflows", "Documentos", "Tareas", "Administracion"
        public string ActiveSection { get; set; }

        protected void Page_Load(object sender, EventArgs e)
        {
            ApplyActive();
        }

        private void ApplyActive()
        {
            SetActive(lnkInicio, ActiveSection == "Inicio");
            SetActive(lnkWorkflows, ActiveSection == "Workflows");
            SetActive(lnkDocumentos, ActiveSection == "Documentos");
            SetActive(lnkTareas, ActiveSection == "Tareas");
            SetActive(lnkAdmin, ActiveSection == "Administracion");
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
