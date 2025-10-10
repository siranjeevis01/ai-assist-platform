using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.Integrations
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
        Task SendResetPasswordAsync(string to, string otp);
        Task SendWelcomeAsync(string to, string name);
        Task SendNotificationAsync(string to, string title, string message);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            try
            {
                var smtpHost = _config["Smtp:Host"] ?? throw new InvalidOperationException("SMTP Host is not configured.");
                var smtpPortStr = _config["Smtp:Port"] ?? throw new InvalidOperationException("SMTP Port is not configured.");
                var smtpPort = int.Parse(smtpPortStr);

                var smtpUsername = _config["Smtp:Username"] ?? throw new InvalidOperationException("SMTP Username is not configured.");
                var smtpPassword = _config["Smtp:Password"] ?? throw new InvalidOperationException("SMTP Password is not configured.");

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUsername, smtpPassword),
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                var mail = new MailMessage
                {
                    From = new MailAddress(smtpUsername, "AI Agent"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };
                mail.To.Add(to);

                await client.SendMailAsync(mail);
                
                _logger.LogInformation("Email sent to {To} with subject {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To}", to);
                throw;
            }
        }

        public async Task SendResetPasswordAsync(string to, string otp)
        {
            var subject = "Your Password Reset OTP";
            var body = $"""
                Hello,

                Your OTP to reset your password is: {otp}

                This OTP is valid for 1 hour.

                If you didn't request this reset, please ignore this email.

                Best regards,
                AI Agent Team
                """;

            await SendEmailAsync(to, subject, body);
            _logger.LogInformation("Password reset OTP sent to {To}", to);
        }

        public async Task SendWelcomeAsync(string to, string name)
        {
            var subject = "Welcome to AI Agent!";
            var body = $"""
                Hello {name},

                Welcome to AI Agent! Your account has been successfully created.

                With AI Agent, you can:
                - Schedule meetings and events
                - Manage tasks and reminders
                - Integrate with Google Calendar, Gmail, and Trello
                - Receive WhatsApp notifications

                Get started by connecting your services in the dashboard.

                Best regards,
                AI Agent Team
                """;

            await SendEmailAsync(to, subject, body);
            _logger.LogInformation("Welcome email sent to {To}", to);
        }

        public async Task SendNotificationAsync(string to, string title, string message)
        {
            var subject = $"Notification: {title}";
            var body = $"""
                {title}

                {message}

                ---
                This is an automated notification from AI Agent.
                """;

            await SendEmailAsync(to, subject, body);
            _logger.LogInformation("Notification email sent to {To}", to);
        }
    }
}