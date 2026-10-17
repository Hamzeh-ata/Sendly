namespace Sendly.Application.Services;

public class ResearchCancellationRegistry
{
    private readonly Dictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly object _sync = new();

    public CancellationToken GetOrCreate(Guid researchRunId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(researchRunId, out var existing))
            {
                return existing.Token;
            }

            var cts = new CancellationTokenSource();
            _tokens[researchRunId] = cts;
            return cts.Token;
        }
    }

    public void Cancel(Guid researchRunId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(researchRunId, out var existing))
            {
                if (!existing.IsCancellationRequested)
                {
                    existing.Cancel();
                }

                return;
            }

            var cts = new CancellationTokenSource();
            cts.Cancel();
            _tokens[researchRunId] = cts;
        }
    }

    public void Clear(Guid researchRunId)
    {
        lock (_sync)
        {
            if (_tokens.TryGetValue(researchRunId, out var existing))
            {
                existing.Dispose();
                _tokens.Remove(researchRunId);
            }
        }
    }
}
