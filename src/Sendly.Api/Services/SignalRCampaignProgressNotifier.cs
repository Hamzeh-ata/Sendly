using Sendly.Api.Hubs;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace Sendly.Api.Services;

public class SignalRCampaignProgressNotifier : ICampaignProgressNotifier
{
    private readonly IHubContext<CampaignHub> _hubContext;

    public SignalRCampaignProgressNotifier(IHubContext<CampaignHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyProgressAsync(CampaignProgressDto progress, CancellationToken cancellationToken)
    {
        var group = CampaignHub.GetGroupName(progress.CampaignId.ToString());
        await _hubContext.Clients.Group(group).SendAsync("campaignProgress", progress, cancellationToken);
        await _hubContext.Clients.All.SendAsync("campaignProgress", progress, cancellationToken);
    }
}
