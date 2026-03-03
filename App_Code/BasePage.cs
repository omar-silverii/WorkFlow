using System;
using System.Web;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public class BasePage : Page
    {
        // Cada página define qué permiso requiere (si requiere)
        protected virtual string[] RequiredPermissions => null; // null => solo autenticación

        protected override void OnLoad(EventArgs e)
        {
            // 1) Autenticación
            if (Context?.User?.Identity == null || !Context.User.Identity.IsAuthenticated)
            {
                Response.Redirect("~/Login.aspx?ReturnUrl=" + HttpUtility.UrlEncode(Request.RawUrl), false);
                Context.ApplicationInstance.CompleteRequest();
                return;
            }

            // 2) Autorización (RBAC)
            var perms = RequiredPermissions;
            if (perms != null && perms.Length > 0)
            {
                var userKey = Context.User.Identity.Name ?? "";
                if (!RbacService.HasAnyPermiso(userKey, perms))
                {
                    Response.Redirect("~/Denied.aspx?u=" + HttpUtility.UrlEncode(userKey) +
                                      "&r=" + HttpUtility.UrlEncode(Request.RawUrl), false);
                    Context.ApplicationInstance.CompleteRequest();
                    return;
                }
            }

            base.OnLoad(e);
        }
    }
}