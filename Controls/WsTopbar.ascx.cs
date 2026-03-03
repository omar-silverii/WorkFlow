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
            //var userKey = (Context?.User?.Identity?.IsAuthenticated == true) ? Context.User.Identity.Name : "";

            //// Ejemplo: mostrar Admin solo si tiene alguno de estos
            //bool canAdmin = !string.IsNullOrEmpty(userKey) &&
            //                RbacService.HasAnyPermiso(userKey, "WF_ADMIN", "SEGURIDAD_ABM");

            //lnkAdmin.Visible = canAdmin;

            bool auth = (Context?.User?.Identity?.IsAuthenticated == true);

            // Menú usuario + logout SOLO si está autenticado
            liUserMenu.Visible = auth;

            // (Opcional) esconder Admin si no está autenticado.
            // Si querés hacerlo por permiso RBAC, lo vemos después.
            liAdmin.Visible = auth;

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
