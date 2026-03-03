using System;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class Denied : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = ""; } catch { }

            var user = (HttpContext.Current?.User?.Identity?.Name) ?? "(anónimo)";
            var path = (Request?.RawUrl) ?? "";

            litUser.Text = Server.HtmlEncode(user);
            litPath.Text = Server.HtmlEncode(path);

            Response.StatusCode = 403;
        }
    }
}