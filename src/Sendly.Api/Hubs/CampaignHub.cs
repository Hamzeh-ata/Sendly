using Microsoft.AspNetCore.SignalR;

namespace Sendly.Api.Hubs;

public class CampaignHub : Hub
{
    public async Task JoinCampaign(string campaignId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(campaignId));
    }

    public async Task LeaveCampaign(string campaignId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(campaignId));
    }

    public static string GetGroupName(string campaignId) => $"campaign:{campaignId}";
}
