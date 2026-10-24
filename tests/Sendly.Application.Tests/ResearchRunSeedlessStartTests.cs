using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Sendly.Application.Tests;

public class ResearchRunSeedlessStartTests
{
    [Fact]
    public async Task StartAsync_WithoutSeeds_CreatesRunWhenIndustryAndCountryExist()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, withScope: true);
        var queue = new Mock<IResearchQueue>();
        Guid? enqueuedId = null;
        queue
            .Setup(x => x.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => enqueuedId = id)
            .Returns(ValueTask.CompletedTask);

        var service = CreateService(db, queue.Object);
        var result = await service.StartAsync(
            project.Id,
            new StartResearchRequestDto
            {
                IncludeEmail = true,
                IncludeWhatsApp = false,
                RequestedProspectCount = 10
            },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(ResearchRunStatus.Pending, result.Status);
        Assert.NotNull(enqueuedId);
        Assert.Equal(result.Id, enqueuedId);

        var queryCount = await db.ResearchQueries.CountAsync(x => x.ProjectId == project.Id);
        Assert.Equal(1, queryCount);
        Assert.Equal(0, await db.ResearchSeedUrls.CountAsync());
    }

    [Fact]
    public async Task StartAsync_WithoutScope_ThrowsWithoutAskingForSeeds()
    {
        await using var db = CreateDb();
        var project = await SeedProjectAsync(db, withScope: false);
        var service = CreateService(db, new Mock<IResearchQueue>().Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(
                project.Id,
                new StartResearchRequestDto { IncludeEmail = true },
                CancellationToken.None));

        Assert.Contains("industry or country", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Seed URLs are not required", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.ResearchRuns.CountAsync());
    }

    private static ResearchRunService CreateService(SendlyDbContext db, IResearchQueue queue)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ResearchOptions
        {
            MaxCandidatesPerRun = 200,
            MaxPagesPerCrawl = 5,
            EnableIndependentCrawler = true,
            EnableSearchProvider = false
        });

        return new ResearchRunService(
            db,
            queue,
            options,
            new ResearchCancellationRegistry(),
            NullLogger<ResearchRunService>.Instance);
    }

    private static async Task<OutreachProject> SeedProjectAsync(SendlyDbContext db, bool withScope)
    {
        var project = new OutreachProject
        {
            Name = "Cleaning SA",
            IsActive = true,
            EnableEmail = true,
            DefaultRequestedProspectCount = 25,
            MinQualificationScore = 70
        };
        db.OutreachProjects.Add(project);

        if (withScope)
        {
            db.OutreachProjectCountries.Add(new OutreachProjectCountry
            {
                ProjectId = project.Id,
                Name = "Saudi Arabia"
            });
            db.OutreachProjectIndustries.Add(new OutreachProjectIndustry
            {
                ProjectId = project.Id,
                Value = "Commercial cleaning",
                IsExcluded = false
            });
        }

        await db.SaveChangesAsync();
        return project;
    }

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }
}
