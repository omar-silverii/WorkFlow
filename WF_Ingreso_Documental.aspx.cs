using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Web.UI.WebControls;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class WF_Ingreso_Documental : BasePage
    {
        protected override string[] RequiredPermissions =>
            new[] { "INGRESO_DOCUMENTAL", "DOC_ABM", "WF_ADMIN" };

        private string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        protected void Page_Load(object sender, EventArgs e)
        {
            try { Topbar1.ActiveSection = "Documentos"; } catch { }

            if (!IsPostBack)
            {
                bool schemaOk = SchemaExists();
                pnlSchemaMissing.Visible = !schemaOk;
                pnlMain.Visible = schemaOk;

                if (!schemaOk)
                    return;

                LoadDefinitions(ddlResolverWorkflow, true);
                LoadDefinitions(ddlRutaWorkflow, true);

                pnlRulesAdmin.Visible = CanManageRoutes();
                ddlFiltroEstado.SelectedValue = "PENDIENTE_RUTA";
                ClearRouteForm();
                BindAll();
            }
        }

        protected void btnBuscar_Click(object sender, EventArgs e)
        {
            BindIngresos();
        }

        protected void btnLimpiar_Click(object sender, EventArgs e)
        {
            txtBuscar.Text = "";
            ddlFiltroEstado.SelectedValue = "";
            BindIngresos();
        }

        protected void btnRefrescar_Click(object sender, EventArgs e)
        {
            BindAll();
            ShowMessage("Bandeja actualizada.", false);
        }

        protected void gvIngresos_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (!string.Equals(e.CommandName, "RESOLVER", StringComparison.OrdinalIgnoreCase))
                return;

            long ingresoId;
            if (!long.TryParse(Convert.ToString(e.CommandArgument), out ingresoId))
                return;

            LoadIngresoForResolution(ingresoId);
        }

        protected void btnAsignarWorkflow_Click(object sender, EventArgs e)
        {
            long ingresoId;
            int definitionId;

            if (!long.TryParse(hfIngresoId.Value, out ingresoId))
            {
                ShowMessage("No se pudo identificar el ingreso seleccionado.", true);
                return;
            }

            if (!int.TryParse(ddlResolverWorkflow.SelectedValue, out definitionId) || definitionId <= 0)
            {
                ShowMessage("Elegí el workflow que debe iniciar este documento.", true);
                pnlResolver.Visible = true;
                return;
            }

            string motivo = (txtResolverMotivo.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(motivo))
                motivo = "Clasificación manual desde Bandeja de Ingreso Documental.";

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE dbo.WF_IngresoDocumento
SET WF_IngresoRutaId = NULL,
    WF_DefinicionId = @DefId,
    Estado = N'RUTA_RESUELTA',
    OrigenDecision = N'USUARIO',
    Confianza = NULL,
    MotivoDecision = @Motivo,
    DecisionPor = @Usuario,
    UltimoError = NULL,
    FechaDecision = GETDATE(),
    FechaActualizacion = GETDATE()
WHERE Id = @Id
  AND WF_InstanciaId IS NULL
  AND Estado = N'PENDIENTE_RUTA';";

                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = definitionId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = CurrentUser();
                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = ingresoId;

                cn.Open();
                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                {
                    ShowMessage(
                        "El ingreso ya cambió de estado o ya creó una instancia. Actualizá la bandeja.",
                        true);
                    pnlResolver.Visible = false;
                    BindAll();
                    return;
                }
            }

            pnlResolver.Visible = false;
            ShowMessage(
                "Workflow asignado. El dispatcher tomará la decisión en su próximo ciclo sin crear otra entrada.",
                false);
            BindAll();
        }

        protected void btnCancelarResolver_Click(object sender, EventArgs e)
        {
            pnlResolver.Visible = false;
            hfIngresoId.Value = "";
            txtResolverMotivo.Text = "";
            ddlResolverWorkflow.SelectedIndex = 0;
        }

        protected void btnGuardarRuta_Click(object sender, EventArgs e)
        {
            if (!CanManageRoutes())
            {
                ShowMessage("No tenés permiso para administrar reglas de enrutamiento.", true);
                return;
            }

            int id;
            int.TryParse(hfRutaId.Value, out id);

            string codigo = NormalizeCode(txtRutaCodigo.Text);
            string nombre = (txtRutaNombre.Text ?? "").Trim();
            string canal = NormalizeNullableCode(txtRutaCanal.Text);
            string pattern = NullIfEmpty(txtRutaPatron.Text);
            string extension = NormalizeExtension(txtRutaExtension.Text);

            int priority;
            if (!int.TryParse((txtRutaPrioridad.Text ?? "").Trim(), out priority))
                priority = 100;

            int definitionId;
            if (!int.TryParse(ddlRutaWorkflow.SelectedValue, out definitionId) || definitionId <= 0)
            {
                ShowMessage("Elegí el workflow de destino de la regla.", true);
                return;
            }

            if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre))
            {
                ShowMessage("Completá Código y Nombre de la regla.", true);
                return;
            }

            using (var cn = new SqlConnection(Cnn))
            {
                cn.Open();

                using (var duplicate = cn.CreateCommand())
                {
                    duplicate.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_IngresoRuta
WHERE Codigo = @Codigo
  AND Id <> @Id;";
                    duplicate.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = codigo;
                    duplicate.Parameters.Add("@Id", SqlDbType.Int).Value = id;

                    if (Convert.ToInt32(duplicate.ExecuteScalar()) > 0)
                    {
                        ShowMessage("Ya existe una regla con ese Código.", true);
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(pattern) && string.IsNullOrWhiteSpace(extension) && chkRutaActiva.Checked)
                {
                    using (var defaultCheck = cn.CreateCommand())
                    {
                        defaultCheck.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_IngresoRuta
WHERE Activo = 1
  AND Id <> @Id
  AND ISNULL(LTRIM(RTRIM(CanalCodigo)), N'') = ISNULL(@Canal, N'')
  AND ISNULL(LTRIM(RTRIM(PatronArchivo)), N'') = N''
  AND ISNULL(LTRIM(RTRIM(Extension)), N'') = N'';";
                        defaultCheck.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        defaultCheck.Parameters.Add("@Canal", SqlDbType.NVarChar, 80).Value =
                            (object)canal ?? DBNull.Value;

                        if (Convert.ToInt32(defaultCheck.ExecuteScalar()) > 0)
                        {
                            ShowMessage(
                                "Ya existe una ruta predeterminada activa para ese canal. " +
                                "Definí una condición o desactivá la existente.",
                                true);
                            return;
                        }
                    }
                }

                if (id > 0)
                {
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
UPDATE dbo.WF_IngresoRuta
SET Codigo = @Codigo,
    Nombre = @Nombre,
    CanalCodigo = @Canal,
    PatronArchivo = @Patron,
    Extension = @Extension,
    Prioridad = @Prioridad,
    WF_DefinicionId = @DefId,
    Activo = @Activo,
    FechaActualizacion = GETDATE()
WHERE Id = @Id;";

                        AddRouteParameters(cmd, id, codigo, nombre, canal, pattern, extension, priority, definitionId, chkRutaActiva.Checked);
                        cmd.ExecuteNonQuery();
                    }

                    ShowMessage("Regla de enrutamiento actualizada.", false);
                }
                else
                {
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO dbo.WF_IngresoRuta
(
    Codigo, Nombre, CanalCodigo, PatronArchivo, Extension,
    Prioridad, WF_DefinicionId, Activo, FechaCreacion
)
VALUES
(
    @Codigo, @Nombre, @Canal, @Patron, @Extension,
    @Prioridad, @DefId, @Activo, GETDATE()
);";

                        AddRouteParameters(cmd, 0, codigo, nombre, canal, pattern, extension, priority, definitionId, chkRutaActiva.Checked);
                        cmd.ExecuteNonQuery();
                    }

                    ShowMessage("Regla de enrutamiento creada.", false);
                }
            }

            ClearRouteForm();
            BindRoutes();
        }

        protected void btnNuevaRuta_Click(object sender, EventArgs e)
        {
            ClearRouteForm();
        }

        protected void gvRutas_RowCommand(object sender, GridViewCommandEventArgs e)
        {
            if (!CanManageRoutes())
                return;

            int id;
            if (!int.TryParse(Convert.ToString(e.CommandArgument), out id))
                return;

            if (string.Equals(e.CommandName, "EDITAR_RUTA", StringComparison.OrdinalIgnoreCase))
            {
                LoadRoute(id);
                return;
            }

            if (string.Equals(e.CommandName, "TOGGLE_RUTA", StringComparison.OrdinalIgnoreCase))
            {
                using (var cn = new SqlConnection(Cnn))
                {
                    cn.Open();

                    bool activating;
                    string channel;
                    string pattern;
                    string extension;

                    using (var read = cn.CreateCommand())
                    {
                        read.CommandText = @"
SELECT Activo, CanalCodigo, PatronArchivo, Extension
FROM dbo.WF_IngresoRuta
WHERE Id = @Id;";
                        read.Parameters.Add("@Id", SqlDbType.Int).Value = id;

                        using (var dr = read.ExecuteReader())
                        {
                            if (!dr.Read()) return;

                            activating = !Convert.ToBoolean(dr["Activo"]);
                            channel = dr["CanalCodigo"] == DBNull.Value ? null : Convert.ToString(dr["CanalCodigo"]);
                            pattern = dr["PatronArchivo"] == DBNull.Value ? null : Convert.ToString(dr["PatronArchivo"]);
                            extension = dr["Extension"] == DBNull.Value ? null : Convert.ToString(dr["Extension"]);
                        }
                    }

                    if (activating &&
                        string.IsNullOrWhiteSpace(pattern) &&
                        string.IsNullOrWhiteSpace(extension))
                    {
                        using (var duplicateDefault = cn.CreateCommand())
                        {
                            duplicateDefault.CommandText = @"
SELECT COUNT(1)
FROM dbo.WF_IngresoRuta
WHERE Activo = 1
  AND Id <> @Id
  AND ISNULL(LTRIM(RTRIM(CanalCodigo)), N'') = ISNULL(@Canal, N'')
  AND ISNULL(LTRIM(RTRIM(PatronArchivo)), N'') = N''
  AND ISNULL(LTRIM(RTRIM(Extension)), N'') = N'';";
                            duplicateDefault.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                            duplicateDefault.Parameters.Add("@Canal", SqlDbType.NVarChar, 80).Value =
                                (object)NullIfEmpty(channel) ?? DBNull.Value;

                            if (Convert.ToInt32(duplicateDefault.ExecuteScalar()) > 0)
                            {
                                ShowMessage(
                                    "No se puede activar: ya existe una ruta predeterminada activa para ese canal.",
                                    true);
                                return;
                            }
                        }
                    }

                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
UPDATE dbo.WF_IngresoRuta
SET Activo = CASE WHEN Activo = 1 THEN 0 ELSE 1 END,
    FechaActualizacion = GETDATE()
WHERE Id = @Id;";
                        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }

                BindRoutes();
                ShowMessage("Estado de la regla actualizado.", false);
            }
        }

        protected string EstadoTexto(object value)
        {
            string state = (Convert.ToString(value) ?? "").ToUpperInvariant();
            switch (state)
            {
                case "RECIBIDO": return "Recibido";
                case "PENDIENTE_RUTA": return "Pendiente de ruta";
                case "RUTA_RESUELTA": return "Ruta resuelta";
                case "INSTANCIA_CREADA": return "Instancia creada";
                case "EN_CURSO": return "En curso";
                case "FINALIZADO": return "Finalizado";
                case "ERROR_WORKFLOW": return "Error de workflow";
                case "ERROR_INGRESO": return "Error de ingreso";
                default: return string.IsNullOrWhiteSpace(state) ? "Sin estado" : state;
            }
        }

        protected string EstadoBadgeClass(object value)
        {
            string state = Convert.ToString(value) ?? "";

            switch (state.ToUpperInvariant())
            {
                case "PENDIENTE_RUTA": return "badge bg-warning text-dark";
                case "RUTA_RESUELTA": return "badge bg-info text-dark";
                case "INSTANCIA_CREADA":
                case "EN_CURSO": return "badge bg-primary";
                case "FINALIZADO": return "badge bg-success";
                case "ERROR_WORKFLOW":
                case "ERROR_INGRESO": return "badge bg-danger";
                default: return "badge bg-secondary";
            }
        }

        protected bool CanResolve(object stateValue, object instanceValue)
        {
            string state = Convert.ToString(stateValue) ?? "";
            bool hasInstance = instanceValue != null && instanceValue != DBNull.Value &&
                               !string.IsNullOrWhiteSpace(Convert.ToString(instanceValue));

            return !hasInstance && string.Equals(state, "PENDIENTE_RUTA", StringComparison.OrdinalIgnoreCase);
        }

        protected bool HasInstance(object value)
        {
            return value != null && value != DBNull.Value &&
                   !string.IsNullOrWhiteSpace(Convert.ToString(value));
        }

        private void BindAll()
        {
            BindKpis();
            BindIngresos();
            if (pnlRulesAdmin.Visible)
                BindRoutes();
        }

        private void BindKpis()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    SUM(CASE WHEN Estado = N'PENDIENTE_RUTA' THEN 1 ELSE 0 END) AS Pendientes,
    SUM(CASE WHEN Estado = N'RUTA_RESUELTA' THEN 1 ELSE 0 END) AS Resueltos,
    SUM(CASE WHEN Estado IN (N'INSTANCIA_CREADA', N'EN_CURSO') THEN 1 ELSE 0 END) AS EnCurso,
    SUM(CASE WHEN Estado = N'FINALIZADO' THEN 1 ELSE 0 END) AS Finalizados,
    SUM(CASE WHEN Estado IN (N'ERROR_WORKFLOW', N'ERROR_INGRESO') THEN 1 ELSE 0 END) AS Errores
FROM dbo.WF_IngresoDocumento;";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return;

                    lblPendientes.Text = ToInt(dr["Pendientes"]).ToString(CultureInfo.InvariantCulture);
                    lblResueltos.Text = ToInt(dr["Resueltos"]).ToString(CultureInfo.InvariantCulture);
                    lblEnCurso.Text = ToInt(dr["EnCurso"]).ToString(CultureInfo.InvariantCulture);
                    lblFinalizados.Text = ToInt(dr["Finalizados"]).ToString(CultureInfo.InvariantCulture);
                    lblErrores.Text = ToInt(dr["Errores"]).ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private void BindIngresos()
        {
            string q = (txtBuscar.Text ?? "").Trim();
            string state = (ddlFiltroEstado.SelectedValue ?? "").Trim();

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP (200)
    i.Id,
    i.IngressId,
    CONVERT(varchar(16), i.FechaIngreso, 120) AS FechaIngresoFmt,
    i.CanalCodigo,
    i.ArchivoNombre,
    i.Extension,
    i.Estado,
    i.OrigenDecision,
    i.Confianza,
    i.MotivoDecision,
    i.DecisionPor,
    i.UltimoError,
    i.RutaActual,
    CASE
        WHEN i.Confianza IS NULL THEN N''
        ELSE N'Confianza ' + CONVERT(nvarchar(20), CONVERT(decimal(5,2), i.Confianza)) + N'%'
    END AS ConfianzaDisplay,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(i.DecisionPor)), N'') IS NULL THEN N''
        ELSE N'Decidido por ' + i.DecisionPor
    END AS DecisionPorDisplay,
    i.WF_DefinicionId,
    i.WF_InstanciaId,
    CASE
        WHEN d.Id IS NULL THEN N'—'
        ELSE ISNULL(NULLIF(d.Codigo, N''), N'WF') + N' · ' + d.Nombre
    END AS WorkflowDisplay,
    CASE
        WHEN r.Id IS NULL THEN N'—'
        ELSE r.Codigo + N' · ' + r.Nombre
    END AS RouteDisplay
FROM dbo.WF_IngresoDocumento i
LEFT JOIN dbo.WF_Definicion d ON d.Id = i.WF_DefinicionId
LEFT JOIN dbo.WF_IngresoRuta r ON r.Id = i.WF_IngresoRutaId
WHERE (@Estado = N'' OR i.Estado = @Estado)
  AND
  (
      @Q = N''
      OR i.ArchivoNombre LIKE N'%' + @Q + N'%'
      OR i.IngressId LIKE N'%' + @Q + N'%'
      OR i.CanalCodigo LIKE N'%' + @Q + N'%'
      OR d.Nombre LIKE N'%' + @Q + N'%'
      OR d.Codigo LIKE N'%' + @Q + N'%'
  )
ORDER BY i.Id DESC;";

                cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 40).Value = state;
                cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 260).Value = q;

                var dt = new DataTable();
                cn.Open();
                using (var da = new SqlDataAdapter(cmd))
                    da.Fill(dt);

                gvIngresos.DataSource = dt;
                gvIngresos.DataBind();
            }
        }

        private void BindRoutes()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    r.Id,
    r.Codigo,
    r.Nombre,
    ISNULL(NULLIF(r.CanalCodigo, N''), N'(Todos)') AS CanalDisplay,
    ISNULL(NULLIF(r.PatronArchivo, N''), N'(Cualquiera)') AS PatronDisplay,
    ISNULL(NULLIF(r.Extension, N''), N'(Cualquiera)') AS ExtensionDisplay,
    r.Prioridad,
    r.Activo,
    r.WF_DefinicionId,
    ISNULL(NULLIF(d.Codigo, N''), N'WF') + N' · ' + d.Nombre AS WorkflowDisplay
FROM dbo.WF_IngresoRuta r
INNER JOIN dbo.WF_Definicion d ON d.Id = r.WF_DefinicionId
ORDER BY r.Activo DESC, r.Prioridad DESC, r.Codigo;";

                var dt = new DataTable();
                cn.Open();
                using (var da = new SqlDataAdapter(cmd))
                    da.Fill(dt);

                gvRutas.DataSource = dt;
                gvRutas.DataBind();
            }
        }

        private void LoadIngresoForResolution(long ingresoId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, ArchivoNombre, CanalCodigo, MotivoDecision
FROM dbo.WF_IngresoDocumento
WHERE Id = @Id
  AND WF_InstanciaId IS NULL
  AND Estado = N'PENDIENTE_RUTA';";

                cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = ingresoId;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                    {
                        ShowMessage("El ingreso ya no está pendiente de clasificación.", true);
                        BindAll();
                        return;
                    }

                    hfIngresoId.Value = Convert.ToString(dr["Id"]);
                    lblResolverArchivo.Text = Convert.ToString(dr["ArchivoNombre"]);
                    lblResolverCanal.Text = Convert.ToString(dr["CanalCodigo"]);
                    lblResolverMotivo.Text = dr["MotivoDecision"] == DBNull.Value
                        ? "Sin ruta determinística."
                        : Convert.ToString(dr["MotivoDecision"]);
                }
            }

            ddlResolverWorkflow.SelectedIndex = 0;
            txtResolverMotivo.Text = "";
            pnlResolver.Visible = true;
        }

        private void LoadRoute(int id)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    Id, Codigo, Nombre, CanalCodigo, PatronArchivo, Extension,
    Prioridad, WF_DefinicionId, Activo
FROM dbo.WF_IngresoRuta
WHERE Id = @Id;";

                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cn.Open();

                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return;

                    hfRutaId.Value = Convert.ToString(dr["Id"]);
                    txtRutaCodigo.Text = Convert.ToString(dr["Codigo"]);
                    txtRutaNombre.Text = Convert.ToString(dr["Nombre"]);
                    txtRutaCanal.Text = dr["CanalCodigo"] == DBNull.Value ? "" : Convert.ToString(dr["CanalCodigo"]);
                    txtRutaPatron.Text = dr["PatronArchivo"] == DBNull.Value ? "" : Convert.ToString(dr["PatronArchivo"]);
                    txtRutaExtension.Text = dr["Extension"] == DBNull.Value ? "" : Convert.ToString(dr["Extension"]);
                    txtRutaPrioridad.Text = Convert.ToString(dr["Prioridad"]);
                    chkRutaActiva.Checked = Convert.ToBoolean(dr["Activo"]);

                    string defId = Convert.ToString(dr["WF_DefinicionId"]);
                    if (ddlRutaWorkflow.Items.FindByValue(defId) != null)
                        ddlRutaWorkflow.SelectedValue = defId;
                }
            }
        }

        private void ClearRouteForm()
        {
            hfRutaId.Value = "";
            txtRutaCodigo.Text = "";
            txtRutaNombre.Text = "";
            txtRutaCanal.Text = "GENERAL";
            txtRutaPatron.Text = "";
            txtRutaExtension.Text = "";
            txtRutaPrioridad.Text = "100";
            chkRutaActiva.Checked = true;
            if (ddlRutaWorkflow.Items.Count > 0)
                ddlRutaWorkflow.SelectedIndex = 0;
        }

        private void LoadDefinitions(DropDownList ddl, bool includeEmpty)
        {
            ddl.Items.Clear();
            if (includeEmpty)
                ddl.Items.Add(new ListItem("(Elegí un workflow activo)", ""));

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Id, Codigo, Nombre
FROM dbo.WF_Definicion
WHERE Activo = 1
ORDER BY Codigo, Nombre, Id;";

                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        string code = dr["Codigo"] == DBNull.Value ? "WF" : Convert.ToString(dr["Codigo"]);
                        string name = Convert.ToString(dr["Nombre"]);
                        string text = code + " · " + name + " (Id " + Convert.ToString(dr["Id"]) + ")";
                        ddl.Items.Add(new ListItem(text, Convert.ToString(dr["Id"])));
                    }
                }
            }
        }

        private bool SchemaExists()
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.WF_IngresoDocumento', N'U') IS NOT NULL
     AND OBJECT_ID(N'dbo.WF_IngresoRuta', N'U') IS NOT NULL
    THEN 1 ELSE 0 END;";

                cn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private bool CanManageRoutes()
        {
            return RbacService.HasAnyPermiso(CurrentUser(), "DOC_ABM", "WF_ADMIN");
        }

        private string CurrentUser()
        {
            return Context?.User?.Identity?.Name ?? "app";
        }

        private void ShowMessage(string message, bool error)
        {
            string css = error ? "alert-danger" : "alert-success";
            litMsg.Text = "<div class=\"alert " + css + "\">" +
                          Server.HtmlEncode(message) + "</div>";
        }

        private static void AddRouteParameters(
            SqlCommand cmd,
            int id,
            string code,
            string name,
            string channel,
            string pattern,
            string extension,
            int priority,
            int definitionId,
            bool active)
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = code;
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = name;
            cmd.Parameters.Add("@Canal", SqlDbType.NVarChar, 80).Value = (object)channel ?? DBNull.Value;
            cmd.Parameters.Add("@Patron", SqlDbType.NVarChar, 260).Value = (object)pattern ?? DBNull.Value;
            cmd.Parameters.Add("@Extension", SqlDbType.NVarChar, 20).Value = (object)extension ?? DBNull.Value;
            cmd.Parameters.Add("@Prioridad", SqlDbType.Int).Value = priority;
            cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = definitionId;
            cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = active;
        }

        private static int ToInt(object value)
        {
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static string NormalizeCode(string value)
        {
            var sb = new StringBuilder();
            foreach (char ch in (value ?? "").Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                    sb.Append(ch);
                else if (char.IsWhiteSpace(ch) && sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        private static string NormalizeNullableCode(string value)
        {
            string normalized = NormalizeCode(value);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string NormalizeExtension(string value)
        {
            string extension = (value ?? "").Trim().ToLowerInvariant();
            if (extension.Length == 0) return null;
            if (!extension.StartsWith(".")) extension = "." + extension;
            return extension;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
