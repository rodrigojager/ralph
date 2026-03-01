namespace Ralph.Engines.Abstractions;

public enum DetectedErrorKind
{
    Unknown,
    Auth,
    RateLimit,
    Network,
    CommandNotFound
}
