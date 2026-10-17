using Microsoft.AspNetCore.SignalR;

namespace Sendly.Api.Hubs;

public class ResearchHub : Hub
{
    public async Task JoinResearchRun(string researchRunId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(researchRunId));
    }

    public async Task LeaveResearchRun(string researchRunId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(researchRunId));
    }

    public static string GetGroupName(string researchRunId) => $"research:{researchRunId}";
}
