using Intranet.WorkflowStudio.Data;
using Intranet.WorkflowStudio.Models;
using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WorkflowUI : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            // 1) Traigo el JSON que venga del front
            //    (acepto los dos nombres para no romper lo que ya tenías)
            var jsonFromForm =
                Request.Form["hfWorkflow"] ??         // nombre nuevo (JS nuevo)
                Request.Form["hfWorkflowJson"];       // tu nombre anterior

            // 2) ¿vino del "Guardar SQL" que dispara el JS?
            var evTarget = Request["__EVENTTARGET"];
            bool isSaveRequest = string.Equals(evTarget, "WF_SAVE", StringComparison.OrdinalIgnoreCase);

            if (isSaveRequest)
            {
                // guardar en SQL
                GuardarDefinicionEnSql(jsonFromForm);

                // muy importante: volver a mandarlo al front para que NO se pierda el grafo
                if (!string.IsNullOrWhiteSpace(jsonFromForm))
                {
                    var script = "window.__WF_RESTORE = " + jsonFromForm + ";";
                    ClientScript.RegisterStartupScript(this.GetType(), "wfRestoreAfterSave", script, true);
                }

                // ya está, no sigo
                return;
            }

            // 3) Para cualquier otro postback (por ejemplo, el botón "Ejecutar en servidor")
            //    si vino JSON, lo re-hidratamos igual
            if (!string.IsNullOrWhiteSpace(jsonFromForm))
            {
                var script = "window.__WF_RESTORE = " + jsonFromForm + ";";
                ClientScript.RegisterStartupScript(this.GetType(), "wfRestore", script, true);
            }
        }

        private void GuardarDefinicionEnSql(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            // podés cambiar estos 3 a mano si querés
            string codigo = "WF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string nombre = "Workflow desde UI " + DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            int version = 1;
            string creadoPor = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                                ? User.Identity.Name
                                : "workflow.ui";

            string cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (var cn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
                INSERT INTO dbo.WF_Definicion
                    (Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef)
                VALUES
                    (@Codigo, @Nombre, @Version, 1, GETDATE(), @CreadoPor, @JsonDef);", cn))
            {
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;
                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                cmd.Parameters.Add("@Version", SqlDbType.Int).Value = version;
                cmd.Parameters.Add("@CreadoPor", SqlDbType.NVarChar, 100).Value = (object)creadoPor ?? DBNull.Value;
                cmd.Parameters.Add("@JsonDef", SqlDbType.NVarChar).Value = json;

                cn.Open();
                cmd.ExecuteNonQuery();
            }

            // avisar en el panel flotante del front
            ClientScript.RegisterStartupScript(
                this.GetType(),
                "wfSaved",
                "if (window.showOutput) { window.showOutput('Guardado', 'Workflow guardado en SQL (WF_Definicion).'); }",
                true
            );
        }

        // ================= API PARA EL JS =================

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static object SaveWorkflow(string name, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("json vacío");

            var dto = JsonConvert.DeserializeObject<WorkflowDto>(json);
            var repo = new WorkflowRepository();

            // por ahora siempre crea un workflow nuevo
            var user = Environment.UserName ?? "web";

            int workflowId = repo.CreateWorkflow(name, user);
            int versionId = repo.SaveWorkflowVersion(workflowId, dto, user);

            return new { ok = true, workflowId, versionId };
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static object LoadLastWorkflow(int workflowId)
        {
            var repo = new WorkflowRepository();
            var json = repo.GetLatestWorkflowJson(workflowId);
            return new
            {
                ok = json != null,
                json
            };
        }

        protected async void btnProbarMotor_Click(object sender, EventArgs e)
        {
            // 1) primero lo que mandó el canvas
            var json = hfWorkflow.Value;
            if (string.IsNullOrWhiteSpace(json))
                json = JsonServidor.Text;

            if (string.IsNullOrWhiteSpace(json))
            {
                litLogs.Text = Server.HtmlEncode("Pegá un JSON de workflow.");
                return;
            }

            try
            {
                var wf = MotorDemo.FromJson(json);
                var logs = new List<string>();

                await MotorDemo.EjecutarAsync(wf, s => logs.Add(s));
                litLogs.Text = Server.HtmlEncode(string.Join(Environment.NewLine, logs));
            }
            catch (Exception ex)
            {
                litLogs.Text = Server.HtmlEncode(ex.ToString());
            }

            // IMPORTANTE: re-hidratar también acá para que no se pierda el canvas
            var script = "window.__WF_RESTORE = " + json + ";";
            ClientScript.RegisterStartupScript(this.GetType(), "wfRestoreAfterRun", script, true);
        }
    }
}
