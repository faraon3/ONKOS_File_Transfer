using System;

namespace ONKOS.Core.Models
{
    public sealed class PeerInfo
    {
        public string FullName { get; set; }
        public string Area { get; set; }
        public string IPv4 { get; set; }
        public string MachineName { get; set; }
        public string FingerprintSha256 { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public string DisplayText => string.IsNullOrWhiteSpace(Area)
            ? FullName
            : $"{FullName} — {Area}";

        public override string ToString() => DisplayText;
    }
}
