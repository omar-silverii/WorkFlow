using System;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_DocTipoReglas : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "DOC_ABM" };

        protected void Page_Load(object sender, EventArgs e)
        {
            // Topbar activo siempre (postback o no)
            try
            {
                Topbar1.ActiveSection = "Tareas";
            }
            catch
            {
                // si por alguna razón aún no está el control en el aspx, no rompemos la página
            }
        }
    }
}
