using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace ERecycleHub.Infrastructure;

public sealed class EmailOptions
{
    /// <summary>"SendGrid" or "Pickup" (writes .html files to <see cref="PickupDirectory"/>; the default, so nothing is ever sent by accident).</summary>
    public string Provider { get; set; } = "Pickup";
    public string PickupDirectory { get; set; } = "App_Data/mail";
    public string? SendGridApiKey { get; set; }
    public string FromAddress { get; set; } = "no-reply@erecyclehub.local";
    public string FromName { get; set; } = "eRecycleHub";
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string html, CancellationToken ct = default);
}

public sealed class SendGridEmailSender(IOptions<EmailOptions> options, ILogger<SendGridEmailSender> log) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SendGridApiKey)) throw new InvalidOperationException("Email:SendGridApiKey is not configured.");
        var msg = MailHelper.CreateSingleEmail(new EmailAddress(o.FromAddress, o.FromName), new EmailAddress(to), subject, null, html);
        var response = await new SendGridClient(o.SendGridApiKey).SendEmailAsync(msg, ct);
        if (!response.IsSuccessStatusCode)
        {
            log.LogError("SendGrid rejected email {Subject} with status {Status}", subject, (int)response.StatusCode);
            throw new InvalidOperationException("Email delivery failed.");
        }
    }
}

/// <summary>Development/test transport: one HTML file per message, never leaves the machine.</summary>
public sealed class PickupEmailSender(IOptions<EmailOptions> options, ILogger<PickupEmailSender> log) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        var dir = Path.GetFullPath(options.Value.PickupDirectory);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, $"<!-- To: {to} -->\n<!-- Subject: {subject} -->\n{html}", ct);
        log.LogInformation("Email to {To} written to {File}", to, file);
    }
}
