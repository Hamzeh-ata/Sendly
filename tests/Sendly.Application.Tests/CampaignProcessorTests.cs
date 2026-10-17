using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Sendly.Application.Tests;

public class CampaignProcessorTests
{
    [Fact]
    public async Task ProcessAsync_SendsIndividuallyAndNeverBatchesRecipients()
    {
        await using var db = CreateDb();
        var sentTo = new List<string>();
        var providerSender = CreateProviderSender(sentTo, success: true);

        var account = await SeedEmailAccountAsync(db);
        var campaign = await SeedCampaignAsync(db, ["one@example.com", "two@example.com"], account);
        var processor = CreateProcessor(db, providerSender.Object);

        await processor.ProcessAsync(campaign.Id, CancellationToken.None);

        Assert.Equal(2, sentTo.Count);
        Assert.Contains("one@example.com", sentTo);
        Assert.Contains("two@example.com", sentTo);

        var updated = await db.Campaigns.Include(x => x.Recipients).FirstAsync(x => x.Id == campaign.Id);
        Assert.Equal(CampaignStatus.Completed, updated.Status);
        Assert.Equal(2, updated.SuccessCount);
        Assert.All(updated.Recipients, r => Assert.Equal(RecipientDeliveryStatus.Sent, r.Status));
    }

    [Fact]
    public async Task ProcessAsync_DoesNotRetryPermanentFailures()
    {
        await using var db = CreateDb();
        var attempts = 0;
        var providerSender = new Mock<IProviderEmailSender>();
        providerSender.SetupGet(x => x.Provider).Returns(EmailProvider.Smtp);
        providerSender
            .Setup(x => x.SendAsync(
                It.IsAny<EmailAccountCredentials>(),
                It.IsAny<EmailDeliveryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attempts++;
                return new EmailSendResult
                {
                    Success = false,
                    IsTransientFailure = false,
                    Message = "Mailbox unavailable"
                };
            });

        var account = await SeedEmailAccountAsync(db);
        var campaign = await SeedCampaignAsync(db, ["fail@example.com"], account);
        campaign.MaxRetryCount = 3;
        await db.SaveChangesAsync();

        var processor = CreateProcessor(db, providerSender.Object);
        await processor.ProcessAsync(campaign.Id, CancellationToken.None);

        Assert.Equal(1, attempts);
        var recipient = await db.CampaignRecipients.FirstAsync();
        Assert.Equal(RecipientDeliveryStatus.Failed, recipient.Status);
        Assert.False(recipient.IsTransientFailure);
    }

    private static SendlyDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SendlyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SendlyDbContext(options);
    }

    private static async Task<EmailAccount> SeedEmailAccountAsync(SendlyDbContext db)
    {
        var protector = new AesSecretProtector(Microsoft.Extensions.Options.Options.Create(new SecretProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(new byte[32])
        }));

        var account = new EmailAccount
        {
            Provider = EmailProvider.Smtp,
            DisplayName = "Default",
            EmailAddress = "sender@example.com",
            IsDefault = true,
            IsActive = true,
            Host = "smtp.example.com",
            Port = 587,
            SecurityMode = SmtpSecurityMode.StartTls,
            Username = "user",
            PasswordProtected = protector.Protect("secret"),
            SenderEmail = "sender@example.com",
            SenderDisplayName = "Sender"
        };

        db.EmailAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task<Campaign> SeedCampaignAsync(
        SendlyDbContext db,
        string[] emails,
        EmailAccount account)
    {
        var campaign = new Campaign
        {
            Subject = "Hello",
            HtmlBody = "<p>Hi</p>",
            PlainTextBody = "Hi",
            Status = CampaignStatus.Queued,
            TotalRecipients = emails.Length,
            PendingCount = emails.Length,
            DelayMilliseconds = 0,
            BatchSize = 10,
            MaxRetryCount = 2,
            EmailAccountId = account.Id,
            Provider = account.Provider,
            SendingAccountName = account.DisplayName,
            SendingAccountEmail = account.EmailAddress
        };

        foreach (var email in emails)
        {
            campaign.Recipients.Add(new CampaignRecipient { Email = email });
        }

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private static Mock<IProviderEmailSender> CreateProviderSender(List<string> sentTo, bool success)
    {
        var providerSender = new Mock<IProviderEmailSender>();
        providerSender.SetupGet(x => x.Provider).Returns(EmailProvider.Smtp);
        providerSender
            .Setup(x => x.SendAsync(
                It.IsAny<EmailAccountCredentials>(),
                It.IsAny<EmailDeliveryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailAccountCredentials _, EmailDeliveryRequest request, CancellationToken _) =>
            {
                sentTo.Add(request.ToEmail);
                Assert.DoesNotContain(";", request.ToEmail);
                Assert.DoesNotContain(",", request.ToEmail);
                return new EmailSendResult { Success = success, Message = "ok" };
            });
        return providerSender;
    }

    private static CampaignProcessor CreateProcessor(SendlyDbContext db, IProviderEmailSender providerSender)
    {
        var attachmentStore = new Mock<IAttachmentStore>();
        attachmentStore
            .Setup(x => x.LoadAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var notifier = new Mock<ICampaignProgressNotifier>();
        notifier
            .Setup(x => x.NotifyProgressAsync(It.IsAny<CampaignProgressDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var protector = new AesSecretProtector(Microsoft.Extensions.Options.Options.Create(new SecretProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(new byte[32])
        }));

        var microsoftToken = new Mock<IMicrosoftTokenService>();
        var delivery = new EmailDeliveryService(
            db,
            protector,
            microsoftToken.Object,
            [providerSender]);

        return new CampaignProcessor(
            db,
            delivery,
            attachmentStore.Object,
            notifier.Object,
            new TemplateRecipientProtectionService(db),
            new CampaignCancellationRegistry(),
            NullLogger<CampaignProcessor>.Instance);
    }
}
