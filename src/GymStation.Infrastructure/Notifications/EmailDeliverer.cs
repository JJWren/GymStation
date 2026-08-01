using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace GymStation.Infrastructure.Notifications;

/// <summary>Channel adapter contract — push slots in later as another implementation site.</summary>
public interface IEmailDeliverer
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default);
}

public record EmailOptions(string? Host, int Port, string? Username, string? Password, string From)
{
    public bool Configured => !string.IsNullOrWhiteSpace(Host);
}

/// <summary>SMTP adapter; used only when Email:Host is configured.</summary>
public class SmtpEmailDeliverer(EmailOptions options) : IEmailDeliverer
{
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        using var client = new SmtpClient(options.Host!, options.Port);
        if (!string.IsNullOrEmpty(options.Username))
        {
            client.Credentials = new NetworkCredential(options.Username, options.Password);
            client.EnableSsl = true;
        }

        using var message = new MailMessage(options.From, toEmail, subject, body);
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>Development fallback when SMTP is unconfigured: logs instead of sending.</summary>
public class LoggingEmailDeliverer(ILogger<LoggingEmailDeliverer> logger) : IEmailDeliverer
{
    public Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL (unconfigured SMTP) to {To}: {Subject} — {Body}", toEmail, subject, body);
        return Task.CompletedTask;
    }
}
