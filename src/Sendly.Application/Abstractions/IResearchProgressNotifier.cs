using Sendly.Application.DTOs;

namespace Sendly.Application.Abstractions;

public interface IResearchProgressNotifier
{
    Task NotifyProgressAsync(ResearchRunDetailDto progress, CancellationToken cancellationToken);
}
