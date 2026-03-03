using System;
using System.Web;
using System.Web.Security;

namespace Intranet.WorkflowStudio.WebForms
{
    public class Global : HttpApplication
    {
        public static string AuthNonce
        {
            get { return (string)(HttpContext.Current?.Application["WF_AUTH_NONCE"]); }
        }

        protected void Application_Start(object sender, EventArgs e)
        {
            // Cambia en cada arranque real de la app -> invalida cookies viejas
            Application["WF_AUTH_NONCE"] = Guid.NewGuid().ToString("N");
        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e)
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx == null) return;

                var user = ctx.User;
                if (user == null || user.Identity == null || !user.Identity.IsAuthenticated) return;

                var id = user.Identity as FormsIdentity;
                if (id == null) return;

                var t = id.Ticket;
                if (t == null) return;

                var nonce = (string)(ctx.Application["WF_AUTH_NONCE"]);
                if (string.IsNullOrEmpty(nonce))
                {
                    // Si por alguna razón falta, forzamos nuevo
                    ctx.Application["WF_AUTH_NONCE"] = Guid.NewGuid().ToString("N");
                    nonce = (string)(ctx.Application["WF_AUTH_NONCE"]);
                }

                // Si el ticket no pertenece a este arranque, lo invalidamos
                if (!string.Equals(t.UserData ?? "", nonce, StringComparison.Ordinal))
                {
                    FormsAuthentication.SignOut();
                    ctx.Response.Redirect(FormsAuthentication.LoginUrl, false);
                    ctx.ApplicationInstance.CompleteRequest();
                }
            }
            catch
            {
                // no romper el sitio por auth
            }
        }
    }
}