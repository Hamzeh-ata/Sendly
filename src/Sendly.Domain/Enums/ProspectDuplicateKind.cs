namespace Sendly.Domain.Enums;

public enum ProspectDuplicateKind
{
    None = 0,
    SameProject = 1,
    SameCompany = 2,
    EmailContacted = 3,
    WhatsAppContacted = 4,
    CrossProject = 5,
    DoNotContact = 6,
    SameWebsite = 7,
    SamePhone = 8,
    PossibleDuplicate = 9
}
