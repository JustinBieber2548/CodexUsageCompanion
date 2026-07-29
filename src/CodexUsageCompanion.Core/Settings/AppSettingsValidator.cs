namespace CodexUsageCompanion.Core.Settings;

public static class AppSettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();

        if (settings.SchemaVersion is <= 0 or > AppSettings.CurrentSchemaVersion)
        {
            errors.Add("The settings schema is unsupported.");
        }

        if (!Guid.TryParse(settings.DeviceId, out _))
        {
            errors.Add("DeviceId must be a valid UUID.");
        }

        if (string.IsNullOrWhiteSpace(settings.DeviceLabel) || settings.DeviceLabel.Length > 100)
        {
            errors.Add("DeviceLabel is required and must not exceed 100 characters.");
        }

        ValidateMinutes(settings.ActiveRefreshMinutes, nameof(settings.ActiveRefreshMinutes), 1, 60, errors);
        ValidateMinutes(settings.IdleRefreshMinutes, nameof(settings.IdleRefreshMinutes), 1, 120, errors);
        ValidateMinutes(settings.StaleAfterMinutes, nameof(settings.StaleAfterMinutes), 1, 1440, errors);
        ValidateMinutes(settings.OfflineAfterMinutes, nameof(settings.OfflineAfterMinutes), 1, 1440, errors);

        if (settings.OfflineAfterMinutes < settings.StaleAfterMinutes)
        {
            errors.Add("OfflineAfterMinutes must be greater than or equal to StaleAfterMinutes.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add("TimeZoneId is not recognized.");
        }
        catch (InvalidTimeZoneException)
        {
            errors.Add("TimeZoneId is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(settings.SupabaseUrl)
            && (!Uri.TryCreate(settings.SupabaseUrl, UriKind.Absolute, out var supabaseUri)
                || supabaseUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("SupabaseUrl must be an absolute HTTPS URL.");
        }

        return errors;
    }

    public static void ThrowIfInvalid(AppSettings settings)
    {
        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(" ", errors));
        }
    }

    private static void ValidateMinutes(
        int value,
        string name,
        int minimum,
        int maximum,
        ICollection<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{name} must be between {minimum} and {maximum} minutes.");
        }
    }
}
