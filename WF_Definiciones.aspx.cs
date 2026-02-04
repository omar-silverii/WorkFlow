using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.UI.WebControls;
using Intranet.WorkflowStudio.Runtime;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Definiciones : System.Web.UI.Page
    {
        private string Cnn
        {
            get { return ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                int defId;
                if (int.TryParse(Request.QueryString["defId"], out defId))
                {
                    CargarGridYPosicionar(defId);
                }
                else
                {
                    CargarGrid();
                }
            }
        }

        private void CargarGridYPosicionar(int defId)
        {
            string sql = @"
SELECT Id, Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef
FROM dbo.WF_Definicion
ORDER BY Codigo, Version DESC;";

            DataTable dt = new DataTable();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            using (SqlDataAdapter da = new SqlDataAdapter(cmd))
            {
                da.Fill(dt);
            }

            DecorarDefinicionesSubflow(dt);

            int rowIndex = -1;
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                if (Convert.ToInt32(dt.Rows[i]["Id"]) == defId)
                {
                    rowIndex = i;
                    break;
                }
            }

            if (rowIndex >= 0)
            {
                int pageSize = gvDef.PageSize;
                gvDef.PageIndex = rowIndex / pageSize;
            }
            else
            {
                gvDef.PageIndex = 0;
            }

            gvDef.DataSource = dt;
            gvDef.DataBind();
        }

        private void CargarGrid()
        {
            string sql = @"
SELECT Id, Codigo, Nombre, Version, Activo, FechaCreacion, CreadoPor, JsonDef
FROM dbo.WF_Definicion
WHERE 1 = 1";

            string filtro = (txtFiltro.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(filtro))
            {
                sql += " AND Codigo LIKE @Filtro";
            }

            sql += " ORDER BY Codigo, Version DESC;";

            DataTable dt = new DataTable();

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand(sql, cn))
            {
                if (!string.IsNullOrEmpty(filtro))
                    cmd.Parameters.AddWithValue("@Filtro", "%" + filtro + "%");

                using (SqlDataAdapter da = new SqlDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }

            DecorarDefinicionesSubflow(dt);

            gvDef.DataSource = dt;
            gvDef.DataBind();

            pnlJson.Visible = false;
            preJson.InnerText = string.Empty;
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            gvDef.PageIndex = 0;
            CargarGrid();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtFiltro.Text = string.Empty;
            gvDef.PageIndex = 0;
            CargarGrid();
        }

        protected void btnNuevo_Click(object sender, EventArgs e)
        {
            Response.Redirect("WorkflowUI.aspx");
        }

        protected void gvDef_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvDef.PageIndex = e.NewPageIndex;
            CargarGrid();
        }

        protected async void gvDef_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (e.CommandName == "VerJson")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                VerJson(id);
            }
            else if (e.CommandName == "VerInst")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                string url = "WF_Instancias.aspx?defId=" + id;

                Response.Redirect(url, false);
                Context.ApplicationInstance.CompleteRequest();
            }
            else if (e.CommandName == "Ejecutar")
            {
                int defId = Convert.ToInt32(e.CommandArgument);

                string usuario =
                    (User != null && User.Identity != null && User.Identity.IsAuthenticated)
                        ? User.Identity.Name
                        : (Environment.UserName ?? "web");

                string datosEntradaJson = null;

                try
                {
                    long instId = await WorkflowRuntime.CrearInstanciaYEjecutarAsync(
                        defId,
                        datosEntradaJson,
                        usuario
                    );

                    pnlJson.Visible = true;
                    preJson.InnerText =
                        "Instancia creada y ejecutada.\r\n" +
                        "WF_DefinicionId = " + defId + "\r\n" +
                        "WF_InstanciaId  = " + instId + "\r\n\r\n" +
                        "Revisá WF_Tarea / WF_Instancia para ver el estado.";
                }
                catch (Exception ex)
                {
                    pnlJson.Visible = true;
                    preJson.InnerText =
                        "Error al ejecutar la definición " + defId + ":\r\n" +
                        ex.Message;
                }
            }
        }

        protected void gvDef_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;

            var drv = e.Row.DataItem as DataRowView;
            if (drv == null) return;

            bool isSubflow = drv.Row.Table.Columns.Contains("IsSubflow")
                             && drv["IsSubflow"] != DBNull.Value
                             && Convert.ToBoolean(drv["IsSubflow"]);

            string usadoPor = drv.Row.Table.Columns.Contains("UsadoPor")
                                ? Convert.ToString(drv["UsadoPor"])
                                : "";

            var lblTipo = e.Row.FindControl("lblTipo") as Label;
            var lblUsadoPor = e.Row.FindControl("lblUsadoPor") as Label;
            var lnkEjecutar = e.Row.FindControl("lnkEjecutar") as LinkButton;

            if (lblTipo != null)
            {
                if (isSubflow)
                {
                    lblTipo.Text = "Subflow";
                    lblTipo.CssClass = "badge bg-secondary";
                }
                else
                {
                    lblTipo.Text = "Workflow";
                    lblTipo.CssClass = "badge bg-primary";
                }
            }

            if (lblUsadoPor != null)
            {
                lblUsadoPor.Text = usadoPor ?? "";
                lblUsadoPor.ToolTip = usadoPor ?? "";
            }

            if (lnkEjecutar != null && isSubflow)
            {
                // No confundir: en subflow, lo dejamos como “Probar” con confirmación.
                lnkEjecutar.Text = "Probar";
                lnkEjecutar.CssClass = "btn btn-sm btn-outline-success";
                string msg = "Este flujo es un SUBFLOW (invocado por: " + (string.IsNullOrWhiteSpace(usadoPor) ? "otro workflow" : usadoPor) +
                             ").\\n\\n¿Ejecutar en modo PRUEBA?";
                lnkEjecutar.OnClientClick = "return confirm('" + JsEscape(msg) + "');";
            }
        }

        private void VerJson(int id)
        {
            string json = null;

            using (SqlConnection cn = new SqlConnection(Cnn))
            using (SqlCommand cmd = new SqlCommand("SELECT JsonDef FROM dbo.WF_Definicion WHERE Id = @Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                cn.Open();
                object o = cmd.ExecuteScalar();
                if (o != null && o != DBNull.Value)
                    json = o.ToString();
            }

            preJson.InnerText = string.IsNullOrEmpty(json) ? "-- sin JSON --" : json;
            pnlJson.Visible = true;
        }

        // ==========================
        //  Subflow: detección automática
        // ==========================
        private void DecorarDefinicionesSubflow(DataTable dt)
        {
            if (dt == null) return;

            if (!dt.Columns.Contains("IsSubflow")) dt.Columns.Add("IsSubflow", typeof(bool));
            if (!dt.Columns.Contains("UsadoPor")) dt.Columns.Add("UsadoPor", typeof(string));

            // Mapas: por Nombre y por Codigo (por si alguno referencia uno u otro)
            var byNombre = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            var byCodigo = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in dt.Rows)
            {
                r["IsSubflow"] = false;
                r["UsadoPor"] = "";

                var nombre = Convert.ToString(r["Nombre"]);
                var codigo = Convert.ToString(r["Codigo"]);

                if (!string.IsNullOrWhiteSpace(nombre) && !byNombre.ContainsKey(nombre))
                    byNombre[nombre] = r;

                if (!string.IsNullOrWhiteSpace(codigo) && !byCodigo.ContainsKey(codigo))
                    byCodigo[codigo] = r;
            }

            // Referencias: target -> set de callers
            var usedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow caller in dt.Rows)
            {
                string callerNombre = Convert.ToString(caller["Nombre"]);
                string json = caller["JsonDef"] == DBNull.Value ? null : Convert.ToString(caller["JsonDef"]);
                if (string.IsNullOrWhiteSpace(json)) continue;

                foreach (var targetKey in FindSubflowTargets(json))
                {
                    if (string.IsNullOrWhiteSpace(targetKey)) continue;

                    // target puede ser Nombre o Codigo (o refKey viejo). Marcamos en ambos mapas.
                    DataRow target = null;

                    if (byNombre.TryGetValue(targetKey, out target) || byCodigo.TryGetValue(targetKey, out target))
                    {
                        string targetNombre = Convert.ToString(target["Nombre"]) ?? targetKey;

                        if (!usedBy.TryGetValue(targetNombre, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            usedBy[targetNombre] = set;
                        }

                        if (!string.IsNullOrWhiteSpace(callerNombre))
                            set.Add(callerNombre);
                    }
                }
            }

            foreach (DataRow r in dt.Rows)
            {
                string nombre = Convert.ToString(r["Nombre"]);
                if (string.IsNullOrWhiteSpace(nombre)) continue;

                if (usedBy.TryGetValue(nombre, out var callers) && callers != null && callers.Count > 0)
                {
                    r["IsSubflow"] = true;

                    // Mostrar pocos para no ensuciar: top 3
                    var top = callers.Take(3).ToList();
                    string texto = string.Join(", ", top);
                    if (callers.Count > 3) texto += " (+" + (callers.Count - 3) + ")";

                    r["UsadoPor"] = texto;
                }
            }
        }

        private static IEnumerable<string> FindSubflowTargets(string jsonDef)
        {
            // Buscamos nodos Type == "util.subflow" y de Parameters tomamos cualquier campo probable:
            // ref / refKey / key / workflowKey / defKey / defNombre / nombre / codigo
            // (robusto sin casarnos con un solo nombre)
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = JToken.Parse(jsonDef);

                // 1) Recorrer todos los "Type" == "util.subflow"
                var subflowNodes = root.SelectTokens("$..Type")
                    .Where(t => t.Type == JTokenType.String && string.Equals((string)t, "util.subflow", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var typeToken in subflowNodes)
                {
                    // Subir al objeto nodo (padre del campo Type)
                    var nodeObj = typeToken.Parent != null ? (typeToken.Parent.Parent as JObject) : null;
                    if (nodeObj == null) continue;

                    var pars = nodeObj["Parameters"] as JObject;
                    if (pars == null) continue;

                    string[] keys = new[]
                    {
                        "ref", "refKey", "key", "workflowKey", "wfKey",
                        "defKey", "defNombre", "nombre", "codigo", "name"
                    };

                    foreach (var k in keys)
                    {
                        var tok = pars.Property(k, StringComparison.OrdinalIgnoreCase)?.Value;
                        if (tok != null && tok.Type == JTokenType.String)
                        {
                            var v = (tok.ToString() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(v))
                                results.Add(v);
                        }
                    }
                }
            }
            catch
            {
                // si algún JSONDef está roto, no rompemos la grilla
            }

            return results;
        }

        private static string JsEscape(string s)
        {
            if (s == null) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "")
                .Replace("\n", "\\n");
        }
    }
}