using Intranet.WorkflowStudio.Data;
using Intranet.WorkflowStudio.Models;
using Intranet.WorkflowStudio.Runtime;
using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            // Si nos llaman con ?defId=### abrimos esa definición y rehidratamos el canvas
            if (!IsPostBack)
            {
                lblUser.Text = HttpContext.Current.User.Identity.Name;
                var qs = Request.QueryString["defId"];
                int defId;
                if (int.TryParse(qs, out defId) && defId > 0)
                {
                    // Guardamos el Id en el hidden para futuros POSTBACKs (UPDATE)
                    hfDefId.Value = defId.ToString();

                    string json = null;
                    using (var cn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT JsonDef FROM dbo.WF_Definicion WHERE Id=@Id;";
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;
                        cn.Open();
                        var o = cmd.ExecuteScalar();
                        json = (o == null || o == DBNull.Value) ? null : (string)o;
                    }
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        ClientScript.RegisterStartupScript(
                            this.GetType(),
                            "wfRestoreFromDef",
                            "window.__WF_RESTORE = " + json + ";",
                            true
                        );
                    }
                }
                else
                {
                    // Es un workflow NUEVO (sin Id todavía)
                    hfDefId.Value = string.Empty;
                }
            }

            // 1) Traigo el JSON que venga del front
            //    (acepto los dos nombres para no romper lo que ya tenías)
            var jsonFromForm =
                Request.Unvalidated["hfWorkflow"] ??         // nombre nuevo (JS nuevo)
                Request.Unvalidated["hfWorkflowJson"];       // tu nombre anterior

            // 2) ¿vino del "Guardar SQL" que dispara el JS?
            var evTarget = Request["__EVENTTARGET"];
            bool isSaveRequest = string.Equals(evTarget, "WF_SAVE", StringComparison.OrdinalIgnoreCase);

            if (isSaveRequest)
            {
                // guardar en SQL (con validación previa)
                GuardarDefinicionEnSql(jsonFromForm);

                // muy importante: volver a mandarlo al front para que NO se pierda el grafo
                if (!string.IsNullOrWhiteSpace(jsonFromForm))
                {
                    var script = "window.__WF_RESTORE = " + EscapeForInlineScript(jsonFromForm) + ";";
                    ClientScript.RegisterStartupScript(this.GetType(), "wfRestoreAfterSave", script, true);
                }

                // ya está, no sigo
                return;
            }

            // 3) Para cualquier otro postback (por ejemplo, el botón "Ejecutar en servidor")
            //    si vino JSON, lo re-hidratamos igual
            if (!string.IsNullOrWhiteSpace(jsonFromForm))
            {
                var script = "window.__WF_RESTORE = " + EscapeForInlineScript(jsonFromForm) + ";";
                ClientScript.RegisterStartupScript(this.GetType(), "wfRestore", script, true);
            }
        }


        private static string EscapeForInlineScript(string s)
        {
            return s == null ? null : s.Replace("</script", "<\\/script");
        }

        private void GuardarDefinicionEnSql(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                ClientScript.RegisterStartupScript(
                    this.GetType(),
                    "wfNoJson",
                    "if (window.showOutput) { window.showOutput('Validación', 'No llegó JSON desde el editor.'); }",
                    true
                );
                return;
            }

            // Validación espejo (contrato Start/End/Edges y parámetros básicos)
            if (!ValidateWorkflowJson(json, out var vmsg))
            {
                ClientScript.RegisterStartupScript(
                    this.GetType(),
                    "wfInvalid",
                    $"if (window.showOutput) {{ window.showOutput('Validación', {ToJsString(vmsg)}); }}",
                    true
                );
                return;
            }

            // Leer Meta.Template y Meta.Name del JSON
            string template = null;
            string nameFromMeta = null;
            try
            {
                var root = JObject.Parse(json);
                template = (string)(root["Meta"]?["Template"]);
                nameFromMeta = (string)(root["Meta"]?["Name"]);
            }
            catch
            {
                // ignorar
            }

            bool esEmision = string.Equals(template, "EMISION_POLIZA", StringComparison.OrdinalIgnoreCase);

            // Nombre: si vino en Meta.Name, usarlo; si no, armar uno por defecto
            string nombre = !string.IsNullOrWhiteSpace(nameFromMeta)
                ? nameFromMeta.Trim()
                : (esEmision ? "Emisión de Póliza " : "Workflow desde UI ") + DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            // 1) ¿Estamos editando una definición existente?
            int defId = 0;
            int.TryParse(hfDefId.Value, out defId);   // <<< AHORA usamos el hidden, NO el QueryString

            string cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            string mensaje;

            using (var cn = new SqlConnection(cs))
            {
                cn.Open();

                if (defId > 0)
                {
                    // ===== UPDATE: edición de una definición existente (mantiene el Id) =====
                    using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Definicion
SET    Nombre   = @Nombre,
       JsonDef  = @JsonDef
WHERE  Id       = @Id;", cn))
                    {
                        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = (object)nombre ?? DBNull.Value;
                        cmd.Parameters.Add("@JsonDef", SqlDbType.NVarChar).Value = json;
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = defId;

                        cmd.ExecuteNonQuery();
                    }

                    mensaje = $"Definición actualizada (Id={defId}).";
                }
                else
                {
                    // ===== INSERT: nueva definición =====
                    string pref = esEmision ? "EMISION-" : "WF-";
                    string codigo = pref + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    int version = 1;
                    string creadoPor = (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                                        ? User.Identity.Name
                                        : "workflow.ui";

                    using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Definicion
    (Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef)
VALUES
    (@Codigo, @Nombre, @Version, 1, GETDATE(), @CreadoPor, @JsonDef);
SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
                    {
                        cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;
                        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                        cmd.Parameters.Add("@Version", SqlDbType.Int).Value = version;
                        cmd.Parameters.Add("@CreadoPor", SqlDbType.NVarChar, 100).Value = (object)creadoPor ?? DBNull.Value;
                        cmd.Parameters.Add("@JsonDef", SqlDbType.NVarChar).Value = json;

                        // Obtenemos el nuevo Id
                        defId = Convert.ToInt32(cmd.ExecuteScalar());
                        // Lo guardamos en el hidden para próximos saves (serán UPDATE)
                        hfDefId.Value = defId.ToString();
                    }

                    mensaje = $"Workflow guardado en SQL (WF_Definicion nueva, Id={defId}).";
                }
            }

            ClientScript.RegisterStartupScript(
                this.GetType(),
                "wfSaved",
                $"if (window.showOutput) {{ window.showOutput('Guardado', {ToJsString(mensaje)}); }}",
                true
            );
        }




        private static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "WF";
            s = s.Trim().ToLowerInvariant();
            var normalized = s.Normalize(System.Text.NormalizationForm.FormD);
            var chars = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);    
                if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(c)) chars.Append(c);
                    else if (char.IsWhiteSpace(c) || c == '-' || c == '_') chars.Append('-');
                }
            }
            var slug = System.Text.RegularExpressions.Regex.Replace(chars.ToString(), "-{2,}", "-").Trim('-');
            return string.IsNullOrEmpty(slug) ? "wf" : slug;
        }


        private static string ToJsString(string s)
        {
            if (s == null) return "''";
            return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
        }

        /// <summary>
        /// Valida contrato de workflow:
        ///  - JSON válido con { StartNodeId, Nodes{}, Edges[] }
        ///  - 1 util.start y ≥1 util.end
        ///  - Edges con From/To existentes y Condition ∈ {always,true,false,error} ∪ dinámicos (p.ej. control.switch)
        ///  - data.sql: requiere al menos conexión (connection|connectionStringName) y query/commandText
        ///  - control.if: requiere expression
        ///  - control.delay: ms numérico (si viene)
        ///  - control.loop: requiere forEach
        ///  - Parameters.position (si existe) debe tener x/y numéricos
        /// </summary>
        private static bool ValidateWorkflowJson(string json, out string message)
        {
            try
            {
                var root = JObject.Parse(json);
                var startId = (string)root["StartNodeId"];
                var nodes = root["Nodes"] as JObject;
                var edges = root["Edges"] as JArray;

                if (string.IsNullOrWhiteSpace(startId)) { message = "StartNodeId requerido."; return false; }
                if (nodes == null || nodes.Count == 0) { message = "Nodes vacío."; return false; }

                int startCount = 0, endCount = 0;
                var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // NUEVO: mapa de condiciones permitidas por nodo From (para control.switch, etc.)
                var perNodeAllowed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var defaultAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "always", "true", "false", "error" };

                foreach (var prop in nodes.Properties())
                {
                    var n = prop.Value as JObject;
                    var id = (string)n["Id"];
                    var type = ((string)n["Type"] ?? "").Trim().ToLowerInvariant();
                    nodeIds.Add(id);

                    if (type == "util.start") startCount++;
                    if (type == "util.end") endCount++;

                    var par = n["Parameters"] as JObject ?? new JObject();

                    // data.sql
                    if (type == "data.sql")
                    {
                        var hasConn = !string.IsNullOrWhiteSpace((string)par["connection"]) ||
                                      !string.IsNullOrWhiteSpace((string)par["connectionStringName"]);
                        var hasCmd = !string.IsNullOrWhiteSpace((string)par["query"]) ||
                                     !string.IsNullOrWhiteSpace((string)par["commandText"]);
                        if (!hasConn) { message = $"Nodo {id}: falta 'connection' (o 'connectionStringName')."; return false; }
                        if (!hasCmd) { message = $"Nodo {id}: falta 'query' (o 'commandText')."; return false; }
                    }

                    // control.if
                    if (type == "control.if")
                    {
                        if (string.IsNullOrWhiteSpace((string)par["expression"])) { message = $"Nodo {id}: 'expression' requerido."; return false; }
                        perNodeAllowed[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false" };
                    }

                    // control.switch → agrega labels dinámicos
                    if (type == "control.switch")
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var casos = par["casos"] as JObject;
                        if (casos != null)
                        {
                            foreach (var c in casos.Properties())
                                set.Add(c.Name);
                        }
                        var def = (string)par["default"];
                        if (!string.IsNullOrWhiteSpace(def)) set.Add(def);
                        // también permitimos always/error por si cablean salida de error a otro lado
                        set.Add("always"); set.Add("error");
                        perNodeAllowed[id] = set;
                    }

                    // control.delay
                    if (type == "control.delay")
                    {
                        var msTok = par["ms"];
                        if (msTok != null && msTok.Type != JTokenType.Null)
                        {
                            if (!int.TryParse((string)msTok, out var _)) { message = $"Nodo {id}: ms debe ser numérico."; return false; }
                        }
                        perNodeAllowed[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "always" };
                    }

                    // control.loop
                    if (type == "control.loop")
                    {
                        if (par["forEach"] == null) { message = $"Nodo {id}: 'forEach' requerido."; return false; }
                        perNodeAllowed[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false" };
                    }

                    // file.read / file.write / email.send → permitir always/error
                    if (type == "file.read" || type == "file.write" || type == "email.send")
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "always", "error" };
                        perNodeAllowed[id] = set;
                    }

                    // position check
                    var pos = par["position"] as JObject;
                    if (pos != null)
                    {
                        if (pos["x"] == null || pos["y"] == null) { message = $"Nodo {id}: position debe tener x/y."; return false; }
                        if (!pos["x"].Type.ToString().Contains("Float") && !pos["x"].Type.ToString().Contains("Integer"))
                        { message = $"Nodo {id}: position.x debe ser numérico."; return false; }
                        if (!pos["y"].Type.ToString().Contains("Float") && !pos["y"].Type.ToString().Contains("Integer"))
                        { message = $"Nodo {id}: position.y debe ser numérico."; return false; }
                    }
                }

                if (startCount != 1) { message = "Debe existir exactamente 1 nodo util.start."; return false; }
                if (endCount < 1) { message = "Debe existir al menos 1 nodo util.end."; return false; }
                if (!nodeIds.Contains(startId)) { message = "StartNodeId no coincide con un nodo existente."; return false; }

                foreach (var e in edges ?? new JArray())
                {
                    var from = (string)e["From"];
                    var to = (string)e["To"];
                    var cond = ((string)e["Condition"] ?? "always").Trim();

                    if (!nodeIds.Contains(from) || !nodeIds.Contains(to))
                    { message = $"Arista inválida: {from} → {to} (nodo inexistente)."; return false; }

                    var condOk = defaultAllowed.Contains(cond);
                    if (!condOk && perNodeAllowed.TryGetValue(from, out var allowedForFrom))
                        condOk = allowedForFrom.Contains(cond);

                    if (!condOk)
                    {
                        message = $"Condition no válida en arista {from}→{to}: '{cond}'.";
                        return false;
                    }
                }

                message = "OK";
                return true;
            }
            catch (Exception ex)
            {
                message = "JSON inválido: " + ex.Message;
                return false;
            }
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
                var wf = WorkflowRunner.FromJson(json);
                var logs = new List<string>();
                var seed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    // guardo todo lo que vino de la UI debajo de "input"
                    seed["input"] = JsonConvert.DeserializeObject<object>(json);
                }
                System.Web.HttpContext.Current.Items["WF_SEED"] = seed; // NUEVO: para que el Contexto lo importe
                await WorkflowRunner.EjecutarAsync(
                    wf,
                    s => logs.Add(s),
                    handlers: new IManejadorNodo[]
                    {
                        new ManejadorSql(),
                        new HParallel(),
                        new HJoin(),
                        new HUtilError(),
                        new HUtilNotify(),
                        new HEmailSend()
                    },
                    ct: System.Threading.CancellationToken.None
                );
                litLogs.Text = Server.HtmlEncode(string.Join(Environment.NewLine, logs));
            }
            catch (Exception ex)
            {
                litLogs.Text = Server.HtmlEncode(ex.ToString());
            }

            // IMPORTANTE: re-hidratar también acá para que no se pierda el canvas
            var script = "window.__WF_RESTORE = " + EscapeForInlineScript(json) + ";";
            ClientScript.RegisterStartupScript(this.GetType(), "wfRestoreAfterRun", script, true);
        }
    }
}
