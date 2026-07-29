namespace CodexUsageCompanion.Core.Settings;

public enum ReaderMode
{
    Mock,
    ExperimentalOAuth,
    CliRpc
}

public enum MockScenario
{
    Normal,
    LowUsage,
    Reset,
    Stale,
    AuthenticationFailure,
    NetworkFailure,
    Unsupported
}
