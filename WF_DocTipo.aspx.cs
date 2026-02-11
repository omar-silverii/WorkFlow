using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_DocTipo : Page
    {
        private string Cs() => ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Tipos de documento"; } catch { }

            if (!IsPostBack)
            {
                ddlEstado.SelectedValue = "1"; // por defecto Activos
                ClearForm();
                BindGrid();
            }
        }

        protected void btnBuscar_Click(object sender, EventArgs e) => BindGrid();

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtQ.Text = "";
            ddlEstado.SelectedValue = "1";
            BindGrid();
        }

        private void BindGrid()
        {
            var q = (txtQ.Text ?? "").Trim();
            var est = (ddlEstado.SelectedValue ?? "").Trim(); // "", "1", "0"

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
SELECT DocTipoId, Codigo, Nombre, ContextPrefix, PlantillaPath, RutaBase, EsActivo, CreatedAt, UpdatedAt, RulesJson
FROM dbo.WF_DocTipo
WHERE 1=1
  AND (@Q = '' OR Codigo LIKE '%' + @Q + '%' OR Nombre LIKE '%' + @Q + '%')
  AND (@EST = '' OR EsActivo = CASE WHEN @EST = '1' THEN 1 ELSE 0 END)
ORDER BY Codigo;", cn))
            {
                cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value = q;
                cmd.Parameters.Add("@EST", SqlDbType.NVarChar, 5).Value = est;

                var dt = new DataTable();
                cn.Open();
                using (var da = new SqlDataAdapter(cmd)) da.Fill(dt);

                gv.DataSource = dt;
                gv.DataBind();
            }
        }

        protected void gv_RowDataBound(object sender, System.Web.UI.WebControls.GridViewRowEventArgs e)
        {
            // nada obligatorio acá, lo dejo por si querés pintar filas
        }

        protected void gv_RowCommand(object sender, System.Web.UI.WebControls.GridViewCommandEventArgs e)
        {
            if (e.CommandName == "EDIT")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                LoadDocTipo(id);
                ShowModal();
                return;
            }

            if (e.CommandName == "TOGGLE")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                ToggleActivo(id);
                BindGrid();
                return;
            }

            if (e.CommandName == "DEL")
            {
                int id = Convert.ToInt32(e.CommandArgument);
                DeleteDocTipo(id);
                BindGrid();
                return;
            }
        }

        protected void btnGuardar_Click(object sender, EventArgs e)
        {
            var idStr = (hfId.Value ?? "").Trim();
            int id = 0;
            int.TryParse(idStr, out id);

            var codigo = (txtCodigo.Text ?? "").Trim();
            var nombre = (txtNombre.Text ?? "").Trim();
            var prefix = (txtPrefix.Text ?? "").Trim();
            var plantilla = (txtPlantilla.Text ?? "").Trim();
            var rutaBase = (txtRutaBase.Text ?? "").Trim();
            var activo = chkActivo.Checked;
            var rulesJson = (txtRulesJson.Text ?? "").Trim();

            // Validaciones mínimas
            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(prefix))
            {
                ShowMsg("Completá Código, Nombre y ContextPrefix.", isError: true);
                ShowModal();
                return;
            }

            // Normalización (código tipo constante)
            codigo = codigo.Replace(" ", "_").ToUpperInvariant();

            using (var cn = new SqlConnection(Cs()))
            {
                cn.Open();

                // Unicidad de Código
                using (var chk = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.WF_DocTipo
WHERE Codigo = @Codigo
  AND (@Id = 0 OR DocTipoId <> @Id);", cn))
                {
                    chk.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;
                    chk.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    var exists = Convert.ToInt32(chk.ExecuteScalar()) > 0;
                    if (exists)
                    {
                        ShowMsg("Ya existe un DocTipo con ese Código.", isError: true);
                        ShowModal();
                        return;
                    }
                }

                if (id > 0)
                {
                    using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_DocTipo
SET Codigo        = @Codigo,
    Nombre        = @Nombre,
    ContextPrefix = @Prefix,
    PlantillaPath = @PlantillaPath,
    RutaBase      = @RutaBase,
    EsActivo      = @Activo,
    RulesJson     = @RulesJson,
    UpdatedAt     = GETDATE()
WHERE DocTipoId = @Id;", cn))
                    {
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;
                        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 30).Value = prefix;

                        cmd.Parameters.Add("@PlantillaPath", SqlDbType.NVarChar, 500).Value =
                            (object)NullIfEmpty(plantilla) ?? DBNull.Value;

                        cmd.Parameters.Add("@RutaBase", SqlDbType.NVarChar, 500).Value =
                            (object)NullIfEmpty(rutaBase) ?? DBNull.Value;

                        cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;

                        cmd.Parameters.Add("@RulesJson", SqlDbType.NVarChar).Value =
                            (object)NullIfEmpty(rulesJson) ?? DBNull.Value;

                        cmd.ExecuteNonQuery();
                    }

                    ShowMsg($"DocTipo actualizado (Id={id}).", isError: false);
                }
                else
                {
                    using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_DocTipo
(Codigo, Nombre, ContextPrefix, PlantillaPath, RutaBase, EsActivo, CreatedAt, UpdatedAt, RulesJson)
VALUES
(@Codigo, @Nombre, @Prefix, @PlantillaPath, @RutaBase, @Activo, GETDATE(), NULL, @RulesJson);
SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
                    {
                        cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 50).Value = codigo;
                        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = nombre;
                        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 30).Value = prefix;

                        cmd.Parameters.Add("@PlantillaPath", SqlDbType.NVarChar, 500).Value =
                            (object)NullIfEmpty(plantilla) ?? DBNull.Value;

                        cmd.Parameters.Add("@RutaBase", SqlDbType.NVarChar, 500).Value =
                            (object)NullIfEmpty(rutaBase) ?? DBNull.Value;

                        cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = activo;

                        cmd.Parameters.Add("@RulesJson", SqlDbType.NVarChar).Value =
                            (object)NullIfEmpty(rulesJson) ?? DBNull.Value;

                        id = Convert.ToInt32(cmd.ExecuteScalar());
                        hfId.Value = id.ToString();
                    }

                    ShowMsg($"DocTipo creado (Id={id}).", isError: false);
                }
            }

            ClearForm();
            BindGrid();
        }

        private void LoadDocTipo(int id)
        {
            ClearForm();

            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
SELECT DocTipoId, Codigo, Nombre, ContextPrefix, PlantillaPath, RutaBase, EsActivo, RulesJson
FROM dbo.WF_DocTipo
WHERE DocTipoId = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return;

                    hfId.Value = dr.GetInt32(0).ToString();
                    txtCodigo.Text = dr.GetString(1);
                    txtNombre.Text = dr.GetString(2);
                    txtPrefix.Text = dr.GetString(3);

                    txtPlantilla.Text = dr.IsDBNull(4) ? "" : dr.GetString(4);
                    txtRutaBase.Text = dr.IsDBNull(5) ? "" : dr.GetString(5);

                    chkActivo.Checked = !dr.IsDBNull(6) && dr.GetBoolean(6);

                    txtRulesJson.Text = dr.IsDBNull(7) ? "" : dr.GetString(7);
                }
            }
        }

        private void ToggleActivo(int id)
        {
            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_DocTipo
SET EsActivo = CASE WHEN EsActivo = 1 THEN 0 ELSE 1 END,
    UpdatedAt = GETDATE()
WHERE DocTipoId = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void DeleteDocTipo(int id)
        {
            // Si querés proteger borrado cuando haya reglas asociadas o instancias, se agrega acá.
            using (var cn = new SqlConnection(Cs()))
            using (var cmd = new SqlCommand(@"
DELETE FROM dbo.WF_DocTipo
WHERE DocTipoId = @Id;", cn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void ClearForm()
        {
            hfId.Value = "";
            txtCodigo.Text = "";
            txtNombre.Text = "";
            txtPrefix.Text = "";
            txtPlantilla.Text = "";
            txtRutaBase.Text = "";
            txtRulesJson.Text = "";
            chkActivo.Checked = true;
        }

        private void ShowModal()
        {
            // abre el modal desde server (postback)
            ScriptManager.RegisterStartupScript(this, GetType(), "wfDocTipoModal", "wfDocTipoShowModal();", true);
        }

        private void ShowMsg(string msg, bool isError)
        {
            var cls = isError ? "alert-danger" : "alert-success";
            litMsg.Text = $@"<div class=""alert {cls} mt-3"">{Server.HtmlEncode(msg)}</div>";
        }

        private static string NullIfEmpty(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return s.Trim();
        }
    }
}
