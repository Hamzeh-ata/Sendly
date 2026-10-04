namespace Sendly.Application.Services;

public class CampaignCancellationRegistry
{
    private readonly Dictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly object _sync = new();

    public CancellationToken GetOrCreate(Guid campaignId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(campaignId, out var existing))
            {
                return existing.Token;
            }

            var cts = new CancellationTokenSource();
            _tokens[campaignId] = cts;
            return cts.Token;
        }
    }

    public void Cancel(Guid campaignId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(campaignId, out var existing))
            {
                if (!existing.IsCancellationRequested)
                {
                    existing.Cancel();
                }

                return;
            }

            var cts = new CancellationTokenSource();
            cts.Cancel();
            _tokens[campaignId] = cts;
        }
    }

    public void Clear(Guid campaignId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(campaignId, out var existing))
            {
                existing.Dispose();
                _tokens.Remove(campaignId);
            }
        }
    }
}
