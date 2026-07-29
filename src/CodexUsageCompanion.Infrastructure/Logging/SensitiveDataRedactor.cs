using System.Text.RegularExpressions;

namespace CodexUsageCompanion.Infrastructure.Logging;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = BearerPattern().Replace(value, "$1[REDACTED]");
        redacted = SecretAssignmentPattern().Replace(redacted, "$1[REDACTED]");
        redacted = JwtPattern().Replace(redacted, "[REDACTED-JWT]");
        redacted = SupabaseServiceRolePattern().Replace(redacted, "[REDACTED-KEY]");
        return redacted;
    }

    [GeneratedRegex(
        @"(?i)\b(authorization\s*[:=]\s*bearer\s+)[^\s,;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|cookie|password|secret|api[_-]?key)\s*[:=]\s*[""']?[^,\s;""']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(
        @"\b(?:sbp_|service_role_)[A-Za-z0-9_-]{12,}\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SupabaseServiceRolePattern();
}
