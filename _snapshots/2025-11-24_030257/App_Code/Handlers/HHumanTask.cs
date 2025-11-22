using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo "human.task":
    ///  - Primera pasada: crea un registro en WF_Tarea y DETIENE el motor.
    ///  - Reanudación: si viene WF_SEED.humanTask.*, NO crea tarea nueva,
    ///    expone el resultado en ctx.Estado y deja seguir el flujo.
    /// </summary>
    public class HHumanTask : IManejadorNodo
    {
        public string TipoNodo => "human.task";

        private static string Cnn =>
            ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        // OJO: sin "async" porque no usamos await
        public Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>();

            // === 1) Instancia obligatoria ===
            long instanciaId = 0;
            if (ctx.Estado.TryGetValue("wf.instanceId", out var instObj) && instObj != null)
                long.TryParse(Convert.ToString(instObj), out instanciaId);

            if (instanciaId <= 0)
                throw new InvalidOperationException("human.task: wf.instanceId no presente en el contexto.");

            // === 2) ¿Venimos en modo REANUDAR? ===
            if (EsReanudacion(ctx, nodo, instanciaId))
            {
                // No se crea tarea nueva, el motor sigue.
                return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
            }

            // === 3) MODO CREACIÓN: se crea WF_Tarea y se detiene ===

            string titulo = GetString(p, "titulo") ?? "Tarea humana";
            string descripcion = GetString(p, "descripcion");
            string rolDestino = GetString(p, "rol") ?? GetString(p, "rolDestino");
            string usuarioAsig = GetString(p, "usuarioAsignado");
            int deadlineMin = GetInt(p, "deadlineMinutes", 0);

            DateTime? fechaVto = null;
            if (deadlineMin > 0)
                fechaVto = DateTime.Now.AddMinutes(deadlineMin);

            string datosJson = BuildMetadataJson(p, instanciaId, nodo.Id);

            long tareaId = CrearTarea(
                instanciaId,
                nodo.Id,
                nodo.Type ?? "human.task",
                titulo,
                descripcion,
                rolDestino,
                usuarioAsig,
                fechaVto,
                datosJson
            );

            ctx.Log($"[human.task] tarea creada para instancia {instanciaId} (nodo {nodo.Id}) id={tareaId}");

            // Marcamos que el motor debe detenerse aquí (se lee como wf.detener en MotorDemo)
            ctx.Estado["wf.detener"] = true;

            return Task.FromResult(new ResultadoEjecucion { Etiqueta = "always" });
        }

        /// <summary>
        /// Lógica de reanudación: consume WF_SEED.humanTask.*
        /// y deja información en ctx.Estado atada al nodo actual.
        /// </summary>
        private static bool EsReanudacion(ContextoEjecucion ctx, NodeDef nodo, long instanciaId)
        {
            var st = ctx.Estado;

            if (!st.TryGetValue("humanTask.id", out var idObj) || idObj == null)
                return false;

            if (!long.TryParse(Convert.ToString(idObj), out var tareaId) || tareaId <= 0)
                return false;

            // Levantamos resultado y datos (los puso WorkflowRuntime.ReanudarDesdeTareaAsync)
            st.TryGetValue("humanTask.result", out var resObj);
            st.TryGetValue("humanTask.data", out var dataObj);
            st.TryGetValue("humanTask.dataRaw", out var dataRawObj);

            // Exponemos en el estado, pero "scoped" al nodo
            st[$"human.{nodo.Id}.tareaId"] = tareaId;
            if (resObj != null)
                st[$"human.{nodo.Id}.resultado"] = resObj;
            if (dataObj != null)
                st[$"human.{nodo.Id}.datos"] = dataObj;
            if (dataRawObj != null)
                st[$"human.{nodo.Id}.datosRaw"] = dataRawObj;

            ctx.Log($"[human.task] retomando tarea {tareaId} para instancia {instanciaId} (nodo {nodo.Id})");

            // Consumimos el seed para que OTROS human.task creen nuevas tareas
            st.Remove("humanTask.id");
            st.Remove("humanTask.result");
            st.Remove("humanTask.data");
            st.Remove("humanTask.dataRaw");

            // Importante: NO detiene el motor
            st["wf.detener"] = false;

            return true;
        }

        private static long CrearTarea(
            long instanciaId,
            string nodoId,
            string nodoTipo,
            string titulo,
            string descripcion,
            string rolDestino,
            string usuarioAsignado,
            DateTime? fechaVencimiento,
            string datosJson)
        {
            using (var cn = new SqlConnection(Cnn))
            using (var cmd = new SqlCommand(@"
INSERT INTO dbo.WF_Tarea
    (WF_InstanciaId, NodoId, NodoTipo,
     Titulo, Descripcion,
     RolDestino, UsuarioAsignado,
     Estado, Resultado,
     FechaCreacion, FechaVencimiento, FechaCierre,
     Datos)
VALUES
    (@InstId, @NodoId, @NodoTipo,
     @Titulo, @Descripcion,
     @RolDestino, @UsuarioAsignado,
     'Pendiente', NULL,
     GETDATE(), @FechaVenc, NULL,
     @Datos);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", cn))
            {
                cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = (object)nodoId ?? DBNull.Value;
                cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = (object)nodoTipo ?? DBNull.Value;

                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = (object)titulo ?? DBNull.Value;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar).Value = (object)descripcion ?? DBNull.Value;

                cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value = (object)rolDestino ?? DBNull.Value;
                cmd.Parameters.Add("@UsuarioAsignado", SqlDbType.NVarChar, 100).Value = (object)usuarioAsignado ?? DBNull.Value;

                if (fechaVencimiento.HasValue)
                    cmd.Parameters.Add("@FechaVenc", SqlDbType.DateTime).Value = fechaVencimiento.Value;
                else
                    cmd.Parameters.Add("@FechaVenc", SqlDbType.DateTime).Value = DBNull.Value;

                cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosJson ?? DBNull.Value;

                cn.Open();
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private static string BuildMetadataJson(Dictionary<string, object> p, long instanciaId, string nodoId)
        {
            object meta;
            p.TryGetValue("metadata", out meta);

            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["origen"] = "workflow",
                ["instanciaId"] = instanciaId.ToString(),
                ["nodoId"] = nodoId
            };

            // Si metadata viene como JObject o Dictionary, lo mergeamos
            if (meta is Newtonsoft.Json.Linq.JObject jo)
            {
                var extra = jo.ToObject<Dictionary<string, object>>();
                if (extra != null)
                {
                    foreach (var kv in extra)
                        dict[kv.Key] = kv.Value;
                }
            }
            else if (meta is Dictionary<string, object> d)
            {
                foreach (var kv in d)
                    dict[kv.Key] = kv.Value;
            }

            return JsonConvert.SerializeObject(dict, Formatting.None);
        }

        private static string GetString(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            if (!d.TryGetValue(k, out var v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static int GetInt(Dictionary<string, object> d, string k, int def = 0)
        {
            if (d == null) return def;
            if (!d.TryGetValue(k, out var v) || v == null) return def;
            return int.TryParse(Convert.ToString(v), out var i) ? i : def;
        }
    }
}
