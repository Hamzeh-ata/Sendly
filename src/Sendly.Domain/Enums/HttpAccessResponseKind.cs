namespace Sendly.Domain.Enums;

public enum HttpAccessResponseKind
{
    ValidResults = 0,
    EmptyResults = 1,
    Forbidden = 2,
    RateLimited = 3,
    CaptchaChallenge = 4,
    BotVerification = 5,
    ConsentInterstitial = 6,
    LoginRequired = 7,
    NetworkTimeout = 8,
    InvalidHtml = 9,
    UnexpectedFormat = 10,
    TransientError = 11
}
