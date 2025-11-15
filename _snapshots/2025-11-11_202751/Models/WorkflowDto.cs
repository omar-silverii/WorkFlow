// Models/WorkflowDto.cs
using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.Models
{
    // Lo que exporta tu JS
    public class WorkflowDto
    {
        public string StartNodeId { get; set; }
        public Dictionary<string, WorkflowNodeDto> Nodes { get; set; }
        public List<WorkflowEdgeDto> Edges { get; set; }
    }

    public class WorkflowNodeDto
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class WorkflowEdgeDto
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Condition { get; set; }
    }
}
