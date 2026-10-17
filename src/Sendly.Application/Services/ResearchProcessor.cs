using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class ResearchProcessor
{
    private readonly ISendlyDbContext _db;
    private readonly IEnumerable<IProspectSource> _sources;
    private readonly IAiResearchService _ai;
    private readonly ProspectDuplicateService _duplicates;
    private readonly ResearchCancellationRegistry _cancellationRegistry;
    private readonly IResearchProgressNotifier _progressNotifier;
    private readonly ResearchOptions _options;
    private readonly ILogger<ResearchProcessor> _logger;

    public ResearchProcessor(
        ISendlyDbContext db,
        IEnumerable<IProspectSource> sources,
        IAiResearchService ai,
        ProspectDuplicateService duplicates,
        ResearchCancellationRegistry cancellationRegistry,
        IResearchProgressNotifier progressNotifier,
        IOptions<ResearchOptions> options,
        ILogger<ResearchProcessor> logger)
    {
        _db = db;
        _sources = sources;
        _ai = ai;
        _duplicates = duplicates;
        _cancellationRegistry = cancellationRegistry;
        _progressNotifier = progressNotifier;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid researchRunId, CancellationToken stoppingToken)
    {
        var runToken = _cancellationRegistry.GetOrCreate(researchRunId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runToken);
        var cancellationToken = linked.Token;

        var run = await _db.ResearchRuns.FirstOrDefaultAsync(x => x.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        if (run.Status is ResearchRunStatus.Cancelled or ResearchRunStatus.Completed or ResearchRunStatus.CompletedWithFailures or ResearchRunStatus.Failed)
        {
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        var project = await _db.OutreachProjects
            .FirstOrDefaultAsync(x => x.Id == run.ProjectId, cancellationToken);
        if (project is null)
        {
            run.Status = ResearchRunStatus.Failed;
            run.Error = "Project was not found.";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(run, null, cancellationToken);
            _cancellationRegistry.Clear(researchRunId);
            return;
        }

        project.Keywords = await _db.OutreachProjectKeywords
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Countries = await _db.OutreachProjectCountries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Industries = await _db.OutreachProjectIndustries
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.TargetRoles = await _db.OutreachProjectTargetRoles
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        project.Templates = await _db.ProjectTemplates
            .Include(x => x.Template)
            .Where(x => x.ProjectId == project.Id)
            .ToListAsync(cancellationToken);

        run.Status = ResearchRunStatus.Running;
        run.StartedAtUtc = DateTimeOffset.UtcNow;
        run.ProgressPercent = 5;
        await _db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(run, project.Name, cancellationToken);
        _logger.LogInformation("Research run {ResearchRunId} started", researchRunId);

        var providerFailures = new List<string>();
        var discoveryState = new ResearchDiscoveryState
        {
            StartedAtUtc = run.StartedAtUtc ?? DateTimeOffset.UtcNow
        };

        try
        {
            var context = BuildSourceContext(project, run, discoveryState);
            context.DiscoveryBatchSize = ResolveDiscoveryBatchSize(run.RequestedProspectCount);
            context.ReportProgressAsync = async (state, token) =>
            {
                ApplyDiscoveryStats(run, state);
                run.CompaniesFound = Math.Max(run.CompaniesFound, state.CandidatesDiscovered);
                run.ContactsFound = Math.Max(run.ContactsFound, state.CandidatesAfterDeduplication);
                run.ProgressPercent = ResolveProgress(run, state);
                await _db.SaveChangesAsync(token);
                await NotifyAsync(run, project.Name, token);
            };

            if (!_sources.Any(x => x.IsEnabled))
            {
                discoveryState.LimitsExhausted = true;
                discoveryState.LimitsExhaustedReason = "No research providers are enabled.";
            }

            while (ShouldContinueResearch(run, discoveryState))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await DiscoverNextBatchAsync(context, providerFailures, cancellationToken);
                if (batch.Count == 0)
                {
                    if (AreAllSourcesCompleted(discoveryState) || discoveryState.LimitsExhausted)
                    {
                        break;
                    }

                    discoveryState.LimitsExhausted = true;
                    discoveryState.LimitsExhaustedReason ??= "No additional candidates could be discovered.";
                    break;
                }

                ApplyDiscoveryStats(run, discoveryState);
                run.CompaniesFound = Math.Max(run.CompaniesFound, CountDistinctCompanies(batch) + run.ProspectsSaved);
                run.ContactsFound = Math.Max(run.ContactsFound, run.CandidatesAfterDeduplication);
                run.ProgressPercent = ResolveProgress(run, discoveryState);
                await _db.SaveChangesAsync(cancellationToken);
                await NotifyAsync(run, project.Name, cancellationToken);

                var index = 0;
                foreach (var candidate in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (run.QualifiedCount >= run.RequestedProspectCount)
                    {
                        break;
                    }

                    await ProcessCandidateAsync(project, run, candidate, discoveryState, cancellationToken);
                    index++;
                    run.ProgressPercent = ResolveProgress(run, discoveryState);
                    ApplyDiscoveryStats(run, discoveryState);
                    await _db.SaveChangesAsync(cancellationToken);
                    await NotifyAsync(run, project.Name, cancellationToken);
                }

                if (run.QualifiedCount >= run.RequestedProspectCount)
                {
                    break;
                }

                if (IsDurationExceeded(discoveryState))
                {
                    discoveryState.LimitsExhausted = true;
                    discoveryState.LimitsExhaustedReason ??= "Maximum research duration reached";
                    break;
                }
            }

            ApplyDiscoveryStats(run, discoveryState);
            FinalizeRunStatus(run, discoveryState, providerFailures);
            run.ProgressPercent = 100;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyAsync(run, project.Name, cancellationToken);
            _logger.LogInformation(
                "Research completed {ResearchRunId} qualified={Qualified}/{Target} queries={Queries} results={Results}",
                researchRunId,
                run.QualifiedCount,
                run.RequestedProspectCount,
                run.SearchQueriesExecuted,
                run.SearchResultsReceived);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ApplyDiscoveryStats(run, discoveryState);
            run.Status = ResearchRunStatus.Cancelled;
            run.Warning = "Research run was cancelled.";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ApplyDiscoveryStats(run, discoveryState);
            run.Status = ResearchRunStatus.Failed;
            run.Error = IsTimeoutCancellation(ex)
                ? "Research failed because a provider or website request timed out. The run was not cancelled by the user."
                : ex.Message;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(run, project.Name, CancellationToken.None);
            _logger.LogError(ex, "Research failed {ResearchRunId}", researchRunId);
        }
        finally
        {
            _cancellationRegistry.Clear(researchRunId);
        }
    }

    private async Task<List<ProspectCandidate>> DiscoverNextBatchAsync(
        ProspectSourceContext context,
        List<string> providerFailures,
        CancellationToken cancellationToken)
    {
        var batch = new List<ProspectCandidate>();
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!source.IsEnabled)
            {
                continue;
            }

            if (context.DiscoveryState.CompletedSourceNames.Contains(source.Name)
                && !string.Equals(source.Name, "WebSearch", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var discovered = await source.DiscoverAsync(context, cancellationToken);
                batch.AddRange(discovered);
                if (!string.Equals(source.Name, "WebSearch", StringComparison.OrdinalIgnoreCase)
                    && discovered.Count == 0)
                {
                    context.DiscoveryState.CompletedSourceNames.Add(source.Name);
                }

                _logger.LogInformation(
                    "Candidate discovered batch from {Source}: {Count}",
                    source.Name,
                    discovered.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                providerFailures.Add($"{source.Name}: {ex.Message}");
                context.DiscoveryState.CompletedSourceNames.Add(source.Name);
                _logger.LogWarning(ex, "Provider failed {Source}", source.Name);
            }
        }

        return ProspectCandidateDeduplicator.DeduplicateAndMerge(batch);
    }

    private static bool ShouldContinueResearch(ResearchRun run, ResearchDiscoveryState state)
    {
        if (run.QualifiedCount >= run.RequestedProspectCount)
        {
            return false;
        }

        if (state.LimitsExhausted)
        {
            return false;
        }

        return true;
    }

    private static bool AreAllSourcesCompleted(ResearchDiscoveryState state)
    {
        return state.CompletedSourceNames.Contains("WebSearch")
               || state.LimitsExhausted;
    }

    private bool IsDurationExceeded(ResearchDiscoveryState state)
    {
        var minutes = Math.Clamp(_options.MaxResearchDurationMinutes, 1, 240);
        return DateTimeOffset.UtcNow - state.StartedAtUtc > TimeSpan.FromMinutes(minutes);
    }

    private static void FinalizeRunStatus(
        ResearchRun run,
        ResearchDiscoveryState state,
        List<string> providerFailures)
    {
        if (run.QualifiedCount >= run.RequestedProspectCount)
        {
            run.Status = providerFailures.Count > 0
                ? ResearchRunStatus.CompletedWithFailures
                : ResearchRunStatus.Completed;
            if (providerFailures.Count > 0)
            {
                run.Warning = string.Join("; ", providerFailures);
            }

            return;
        }

        run.ResearchLimitsReached = state.LimitsExhausted || AreAllSourcesCompleted(state);
        run.ResearchLimitsReachedReason = state.LimitsExhaustedReason
            ?? (run.CandidatesDiscovered == 0
                ? "No candidates were returned by configured research providers."
                : "Research limits reached before target prospects were found.");

        var summary =
            $"{run.RequestedProspectCount} requested, {run.QualifiedCount} qualified prospects found. {run.ResearchLimitsReachedReason}";

        if (providerFailures.Count > 0)
        {
            run.Status = ResearchRunStatus.CompletedWithFailures;
            run.Warning = $"{summary} Providers: {string.Join("; ", providerFailures)}";
            return;
        }

        if (run.CandidatesDiscovered == 0)
        {
            run.Status = ResearchRunStatus.Completed;
            run.Warning = summary;
            return;
        }

        run.Status = ResearchRunStatus.CompletedWithFailures;
        run.Warning = summary;
    }

    private static void ApplyDiscoveryStats(ResearchRun run, ResearchDiscoveryState state)
    {
        run.SearchQueriesExecuted = state.SearchQueriesExecuted;
        run.SearchResultsReceived = state.SearchResultsReceived;
        run.CandidatesDiscovered = state.CandidatesDiscovered;
        run.CandidatesAfterDeduplication = Math.Max(run.CandidatesAfterDeduplication, state.CandidatesAfterDeduplication);
        run.CandidatesValidated = state.CandidatesValidated;
        run.ProspectsSaved = state.ProspectsSaved;
        run.ProspectsSkippedAsDuplicates = state.ProspectsSkippedAsDuplicates;
        run.ResearchLimitsReached = state.LimitsExhausted;
        run.ResearchLimitsReachedReason = state.LimitsExhaustedReason;
        run.RejectionReasonsJson = state.SerializeRejectionReasons();
        run.SearchProviderDiagnosticsJson = state.SerializeProviderDiagnostics();
    }

    private int ResolveDiscoveryBatchSize(int requestedCount)
    {
        var oversample = Math.Clamp(_options.DiscoveryOversampleFactor, 1, 10);
        var target = Math.Max(8, Math.Min(15, requestedCount / 3));
        return Math.Clamp(target * Math.Max(1, oversample / 2), 8, 20);
    }

    private static int ResolveProgress(ResearchRun run, ResearchDiscoveryState? state = null)
    {
        if (run.RequestedProspectCount <= 0)
        {
            return 5;
        }

        var qualifiedRatio = run.QualifiedCount / (double)run.RequestedProspectCount;
        var qualifiedProgress = (int)Math.Floor(qualifiedRatio * 55);

        var queries = state?.SearchQueriesExecuted ?? run.SearchQueriesExecuted;
        var maxQueries = Math.Max(1, queries > 0 ? Math.Max(queries, 10) : 50);
        var queryProgress = (int)Math.Floor(Math.Min(queries / (double)maxQueries, 1.0) * 15);

        var discovered = state?.CandidatesDiscovered ?? run.CandidatesDiscovered;
        var discoveryTarget = Math.Max(run.RequestedProspectCount, 1);
        var discoveryProgress = (int)Math.Floor(Math.Min(discovered / (double)(discoveryTarget * 2), 1.0) * 15);

        var validated = state?.CandidatesValidated ?? run.CandidatesValidated;
        var validatedProgress = (int)Math.Floor(Math.Min(validated / (double)discoveryTarget, 1.0) * 10);

        return Math.Clamp(5 + queryProgress + discoveryProgress + validatedProgress + qualifiedProgress, 5, 95);
    }

    private async Task NotifyAsync(ResearchRun run, string? projectName, CancellationToken cancellationToken)
    {
        var prospects = await _db.Prospects.AsNoTracking()
            .Where(x => x.ResearchRunId == run.Id)
            .ToListAsync(cancellationToken);
        prospects = prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToList();
        var detail = ResearchRunMapper.MapDetail(run, projectName, prospects);
        await _progressNotifier.NotifyProgressAsync(detail, cancellationToken);
    }

    private async Task ProcessCandidateAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.CompanyName))
        {
            run.RejectedCount++;
            state.RecordRejection("Missing company name");
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.SourceUrl) && string.IsNullOrWhiteSpace(candidate.SourceName))
        {
            run.RejectedCount++;
            state.RecordRejection("Insufficient evidence");
            _logger.LogInformation("Prospect rejected missing source for {Company}", candidate.CompanyName);
            return;
        }

        var email = NormalizeEmailOrNull(candidate.Email);
        var phone = ProspectNormalizer.NormalizePhone(candidate.Phone);
        var whatsApp = ProspectNormalizer.NormalizeWhatsApp(candidate.WhatsApp) ?? phone;
        state.CandidatesValidated++;

        var match = await _duplicates.FindMatchAsync(
            new ProspectDuplicateLookupRequest
            {
                ProjectId = project.Id,
                CompanyName = candidate.CompanyName,
                Website = candidate.Website,
                City = candidate.City,
                Email = email,
                Phone = candidate.Phone,
                WhatsApp = candidate.WhatsApp,
                SourceUrl = candidate.SourceUrl,
                ExcludeProspectId = null,
                TrackExistingForMerge = true
            },
            cancellationToken);

        if (match.Kind != ProspectDuplicateKind.None)
        {
            run.DuplicatesFound++;
            _logger.LogInformation("Duplicate detected {Kind} for {Company}", match.Kind, candidate.CompanyName);
        }

        if (match.ShouldSkip
            || match.Kind == ProspectDuplicateKind.DoNotContact
            || (project.DuplicateHandling == DuplicateHandlingMode.Exclude
                && match.Kind is ProspectDuplicateKind.EmailContacted
                    or ProspectDuplicateKind.WhatsAppContacted
                    or ProspectDuplicateKind.CrossProject
                    or ProspectDuplicateKind.DoNotContact))
        {
            state.ProspectsSkippedAsDuplicates++;
            state.RecordRejection("Duplicate");
            return;
        }

        if (project.DuplicateHandling == DuplicateHandlingMode.Exclude
            && match.ShouldMerge
            && match.Kind is not ProspectDuplicateKind.SameProject
                and not ProspectDuplicateKind.SameCompany
                and not ProspectDuplicateKind.SameWebsite
                and not ProspectDuplicateKind.SamePhone
                and not ProspectDuplicateKind.PossibleDuplicate)
        {
            state.ProspectsSkippedAsDuplicates++;
            state.RecordRejection("Duplicate");
            return;
        }

        var classification = new AiClassificationRequest
        {
            ProjectName = project.Name,
            ProjectDescription = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            CompanyName = candidate.CompanyName.Trim(),
            Website = TrimOrNull(candidate.Website),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            Industry = TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(candidate.ContactRole),
            Email = email,
            WhatsApp = whatsApp,
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes)
        };

        AiQualificationResult qualification;
        try
        {
            qualification = await _ai.QualifyAsync(classification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI qualification failed for {Company}", candidate.CompanyName);
            qualification = new AiQualificationResult
            {
                IsRelevant = true,
                NeedsReview = true,
                IsQualified = false,
                QualificationScore = 50,
                Summary = "Needs review because AI qualification was unavailable.",
                Industry = candidate.Industry,
                CompanySize = candidate.CompanySize,
                ContactRole = candidate.ContactRole,
                EmailConfidenceScore = email is null ? null : 50,
                WhatsAppConfidenceScore = whatsApp is null ? null : 50
            };
        }

        if (match.NeedsReview)
        {
            qualification.NeedsReview = true;
        }

        if (!qualification.IsRelevant || qualification.QualificationScore < Math.Min(run.MinQualificationScore, 40))
        {
            run.RejectedCount++;
            state.RecordRejection(ResolveRejectionReason(qualification, candidate));
            _logger.LogInformation("Prospect rejected {Company}", candidate.CompanyName);
            if (project.DuplicateHandling == DuplicateHandlingMode.Allow
                || match.Kind == ProspectDuplicateKind.None
                || match.ShouldMerge)
            {
                await PersistProspectAsync(
                    project,
                    run,
                    candidate,
                    email,
                    whatsApp,
                    qualification,
                    ProspectStatus.Rejected,
                    match,
                    null,
                    state,
                    cancellationToken);
            }

            return;
        }

        var hasRequiredContact = HasAcceptableContact(run, email, whatsApp);
        var status = ResolveStatus(
            qualification,
            run.MinQualificationScore,
            match,
            project.DuplicateHandling,
            hasRequiredContact);

        if (!hasRequiredContact
            && status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach or ProspectStatus.NeedsReview)
        {
            qualification.NeedsReview = true;
            status = ProspectStatus.NeedsReview;
            qualification.Summary = AppendNote(
                qualification.Summary,
                "Business qualified; contact method still missing (email or WhatsApp).");
        }

        if (status == ProspectStatus.Qualified || status == ProspectStatus.ReadyForOutreach)
        {
            if (!match.ShouldMerge)
            {
                run.QualifiedCount++;
            }

            _logger.LogInformation("Prospect qualified {Company}", candidate.CompanyName);
        }
        else if (status == ProspectStatus.NeedsReview
                 && qualification.IsQualified
                 && qualification.QualificationScore >= run.MinQualificationScore
                 && !match.ShouldMerge)
        {
            run.QualifiedCount++;
        }
        else if (status == ProspectStatus.Rejected)
        {
            run.RejectedCount++;
            state.RecordRejection(ResolveRejectionReason(qualification, candidate));
        }

        if (!string.IsNullOrWhiteSpace(email) && !match.ShouldMerge)
        {
            run.ValidEmails++;
        }

        if (!string.IsNullOrWhiteSpace(whatsApp) && !match.ShouldMerge)
        {
            run.ValidWhatsApp++;
        }

        var recommendation = await RecommendTemplateSafeAsync(project, classification, qualification, cancellationToken);
        await PersistProspectAsync(
            project,
            run,
            candidate,
            email,
            whatsApp,
            qualification,
            status,
            match,
            recommendation,
            state,
            cancellationToken);
    }

    private static bool HasAcceptableContact(ResearchRun run, string? email, string? whatsApp)
    {
        if (!run.IncludeEmail && !run.IncludeWhatsApp)
        {
            return true;
        }

        if (run.IncludeEmail && !string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        if (run.IncludeWhatsApp && !string.IsNullOrWhiteSpace(whatsApp))
        {
            return true;
        }

        return false;
    }

    private static string ResolveRejectionReason(AiQualificationResult qualification, ProspectCandidate candidate)
    {
        var summary = qualification.Summary ?? string.Empty;
        if (summary.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return "Business closed";
        }

        if (summary.Contains("supplier", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("equipment", StringComparison.OrdinalIgnoreCase))
        {
            return "Equipment supplier";
        }

        if (summary.Contains("personal trainer", StringComparison.OrdinalIgnoreCase))
        {
            return "Personal trainer only";
        }

        if (summary.Contains("outside", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("country", StringComparison.OrdinalIgnoreCase))
        {
            return "Outside target country";
        }

        if (!qualification.IsRelevant)
        {
            return "Low project fit";
        }

        if (string.IsNullOrWhiteSpace(candidate.Website) && string.IsNullOrWhiteSpace(candidate.SourceUrl))
        {
            return "Insufficient evidence";
        }

        return "Low project fit";
    }

    private static string AppendNote(string? existing, string note)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return note;
        }

        if (existing.Contains(note, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing.Trim()} {note}";
    }

    private async Task PersistProspectAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        string? email,
        string? whatsApp,
        AiQualificationResult qualification,
        ProspectStatus status,
        ProspectDuplicateMatch match,
        AiTemplateRecommendationResult? recommendation,
        ResearchDiscoveryState state,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var savedNew = await PersistProspectCoreAsync(
                    project,
                    run,
                    candidate,
                    email,
                    whatsApp,
                    qualification,
                    status,
                    match,
                    recommendation,
                    cancellationToken);
                if (savedNew)
                {
                    state.ProspectsSaved++;
                }

                return;
            }
            catch (DbUpdateException ex) when (attempt < 3 && IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(
                    ex,
                    "Concurrent prospect insert conflict for {Company}; retrying merge",
                    candidate.CompanyName);

                DetachAddedProspects();
                match = await _duplicates.FindMatchAsync(
                    new ProspectDuplicateLookupRequest
                    {
                        ProjectId = project.Id,
                        CompanyName = candidate.CompanyName,
                        Website = candidate.Website,
                        City = candidate.City,
                        Email = email,
                        Phone = candidate.Phone,
                        WhatsApp = candidate.WhatsApp,
                        SourceUrl = candidate.SourceUrl,
                        ExcludeProspectId = null,
                        TrackExistingForMerge = true
                    },
                    cancellationToken);

                if (match.ExistingProspect is null && !match.ShouldMerge)
                {
                    throw;
                }
            }
        }
    }

    private async Task<bool> PersistProspectCoreAsync(
        OutreachProject project,
        ResearchRun run,
        ProspectCandidate candidate,
        string? email,
        string? whatsApp,
        AiQualificationResult qualification,
        ProspectStatus status,
        ProspectDuplicateMatch match,
        AiTemplateRecommendationResult? recommendation,
        CancellationToken cancellationToken)
    {
        var mergeInput = BuildMergeInput(candidate, email, whatsApp);
        var personalizationJson = await ExtractPersonalizationSafeAsync(candidate, email, cancellationToken);

        if (match.ShouldMerge && match.ExistingProspect is not null)
        {
            var existing = match.ExistingProspect;
            ProspectMergeHelper.MergeCandidateInto(existing, mergeInput);
            ApplyAdditionalContacts(existing, candidate);
            ProspectMergeHelper.ApplyQualificationMerge(
                existing,
                TrimOrNull(qualification.Summary),
                qualification.QualificationScore,
                qualification.EmailConfidenceScore,
                qualification.WhatsAppConfidenceScore,
                status,
                status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach,
                match.Kind,
                run.Id,
                recommendation?.TemplateId,
                recommendation?.Reason,
                recommendation?.Confidence);

            if (!string.IsNullOrWhiteSpace(personalizationJson) && string.IsNullOrWhiteSpace(existing.PersonalizationJson))
            {
                existing.PersonalizationJson = personalizationJson;
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Merged duplicate prospect {ProspectId} for {Company}",
                existing.Id,
                existing.CompanyName);
            return false;
        }

        var prospect = new Prospect
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            CompanyName = candidate.CompanyName.Trim(),
            NormalizedCompanyName = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName),
            Website = TrimOrNull(candidate.Website),
            NormalizedWebsite = ProspectNormalizer.NormalizeWebsite(candidate.Website)
                ?? ProspectNormalizer.NormalizeWebsite(candidate.SourceUrl),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            NormalizedCity = ProspectNormalizer.NormalizeCity(candidate.City),
            Industry = TrimOrNull(qualification.Industry) ?? TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(qualification.CompanySize) ?? TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(qualification.ContactRole) ?? TrimOrNull(candidate.ContactRole),
            Email = email,
            NormalizedEmail = email is null ? null : EmailAddressParser.Normalize(email),
            Phone = TrimOrNull(candidate.Phone),
            NormalizedPhone = ProspectNormalizer.NormalizePhone(candidate.Phone),
            WhatsApp = TrimOrNull(candidate.WhatsApp),
            NormalizedWhatsApp = whatsApp,
            LinkedInUrl = TrimOrNull(candidate.LinkedInUrl),
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            SourceUrlsJson = ProspectSourceUrlStore.Serialize(
                [candidate.SourceUrl, ..candidate.AdditionalSourceUrls]),
            SourceType = candidate.SourceType,
            SourceName = TrimOrNull(candidate.SourceName),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes),
            QualificationSummary = TrimOrNull(qualification.Summary),
            QualificationScore = qualification.QualificationScore,
            EmailConfidenceScore = qualification.EmailConfidenceScore,
            WhatsAppConfidenceScore = qualification.WhatsAppConfidenceScore,
            IsQualified = (status is ProspectStatus.Qualified or ProspectStatus.ReadyForOutreach
                    || (status == ProspectStatus.NeedsReview && qualification.IsQualified))
                && qualification.QualificationScore >= run.MinQualificationScore,
            Status = status,
            DuplicateKind = match.Kind,
            RecommendedTemplateId = recommendation?.TemplateId,
            RecommendedTemplateReason = recommendation?.Reason,
            RecommendedTemplateConfidence = recommendation?.Confidence,
            PersonalizationJson = personalizationJson
        };

        ApplyAdditionalContacts(prospect, candidate);
        _db.Prospects.Add(prospect);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ApplyAdditionalContacts(Prospect prospect, ProspectCandidate candidate)
    {
        foreach (var extraEmail in candidate.AdditionalEmails)
        {
            ProspectMergeHelper.MergeCandidateInto(
                prospect,
                new ProspectMergeInput
                {
                    CompanyName = prospect.CompanyName,
                    Email = extraEmail
                });
        }

        foreach (var extraPhone in candidate.AdditionalPhones)
        {
            ProspectMergeHelper.MergeCandidateInto(
                prospect,
                new ProspectMergeInput
                {
                    CompanyName = prospect.CompanyName,
                    Phone = extraPhone
                });
        }
    }

    private async Task<string?> ExtractPersonalizationSafeAsync(
        ProspectCandidate candidate,
        string? email,
        CancellationToken cancellationToken)
    {
        try
        {
            var personalization = await _ai.ExtractPersonalizationAsync(
                new AiClassificationRequest
                {
                    CompanyName = candidate.CompanyName,
                    ContactName = candidate.ContactName,
                    ContactRole = candidate.ContactRole,
                    Industry = candidate.Industry,
                    City = candidate.City,
                    Country = candidate.Country,
                    Email = email
                },
                cancellationToken);
            return PersonalizationSerializer.Serialize(personalization.Values);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Personalization extract failed for {Company}", candidate.CompanyName);
            return null;
        }
    }

    private void DetachAddedProspects()
    {
        if (_db is not DbContext ef)
        {
            return;
        }

        foreach (var entry in ef.ChangeTracker.Entries<Prospect>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unique", StringComparison.OrdinalIgnoreCase)
               || message.Contains("constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static ProspectMergeInput BuildMergeInput(ProspectCandidate candidate, string? email, string? whatsApp)
    {
        return new ProspectMergeInput
        {
            CompanyName = candidate.CompanyName.Trim(),
            Website = TrimOrNull(candidate.Website),
            Country = TrimOrNull(candidate.Country),
            City = TrimOrNull(candidate.City),
            Industry = TrimOrNull(candidate.Industry),
            CompanyDescription = TrimOrNull(candidate.CompanyDescription),
            CompanySize = TrimOrNull(candidate.CompanySize),
            ContactName = TrimOrNull(candidate.ContactName),
            ContactRole = TrimOrNull(candidate.ContactRole),
            Email = email,
            Phone = TrimOrNull(candidate.Phone),
            WhatsApp = TrimOrNull(candidate.WhatsApp) ?? whatsApp,
            LinkedInUrl = TrimOrNull(candidate.LinkedInUrl),
            SourceUrl = TrimOrNull(candidate.SourceUrl),
            AdditionalSourceUrls = [..candidate.AdditionalSourceUrls],
            SourceType = candidate.SourceType,
            SourceName = TrimOrNull(candidate.SourceName),
            ResearchNotes = TrimOrNull(candidate.ResearchNotes)
        };
    }

    private async Task<AiTemplateRecommendationResult?> RecommendTemplateSafeAsync(
        OutreachProject project,
        AiClassificationRequest classification,
        AiQualificationResult qualification,
        CancellationToken cancellationToken)
    {
        if (project.Templates.Count == 0)
        {
            return null;
        }

        try
        {
            return await _ai.RecommendTemplateAsync(
                new AiTemplateRecommendationRequest
                {
                    Industry = qualification.Industry ?? classification.Industry,
                    CompanySize = qualification.CompanySize ?? classification.CompanySize,
                    CompanyName = classification.CompanyName,
                    ContactRole = qualification.ContactRole ?? classification.ContactRole,
                    Country = classification.Country,
                    Templates = project.Templates.Select(x => new AiTemplateOption
                    {
                        TemplateId = x.TemplateId,
                        Name = x.Template?.Name ?? x.TemplateId.ToString(),
                        Category = x.Template?.Category,
                        MatchIndustry = x.MatchIndustry,
                        MatchCompanySize = x.MatchCompanySize,
                        MatchNotes = x.MatchNotes
                    }).ToList()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Template recommendation failed");
            return null;
        }
    }

    private static ProspectStatus ResolveStatus(
        AiQualificationResult qualification,
        int minScore,
        ProspectDuplicateMatch match,
        DuplicateHandlingMode duplicateHandling,
        bool hasRequiredContact)
    {
        if (match.Kind == ProspectDuplicateKind.DoNotContact)
        {
            return ProspectStatus.DoNotContact;
        }

        if (match.NeedsReview
            || (match.Kind != ProspectDuplicateKind.None && duplicateHandling == DuplicateHandlingMode.Warn))
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.NeedsReview)
        {
            return ProspectStatus.NeedsReview;
        }

        if (qualification.IsQualified && qualification.QualificationScore >= minScore)
        {
            return hasRequiredContact ? ProspectStatus.ReadyForOutreach : ProspectStatus.NeedsReview;
        }

        if (qualification.IsRelevant)
        {
            return ProspectStatus.NeedsReview;
        }

        return ProspectStatus.Rejected;
    }

    private ProspectSourceContext BuildSourceContext(
        OutreachProject project,
        ResearchRun run,
        ResearchDiscoveryState discoveryState)
    {
        var context = new ProspectSourceContext
        {
            ProjectId = project.Id,
            ResearchRunId = run.Id,
            ProjectName = project.Name,
            Description = project.Description,
            ResearchInstructions = project.ResearchInstructions,
            Keywords = project.Keywords.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedKeywords = project.Keywords.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            Countries = project.Countries.Select(x => x.Name).ToList(),
            Industries = project.Industries.Where(x => !x.IsExcluded).Select(x => x.Value).ToList(),
            ExcludedIndustries = project.Industries.Where(x => x.IsExcluded).Select(x => x.Value).ToList(),
            TargetRoles = project.TargetRoles.Select(x => x.Role).ToList(),
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            RequestedCount = run.RequestedProspectCount,
            DiscoveryState = discoveryState
        };

        context.Locations = ResearchSearchQueryGenerator.ResolveLocations(context);
        return context;
    }

    private static int CountDistinctCompanies(List<ProspectCandidate> candidates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var name = ProspectNormalizer.NormalizeCompanyName(candidate.CompanyName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                set.Add(name);
            }
        }

        return set.Count;
    }

    private static string? NormalizeEmailOrNull(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var parsed = EmailAddressParser.Parse(email);
        return parsed.ValidEmails.Count == 0 ? null : parsed.ValidEmails[0];
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsTimeoutCancellation(Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException or OperationCanceledException)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase);
    }
}
