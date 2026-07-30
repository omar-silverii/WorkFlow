using System;

namespace Intranet.WorkflowStudio.Dispatcher.WatchFolder
{
    internal sealed class WatchFolderReceipt
    {
        public int Version { get; set; }
        public string IngressId { get; set; }
        public string ChannelCode { get; set; }
        public string OriginalFileName { get; set; }
        public string CurrentFilePath { get; set; }
        public string FinalFilePath { get; set; }

        public string WorkflowName { get; set; }
        public int? DefinitionId { get; set; }
        public int? RouteId { get; set; }
        public string RouteSource { get; set; }
        public string RouteReason { get; set; }
        public decimal? RouteConfidence { get; set; }

        public long? InstanceId { get; set; }
        public string InstanceState { get; set; }
        public string DispatcherState { get; set; }
        public string ClaimedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public string LastError { get; set; }

        public static WatchFolderReceipt Create(
            string documentPath,
            string originalFileName,
            string channelCode,
            string workflowName)
        {
            var now = DateTime.UtcNow.ToString("o");

            return new WatchFolderReceipt
            {
                Version = 2,
                IngressId = Guid.NewGuid().ToString("N"),
                ChannelCode = string.IsNullOrWhiteSpace(channelCode)
                    ? "GENERAL"
                    : channelCode.Trim().ToUpperInvariant(),
                OriginalFileName = originalFileName,
                CurrentFilePath = documentPath,
                WorkflowName = workflowName,
                DispatcherState = "Claimed",
                ClaimedUtc = now,
                UpdatedUtc = now
            };
        }
    }
}
