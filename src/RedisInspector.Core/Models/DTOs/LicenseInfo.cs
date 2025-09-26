using RedisInspector.CLI.src.RedisInspector.Core.Models.Enums;

namespace RedisInspector.CLI.src.RedisInspector.Core.Models.DTOs
{
    public class LicenseInfo
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Tier { get; set; } = "Free"; // "Free" | "Pro"
        public DateTime ExpUtc { get; set; } = DateTime.UtcNow.AddYears(100);
        public string Sig { get; set; } = ""; // base64 signature

        public LicenseTier TierEnum => string.Equals(Tier, "Pro", StringComparison.OrdinalIgnoreCase) ? LicenseTier.Pro : LicenseTier.Free;
    }
}