using Intranet.WorkflowStudio.WebForms;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.Runtime
{
    public static class WorkflowRuntime
    {
        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        public static async Task<long> ReejecutarInstanciaAsync(long instanciaId, string usuario)
        {
            int defId;
            string datosEntrada;

            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(
                "SELECT WF_DefinicionId, DatosEntrada FROM dbo.WF_Instancia WHERE Id=@Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", instanciaId);
                cn.Open();
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read())
                        throw new InvalidOperationException("Instancia no encontrada: " + instanciaId);

                    defId = dr.GetInt32(0);
                    datosEntrada = dr.IsDBNull(1) ? null : dr.GetString(1);
                }
            }

            // Crea una NUEVA instancia usando la misma definición y los mismos datos de entrada
            return await CrearInstanciaYEjecutarAsync(defId, datosEntrada, usuario);
        }




        public static async Task<long> CrearInstanciaYEjecutarAsync(
            int defId,
            string datosEntradaJson,
            string usuario)
        {
            string jsonDef = CargarJsonDefinicion(defId);
            if (string.IsNullOrWhiteSpace(jsonDef))
                throw new InvalidOperationException("Definición no encontrada: " + defId);

            long instId = CrearInstancia(defId, datosEntradaJson, usuario);

            var wf = MotorDemo.FromJson(jsonDef);
            var logs = new List<string>();

            // acá le decimos al motor que, además de los básicos, agregue el SQL
            var handlers = MotorDemo.CrearHandlersPorDefecto();
            handlers.Add(new ManejadorSql());

            await MotorDemo.EjecutarAsync(
                wf,
                s => logs.Add(s),
                handlersExtra: new IManejadorNodo[] { new ManejadorSql() },
                ct: CancellationToken.None
            );

            string datosContexto =
                JsonConvert.SerializeObject(new { logs }, Formatting.None);
            CerrarInstanciaOk(instId, datosContexto);

            return instId;
        }

        private static string CargarJsonDefinicion(int defId)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(
                       "SELECT JsonDef FROM dbo.WF_Definicion WHERE Id=@Id", cn))
            {
                cmd.Parameters.AddWithValue("@Id", defId);
                cn.Open();
                var o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? null : o.ToString();
            }
        }

        private static long CrearInstancia(int defId, string datos, string usuario)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Instancia
    (WF_DefinicionId, Estado, FechaInicio, DatosEntrada, CreadoPor)
VALUES
    (@DefId, 'EnCurso', GETDATE(), @Datos, @User);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@DefId", SqlDbType.Int).Value = defId;
                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datos ?? DBNull.Value;
                cmd.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value =
                    (object)usuario ?? "app";
                cn.Open();
                return (long)cmd.ExecuteScalar();
            }
        }

        private static void GuardarLog(long instId, string nivel, string mensaje,
            string nodoId, string nodoTipo)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_InstanciaLog
    (WF_InstanciaId, FechaLog, Nivel, Mensaje, NodoId, NodoTipo)
VALUES
    (@InstId, GETDATE(), @Nivel, @Msg, @NodoId, @NodoTipo);", cn))
            {
                cmd.Parameters.AddWithValue("@InstId", instId);
                cmd.Parameters.AddWithValue("@Nivel", (object)nivel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Msg", (object)mensaje ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NodoId", (object)nodoId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NodoTipo", (object)nodoTipo ?? DBNull.Value);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void CerrarInstanciaOk(long instId, string datosContexto)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
UPDATE dbo.WF_Instancia
SET Estado = 'Finalizado',
    FechaFin = GETDATE(),
    DatosContexto = @Ctx
WHERE Id = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Ctx", (object)datosContexto ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", instId);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
