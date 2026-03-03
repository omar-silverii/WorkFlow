using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.UI;

namespace Intranet.WorkflowStudio.WebForms
{
    public partial class _Default : BasePage
    {
        protected override string[] RequiredPermissions => new[] { "DASH" };

        private class DocLastRow
        {
            public long WF_InstanciaId { get; set; }
            public string Tipo { get; set; }
            public string DocumentoId { get; set; }
            public DateTime FechaAlta { get; set; }
            public string FechaAltaFmt { get; set; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
           

            if (!IsPostBack)
            {
                BindActividadDocumental48h();
                CargarKpiEntidades();
            }
        }

        private void CargarKpiEntidades()
        {
            try
            {
                string cnn = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                using (var cn = new SqlConnection(cnn))
                using (var cmd = new SqlCommand(@"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN EstadoActual = 'Iniciado' THEN 1 ELSE 0 END) AS Iniciado,
    SUM(CASE WHEN EstadoActual = 'Finalizado' THEN 1 ELSE 0 END) AS Finalizado,
    SUM(CASE WHEN EstadoActual = 'Error' THEN 1 ELSE 0 END) AS Error
FROM dbo.WF_Entidad;", cn))
                {
                    cn.Open();
                    using (var rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            lblEntTotal.Text = Convert.ToString(rd["Total"] ?? 0);
                            lblEntIniciado.Text = Convert.ToString(rd["Iniciado"] ?? 0);
                            lblEntFinalizado.Text = Convert.ToString(rd["Finalizado"] ?? 0);
                            lblEntError.Text = Convert.ToString(rd["Error"] ?? 0);
                        }
                    }
                }
            }
            catch
            {
                lblEntTotal.Text = "-";
                lblEntIniciado.Text = "-";
                lblEntFinalizado.Text = "-";
                lblEntError.Text = "-";
            }
        }

        private void BindActividadDocumental48h()
        {
            var cnn = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

            // Resumen
            string sqlResumen = @"
SELECT
  COUNT(*) AS DocsAdjuntados,
  COUNT(DISTINCT WF_InstanciaId) AS InstanciasConDocs
FROM dbo.WF_InstanciaDocumento
WHERE FechaAlta >= DATEADD(HOUR, -48, GETDATE());";

            // Últimos 5
            string sqlLast = @"
SELECT TOP 5
  WF_InstanciaId,
  Tipo,
  DocumentoId,
  FechaAlta
FROM dbo.WF_InstanciaDocumento
ORDER BY FechaAlta DESC;";

            int docs48 = 0;
            int inst48 = 0;

            using (var cn = new SqlConnection(cnn))
            {
                cn.Open();

                using (var cmd = new SqlCommand(sqlResumen, cn))
                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                    {
                        docs48 = Convert.ToInt32(rd["DocsAdjuntados"]);
                        inst48 = Convert.ToInt32(rd["InstanciasConDocs"]);
                    }
                }

                lblDocCount48.Text = docs48.ToString();
                lblInstCount48.Text = inst48.ToString();

                var rows = new List<DocLastRow>();
                using (var cmd = new SqlCommand(sqlLast, cn))
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        var dt = (rd["FechaAlta"] == DBNull.Value) ? DateTime.MinValue : Convert.ToDateTime(rd["FechaAlta"]);
                        rows.Add(new DocLastRow
                        {
                            WF_InstanciaId = Convert.ToInt64(rd["WF_InstanciaId"]),
                            Tipo = Convert.ToString(rd["Tipo"] ?? ""),
                            DocumentoId = Convert.ToString(rd["DocumentoId"] ?? ""),
                            FechaAlta = dt,
                            FechaAltaFmt = (dt == DateTime.MinValue) ? "" : dt.ToString("dd/MM/yyyy HH:mm:ss")
                        });
                    }
                }

                if (rows.Count > 0)
                {
                    rptDocLast.DataSource = rows;
                    rptDocLast.DataBind();
                    pnlDocLastEmpty.Visible = false;
                }
                else
                {
                    rptDocLast.DataSource = null;
                    rptDocLast.DataBind();
                    pnlDocLastEmpty.Visible = true;
                }
            }
        }
    }
}
