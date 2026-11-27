using System;

namespace ONKOS.Core.Models
{
    public sealed class TransferHistoryEntry
    {
        public DateTime TimestampLocal { get; set; }
        public string Direction { get; set; }
        public string ItemName { get; set; }
        public long SizeBytes { get; set; }
        public string Counterparty { get; set; }
        public string Status { get; set; }
        public string TargetPath { get; set; }
        public string Sha256 { get; set; }
        public string Notes { get; set; }
    }
}
