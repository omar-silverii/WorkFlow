using System;
using System.Web.Security;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class Logout : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            try
            {
                FormsAuthentication.SignOut();
            }
            catch { }

            // mata cookie
            try
            {
                var c = new System.Web.HttpCookie(FormsAuthentication.FormsCookieName, "")
                {
                    Expires = DateTime.Now.AddDays(-10)
                };
                Response.Cookies.Add(c);
            }
            catch { }

            Response.Redirect("Login.aspx", false);
            Context.ApplicationInstance.CompleteRequest();
        }
    }
}