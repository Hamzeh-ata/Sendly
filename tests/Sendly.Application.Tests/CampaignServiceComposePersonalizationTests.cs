using Microsoft.EntityFrameworkCore;
using Moq;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Security;

namespace Sendly.Application.Tests;

public class CampaignServiceComposePersonalizationTests
{
    [Fact]
    public async Task CreateDraftAsync_WithTemplateAndRawRecipients_FillsMergeDataFromMatchingProspect()
    {
        await using var db = CreateDb();
        var account = await SeedAccountAsync(db);
        var template = await SeedTemplateAsync(db, account);
        var prospect = await SeedProspectAsync(db, template);

        var service = CreateService(db);
        var campaign = await service.CreateDraftAsync(new CreateCampaignRequestDto
        {
            Subject = "PlayWatch for {{CompanyName}}",
            HtmlBody = "<p>Hello,</p><p>For {{CompanyName}} in {{City}}, {{Country}}</p>",
            RawRecipients = "owner@acme-billiards.com\nunknown@example.com",
            TemplateId = template.Id,
            EmailAccountId = account.Id
        }, CancellationToken.None);

        var saved = await db.Campaigns.Include(x => x.Recipients).FirstAsync(x => x.Id == campaign.Id);
        var known = saved.Recipients.First(x => x.Email == "owner@acme-billiards.com");
        Assert.Equal(prospect.Id, known.ProspectId);
        Assert.Equal("Sara Owner", known.DisplayName);
        Assert.Equal("Acme Billiards", known.CompanyName);

        var values = TemplateMergeRenderer.BuildValues(
            known.Email,
            known.DisplayName,
            known.CompanyName,
            null,
            null,
            null,
            null,
            PersonalizationSerializer.Deserialize(known.PersonalizationJson));
        var rendered = TemplateMergeRenderer.Render(saved.HtmlBody, values);
        Assert.Contains("For Acme Billiards in Houston, United States", rendered);
        Assert.DoesNotContain("{{", rendered);

        var unknown = saved.Recipients.First(x => x.Email == "unknown@example.com");
        Assert.Null(unknown.ProspectId);
        Assert.Null(unknown.CompanyName);
    }

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }

    private static async Task<EmailAccount> SeedAccountAsync(SendlyDbContext db)
    {
        var protector = CreateProtector();
        var account = new EmailAccount
        {
            Provider = EmailProvider.Smtp,
            DisplayName = "PlayWatch",
            EmailAddress = "sender@example.com",
            IsDefault = true,
            IsActive = true,
            Host = "smtp.example.com",
            Port = 587,
            SecurityMode = SmtpSecurityMode.StartTls,
            Username = "user",
            PasswordProtected = protector.Protect("secret"),
            SenderEmail = "sender@example.com",
            SenderDisplayName = "PlayWatch"
        };
        db.EmailAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task<EmailTemplate> SeedTemplateAsync(SendlyDbContext db, EmailAccount account)
    {
        var project = new OutreachProject { Name = "PlayWatch - United States", IsActive = true };
        var template = new EmailTemplate
        {
            Name = "PlayWatch Introduction",
            Subject = "PlayWatch for {{CompanyName}}",
            HtmlBody = "<p>Hello,</p>",
            EmailAccountId = account.Id,
            IsActive = true
        };
        db.OutreachProjects.Add(project);
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync();
        db.ProjectTemplates.Add(new ProjectTemplate
        {
            ProjectId = project.Id,
            TemplateId = template.Id,
            Channel = OutreachChannelType.Email
        });
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task<Prospect> SeedProspectAsync(SendlyDbContext db, EmailTemplate template)
    {
        var link = await db.ProjectTemplates.FirstAsync(x => x.TemplateId == template.Id);
        var prospect = new Prospect
        {
            ProjectId = link.ProjectId,
            CompanyName = "Acme Billiards",
            NormalizedCompanyName = "acme billiards",
            ContactName = "Sara Owner",
            ContactRole = "Owner",
            Industry = "Billiards Club",
            City = "Houston",
            Country = "United States",
            Email = "owner@acme-billiards.com",
            NormalizedEmail = "owner@acme-billiards.com",
            RecommendedTemplateId = template.Id
        };
        db.Prospects.Add(prospect);
        await db.SaveChangesAsync();
        return prospect;
    }

    private static CampaignService CreateService(SendlyDbContext db)
    {
        var protector = CreateProtector();
        var accountService = new EmailAccountService(
            db,
            protector,
            new Mock<ISmtpConnectionTester>().Object,
            new Mock<IMicrosoftOAuthService>().Object,
            new Mock<IMicrosoftTokenService>().Object);
        var delivery = new Mock<IEmailDeliveryService>().Object;
        return new CampaignService(
            db,
            new Mock<ICampaignQueue>().Object,
            CreateAttachmentStore(),
            delivery,
            accountService,
            new SignatureService(db),
            new TemplateRecipientProtectionService(db),
            new CampaignCancellationRegistry(),
            Microsoft.Extensions.Options.Options.Create(new EmailSendingOptions()));
    }

    private static IAttachmentStore CreateAttachmentStore()
    {
        var store = new Mock<IAttachmentStore>();
        store.Setup(x => x.LoadAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return store.Object;
    }

    private static AesSecretProtector CreateProtector()
    {
        return new AesSecretProtector(Microsoft.Extensions.Options.Options.Create(new SecretProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(new byte[32])
        }));
    }
}
