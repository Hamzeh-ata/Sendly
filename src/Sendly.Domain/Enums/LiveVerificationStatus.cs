namespace Sendly.Domain.Enums;

public enum LiveVerificationStatus
{
    NotAttempted = 0,
    Accessible = 1,
    Parsed = 2,
    IdentitySupported = 3,
    Unavailable = 4,
    RobotsBlocked = 5,
    Failed = 6
}
