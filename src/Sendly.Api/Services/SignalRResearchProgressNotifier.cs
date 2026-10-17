using Sendly.Api.Hubs;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace Sendly.Api.Services;

public class SignalRResearchProgressNotifier : IResearchProgressNotifier
{
    private readonly IHubContext<ResearchHub> _hubContext;

    public SignalRResearchProgressNotifier(IHubContext<ResearchHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyProgressAsync(ResearchRunDetailDto progress, CancellationToken cancellationToken)
    {
        var group = ResearchHub.GetGroupName(progress.Id.ToString());
        await _hubContext.Clients.Group(group).SendAsync("researchProgress", progress, cancellationToken);
        await _hubContext.Clients.All.SendAsync("researchProgress", progress, cancellationToken);
    }
}
