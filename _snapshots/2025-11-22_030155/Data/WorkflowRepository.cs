// Data/WorkflowRepository.cs
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using Intranet.WorkflowStudio.Models;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.Data
{
    public class WorkflowRepository
    {
        private readonly string _cs;

        public WorkflowRepository()
        {
            // lee <connectionStrings> del web.config
            _cs = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        }

        // 1) crear workflow (cabecera)
        public int CreateWorkflow(string name, string createdBy)
        {
            using (var cn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
INSERT INTO Workflow (Name, IsActive, CreatedAt, CreatedBy)
VALUES (@Name, 1, GETDATE(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS INT);", cn))
            {
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@CreatedBy", (object)createdBy ?? DBNull.Value);
                cn.Open();
                return (int)cmd.ExecuteScalar();
            }
        }

        // 2) guardar versión + nodos + aristas en una sola transacción
        public int SaveWorkflowVersion(int workflowId, WorkflowDto dto, string createdBy)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            var json = JsonConvert.SerializeObject(dto);

            using (var cn = new SqlConnection(_cs))
            {
                cn.Open();
                using (var tx = cn.BeginTransaction())
                {
                    try
                    {
                        // 2.1 insertar versión
                        int versionId;
                        using (var cmd = new SqlCommand(@"
INSERT INTO WorkflowVersion (WorkflowId, VersionNumber, JsonPayload, CreatedAt, CreatedBy)
VALUES (@WorkflowId,
       (SELECT ISNULL(MAX(VersionNumber),0)+1 FROM WorkflowVersion WHERE WorkflowId=@WorkflowId),
       @JsonPayload,
       GETDATE(),
       @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS INT);", cn, tx))
                        {
                            cmd.Parameters.AddWithValue("@WorkflowId", workflowId);
                            cmd.Parameters.AddWithValue("@JsonPayload", json);
                            cmd.Parameters.AddWithValue("@CreatedBy", (object)createdBy ?? DBNull.Value);
                            versionId = (int)cmd.ExecuteScalar();
                        }

                        // 2.2 insertar nodos
                        if (dto.Nodes != null)
                        {
                            foreach (var kv in dto.Nodes)
                            {
                                var node = kv.Value;
                                var paramsJson = node.Parameters != null
                                    ? JsonConvert.SerializeObject(node.Parameters)
                                    : "{}";

                                using (var cmd = new SqlCommand(@"
INSERT INTO WorkflowNode (WorkflowVersionId, NodeId, NodeType, Label, ParamsJson)
VALUES (@WorkflowVersionId, @NodeId, @NodeType, @Label, @ParamsJson);
", cn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@WorkflowVersionId", versionId);
                                    cmd.Parameters.AddWithValue("@NodeId", node.Id);
                                    cmd.Parameters.AddWithValue("@NodeType", (object)node.Type ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@Label", node.Id); // si querés otra cosa, cambiamos
                                    cmd.Parameters.AddWithValue("@ParamsJson", paramsJson);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        // 2.3 insertar aristas
                        if (dto.Edges != null)
                        {
                            foreach (var e in dto.Edges)
                            {
                                using (var cmd = new SqlCommand(@"
INSERT INTO WorkflowEdge (WorkflowVersionId, FromNodeId, ToNodeId, Condition)
VALUES (@WorkflowVersionId, @FromNodeId, @ToNodeId, @Condition);
", cn, tx))
                                {
                                    cmd.Parameters.AddWithValue("@WorkflowVersionId", versionId);
                                    cmd.Parameters.AddWithValue("@FromNodeId", e.From);
                                    cmd.Parameters.AddWithValue("@ToNodeId", e.To);
                                    cmd.Parameters.AddWithValue("@Condition", (object)e.Condition ?? "always");
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }

                        tx.Commit();
                        return versionId;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        // 3) traer últimas N cabeceras
        public List<(int Id, string Name, DateTime CreatedAt)> GetLastWorkflows(int top = 20)
        {
            var list = new List<(int, string, DateTime)>();
            using (var cn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
SELECT TOP (@Top) Id, Name, CreatedAt
FROM Workflow
ORDER BY CreatedAt DESC;", cn))
            {
                cmd.Parameters.Add("@Top", SqlDbType.Int).Value = top;
                cn.Open();
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                    {
                        list.Add((
                            rd.GetInt32(0),
                            rd.GetString(1),
                            rd.GetDateTime(2)
                        ));
                    }
                }
            }
            return list;
        }

        // 4) traer la última versión de un workflow como JSON (para que el front la pinte)
        public string GetLatestWorkflowJson(int workflowId)
        {
            using (var cn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
SELECT TOP 1 JsonPayload
FROM WorkflowVersion
WHERE WorkflowId = @WorkflowId
ORDER BY VersionNumber DESC;", cn))
            {
                cmd.Parameters.AddWithValue("@WorkflowId", workflowId);
                cn.Open();
                var val = cmd.ExecuteScalar();
                return val == null || val == DBNull.Value ? null : (string)val;
            }
        }
    }
}
