using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    public class WfAiAssistantRequest
    {
        public string UserText { get; set; }
        public string WorkflowJson { get; set; }
        public string ConversationId { get; set; }
    }

    public class WfAiCatalog
    {
        public List<WfAiNodeInfo> Nodes { get; set; }
        public List<WfAiDocTypeInfo> DocTypes { get; set; }
        public List<string> Roles { get; set; }
        public List<WfAiUserInfo> Users { get; set; }
        public List<WfAiFieldInfo> Fields { get; set; }
        public List<string> Warnings { get; set; }

        public WfAiCatalog()
        {
            Nodes = new List<WfAiNodeInfo>();
            DocTypes = new List<WfAiDocTypeInfo>();
            Roles = new List<string>();
            Users = new List<WfAiUserInfo>();
            Fields = new List<WfAiFieldInfo>();
            Warnings = new List<string>();
        }
    }

    public class WfAiNodeInfo
    {
        public string Type { get; set; }
        public string Label { get; set; }
        public List<string> Params { get; set; }

        public WfAiNodeInfo()
        {
            Params = new List<string>();
        }
    }

    public class WfAiDocTypeInfo
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public string ContextPrefix { get; set; }
        public string MotorExtraccion { get; set; }
    }

    public class WfAiUserInfo
    {
        public string UserKey { get; set; }
        public string DisplayName { get; set; }
    }

    public class WfAiFieldInfo
    {
        public string Path { get; set; }
        public string Label { get; set; }
        public string DocTipo { get; set; }
    }

    public class WfAiValidationResult
    {
        public bool Ok { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }

        public WfAiValidationResult()
        {
            Ok = true;
            Errors = new List<string>();
            Warnings = new List<string>();
        }
    }

    public class WfAiLocalModelResult
    {
        public bool Ok { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string RawText { get; set; }
        public JObject Plan { get; set; }
        public string ErrorMessage { get; set; }
    }
}
