using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// Nodo de tarea humana: crea un registro en WF_Tarea y detiene el motor.
    /// Usa:
    ///   - wf.instanceId  desde ctx.Estado (cargado por WF_SEED en WorkflowRuntime)
    ///   - parámetros del nodo:
    ///       titulo          (string, admite ${...})
    ///       descripcion     (string, admite ${...})
    ///       rol             (string, RolDestino)
    ///       usuarioAsignado (string opcional)
    ///       deadlineMinutes (int opcional)
    ///       fechaVencimiento(string opcional, parseable a DateTime)
    ///       metadata        (objeto/JObject, se guarda en WF_Tarea.Datos como JSON)
    /// </summary>
    public class HHumanTask : IManejadorNodo
    {
        public string TipoNodo => "human.task";

        public async Task<ResultadoEjecucion> EjecutarAsync(
            ContextoEjecucion ctx,
            NodeDef nodo,
            CancellationToken ct)
        {
            var p = nodo.Parameters ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // -------- 1) Resolver textos con templating --------
            string tituloRaw = GetString(p, "titulo") ?? $"Tarea {nodo.Id ?? "sin-id"}";
            string descRaw = GetString(p, "descripcion");
            string rolRaw = GetString(p, "rol");
            string usuarioRaw = GetString(p, "usuarioAsignado");
            string resultadoDefault = GetString(p, "resultadoPorDefecto");

            // Usamos TemplateUtil/ExpandString para interpolar ${...} contra ctx.Estado
            string titulo = TemplateUtil.Expand(ctx, tituloRaw);
            string descripcion = TemplateUtil.Expand(ctx, descRaw);
            string rolDestino = TemplateUtil.Expand(ctx, rolRaw);
            string usuarioAsignado = TemplateUtil.Expand(ctx, usuarioRaw);

            // -------- 2) Calcular FechaVencimiento --------
            DateTime? fechaVenc = null;

            int deadlineMinutes = GetInt(p, "deadlineMinutes", 0);
            if (deadlineMinutes > 0)
            {
                fechaVenc = DateTime.Now.AddMinutes(deadlineMinutes);
            }

            string fechaVencStr = TemplateUtil.Expand(ctx, GetString(p, "fechaVencimiento"));
            if (!string.IsNullOrWhiteSpace(fechaVencStr) &&
                DateTime.TryParse(fechaVencStr, out var fvFromParam))
            {
                fechaVenc = fvFromParam;
            }

            // -------- 3) Metadata → WF_Tarea.Datos (JSON) --------
            string datosJson = null;
            if (p.TryGetValue("metadata", out var metaObj) && metaObj != null)
            {
                if (metaObj is JToken tok)
                {
                    // Expandir strings de 1er nivel dentro del objeto
                    if (tok.Type == JTokenType.Object)
                    {
                        foreach (var prop in ((JObject)tok).Properties())
                        {
                            if (prop.Value.Type == JTokenType.String)
                            {
                                prop.Value = TemplateUtil.Expand(ctx, prop.Value.ToString());
                            }
                        }
                    }
                    datosJson = tok.ToString(Formatting.None);
                }
                else
                {
                    datosJson = JsonConvert.SerializeObject(metaObj);
                }
            }

            // -------- 4) Obtener wf.instanceId del contexto --------
            long instanciaId = 0;
            if (ctx.Estado.TryGetValue("wf.instanceId", out var instObj))
            {
                long.TryParse(Convert.ToString(instObj), out instanciaId);
            }

            // -------- 5) Insertar en WF_Tarea (si hay instancia real) --------
            if (instanciaId > 0)
            {
                string cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                using (var cn = new SqlConnection(cs))
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO dbo.WF_Tarea
    (WF_InstanciaId,
     NodoId,
     NodoTipo,
     Titulo,
     Descripcion,
     RolDestino,
     UsuarioAsignado,
     Estado,
     Resultado,
     FechaCreacion,
     FechaVencimiento,
     Datos)
VALUES
    (@InstId,
     @NodoId,
     @NodoTipo,
     @Titulo,
     @Descripcion,
     @RolDestino,
     @UsuarioAsignado,
     @Estado,
     @Resultado,
     GETDATE(),
     @FechaVencimiento,
     @Datos);";

                    string nodoId = nodo.Id ?? "";
                    string nodoTipo = nodo.Type ?? "";

                    cmd.Parameters.Add("@InstId", SqlDbType.BigInt).Value = instanciaId;
                    cmd.Parameters.Add("@NodoId", SqlDbType.NVarChar, 50).Value = nodoId;
                    cmd.Parameters.Add("@NodoTipo", SqlDbType.NVarChar, 100).Value = nodoTipo;
                    cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = (object)titulo ?? DBNull.Value;
                    cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar).Value = (object)descripcion ?? DBNull.Value;
                    cmd.Parameters.Add("@RolDestino", SqlDbType.NVarChar, 100).Value = (object)rolDestino ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioAsignado", SqlDbType.NVarChar, 100).Value = (object)usuarioAsignado ?? DBNull.Value;
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = "Pendiente";
                    cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 50).Value = (object)resultadoDefault ?? DBNull.Value;
                    if (fechaVenc.HasValue)
                        cmd.Parameters.Add("@FechaVencimiento", SqlDbType.DateTime).Value = fechaVenc.Value;
                    else
                        cmd.Parameters.Add("@FechaVencimiento", SqlDbType.DateTime).Value = DBNull.Value;
                    cmd.Parameters.Add("@Datos", SqlDbType.NVarChar).Value = (object)datosJson ?? DBNull.Value;

                    await cn.OpenAsync(ct);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                ctx.Log("[human.task] tarea creada para instancia " + instanciaId + " (nodo " + (nodo.Id ?? "?") + ")");
            }
            else
            {
                // Caso típico: ejecución desde WorkflowUI → btnProbarMotor (sin instancia).
                ctx.Log("[human.task] wf.instanceId no definido; NO se creó WF_Tarea (modo prueba).");
            }

            // -------- 6) Señalar al runtime que debe detener el flujo --------
            ctx.Estado["wf.detener"] = true;

            // (MotorFlujo ya debería copiar ctx.Estado a HttpContext.Current.Items["WF_CTX_ESTADO"]
            //  en cada iteración, como vimos en WorkflowRuntime.)

            return new ResultadoEjecucion
            {
                Etiqueta = "wait",   // para edges que quieran cablear un camino específico
                Detener = true       // propiedad nueva de ResultadoEjecucion
            };
        }

        private static string GetString(Dictionary<string, object> p, string key)
        {
            if (p == null) return null;
            return p.TryGetValue(key, out var v) ? Convert.ToString(v) : null;
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def = 0)
        {
            if (p == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;
            return int.TryParse(Convert.ToString(v), out var i) ? i : def;
        }
    }
}
