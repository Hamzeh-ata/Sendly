using Sendly.Application.DTOs;

namespace Sendly.Application.Abstractions;

public interface ICampaignProgressNotifier
{
    Task NotifyProgressAsync(CampaignProgressDto progress, CancellationToken cancellationToken);
}
