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
using Microsoft.Extensions.Options;
using Moq;

namespace Sendly.Application.Tests;

public class CampaignProcessorTests
{
    [Fact]
    public async Task ProcessAsync_SendsIndividuallyAndNeverBatchesRecipients()
    {
        await using var db = CreateDb();
        var sentTo = new List<string>();
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(x => x.SendAsync(It.IsAny<EmailSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailSendRequest request, CancellationToken _) =>
            {
                sentTo.Add(request.ToEmail);
                Assert.DoesNotContain(";", request.ToEmail);
                Assert.DoesNotContain(",", request.ToEmail);
                return new EmailSendResult { Success = true, Message = "ok" };
            });

        var smtp = await SeedSmtpAsync(db);
        var campaign = await SeedCampaignAsync(db, ["one@example.com", "two@example.com"]);
        var processor = CreateProcessor(db, emailSender.Object, smtp);

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
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(x => x.SendAsync(It.IsAny<EmailSendRequest>(), It.IsAny<CancellationToken>()))
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

        var smtp = await SeedSmtpAsync(db);
        var campaign = await SeedCampaignAsync(db, ["fail@example.com"]);
        campaign.MaxRetryCount = 3;
        await db.SaveChangesAsync();

        var processor = CreateProcessor(db, emailSender.Object, smtp);
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

    private static async Task<SmtpSettings> SeedSmtpAsync(SendlyDbContext db)
    {
        var protector = new AesSecretProtector(Microsoft.Extensions.Options.Options.Create(new SecretProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(new byte[32])
        }));

        var settings = new SmtpSettings
        {
            Host = "smtp.example.com",
            Port = 587,
            SecurityMode = SmtpSecurityMode.StartTls,
            Username = "user",
            PasswordProtected = protector.Protect("secret"),
            SenderEmail = "sender@example.com",
            SenderDisplayName = "Sender"
        };

        db.SmtpSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    private static async Task<Campaign> SeedCampaignAsync(SendlyDbContext db, string[] emails)
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
            MaxRetryCount = 2
        };

        foreach (var email in emails)
        {
            campaign.Recipients.Add(new CampaignRecipient { Email = email });
        }

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private static CampaignProcessor CreateProcessor(SendlyDbContext db, IEmailSender emailSender, SmtpSettings smtp)
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

        var tester = new Mock<ISmtpConnectionTester>();
        var smtpService = new SmtpSettingsService(db, protector, tester.Object);

        return new CampaignProcessor(
            db,
            emailSender,
            attachmentStore.Object,
            notifier.Object,
            smtpService,
            new CampaignCancellationRegistry(),
            NullLogger<CampaignProcessor>.Instance);
    }
}
