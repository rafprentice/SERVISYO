using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace Servisyo.Web.Services;

/// Settings read from the "Email" section (user-secrets locally, appsettings.Production.json online).
public class EmailSettings
{
    public const string SectionName = "Email";

    /// "Smtp" = really send through Gmail. Anything else (default "Log") = do not send, write the message
    /// (including the code) to the application log instead. Log is the safe default: nothing is sent by accident.
    public string Mode { get; set; } = "Log";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;       // your Gmail address
    public string AppPassword { get; set; } = string.Empty;    // the 16-character Google App Password (NOT your Gmail password)
    public string FromName { get; set; } = "SERVISYO";
    public string FromAddress { get; set; } = string.Empty;    // empty = same as Username
}

public interface IEmailService
{
    /// Returns true when the message was handed to Gmail (or written to the log in Log mode), false when it could not be sent.
    Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string textBody);
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> options, ILogger<EmailService>? logger = null)
    {
        _settings = options.Value;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailService>.Instance;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string textBody)
    {
        var wantsSmtp = string.Equals(_settings.Mode, "Smtp", StringComparison.OrdinalIgnoreCase);

        if (!wantsSmtp)
        {
            // Log mode: for local development, or when the host does not allow outgoing mail.
            _logger.LogWarning("EMAIL (Log mode, not sent) To: {To} | Subject: {Subject} | Body: {Body}", toEmail, subject, textBody);
            return true;
        }

        if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.AppPassword))
        {
            _logger.LogError("Email:Mode is Smtp but Email:Username or Email:AppPassword is missing. The email was not sent.");
            return false;
        }

        try
        {
            var from = string.IsNullOrWhiteSpace(_settings.FromAddress) ? _settings.Username : _settings.FromAddress;

            using var message = new MailMessage
            {
                From = new MailAddress(from, _settings.FromName),
                Subject = subject,
                Body = textBody,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            message.To.Add(new MailAddress(toEmail));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html"));

            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = true,                              // port 587 + STARTTLS
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_settings.Username, _settings.AppPassword),
                Timeout = 15000
            };

            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            // Never show this to the visitor and never log the password. The message says what went wrong (blocked port, bad App Password...).
            _logger.LogError(ex, "Could not send an email through {Host}:{Port} to {To}.", _settings.Host, _settings.Port, Mask(toEmail));
            return false;
        }
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" : email[0] + "***" + email[at..];
    }
}

public record EmailContent(string Html, string Text);

/// The emails SERVISYO sends. Plain inline-styled HTML so it looks right in Gmail.
public static class EmailTemplates
{
    public static EmailContent CodeEmail(string heading, string intro, string code, int validMinutes)
    {
        var h = WebUtility.HtmlEncode(heading);
        var i = WebUtility.HtmlEncode(intro);

        var html = $@"<div style=""font-family:Segoe UI,Arial,sans-serif;background:#F8F9FA;padding:24px"">
  <div style=""max-width:460px;margin:0 auto;background:#ffffff;border:1px solid #E4E7EC;border-radius:10px;overflow:hidden"">
    <div style=""background:#5B2BF0;color:#ffffff;padding:18px 24px;font-size:18px;font-weight:700"">SERVISYO</div>
    <div style=""padding:24px;color:#101828"">
      <h2 style=""margin:0 0 10px;font-size:18px"">{h}</h2>
      <p style=""margin:0 0 18px;font-size:14px;color:#475467"">{i}</p>
      <div style=""text-align:center;margin:0 0 18px"">
        <span style=""display:inline-block;font-size:32px;letter-spacing:10px;font-weight:700;background:#F2F0FF;color:#3B1BB8;border-radius:8px;padding:12px 18px 12px 28px"">{code}</span>
      </div>
      <p style=""margin:0 0 6px;font-size:13px;color:#475467"">This code expires in {validMinutes} minutes and can be used once.</p>
      <p style=""margin:0;font-size:13px;color:#475467"">If you did not ask for this, you can ignore this email. Nobody can use your account without the code.</p>
    </div>
  </div>
</div>";

        var text = $"{heading}\n\n{intro}\n\nYour code: {code}\n\nThis code expires in {validMinutes} minutes and can be used once.\nIf you did not ask for this, you can ignore this email.";
        return new EmailContent(html, text);
    }

    public static EmailContent PasswordReset(string code, int validMinutes) => CodeEmail(
        "Reset your password",
        "Enter this code on the SERVISYO password reset page to choose a new password.",
        code, validMinutes);

    public static EmailContent VerifyEmail(string code, int validMinutes) => CodeEmail(
        "Verify your email",
        "Enter this code on SERVISYO to confirm your email address and activate your account.",
        code, validMinutes);
}
