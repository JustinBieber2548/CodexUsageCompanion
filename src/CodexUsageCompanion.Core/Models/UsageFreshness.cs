namespace CodexUsageCompanion.Core.Models;

public enum UsageFreshness
{
    Fresh,
    Stale,
    Offline,
    AuthenticationRequired,
    UnsupportedSource,
    ReaderError
}
