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

        // Multipart: plain-text body for HTML-disabled clients + a minimal branded HTML view.
        var html = $"""
            <div style="background:#171B21;color:#F1EDE3;padding:24px;font-family:'Segoe UI',sans-serif">
              <p style="font-family:Consolas,monospace;font-size:11px;letter-spacing:2px;color:#A9AEB9">GYMSTATION</p>
              <h2 style="margin:0 0 12px">{System.Net.WebUtility.HtmlEncode(subject)}</h2>
              <p style="color:#A9AEB9;line-height:1.6">{System.Net.WebUtility.HtmlEncode(body)}</p>
            </div>
            """;

        using var message = new MailMessage(options.From, toEmail, subject, body);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, null, "text/html"));
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
