namespace Rethread.Api.Auth;

/// <summary>
/// Sends text messages: <see cref="NotifyLkSmsSender"/> once <c>Sms:NotifyLk:ApiKey</c> is set, otherwise
/// <see cref="LoggingSmsSender"/>. Throws <see cref="SmsNotSentException"/> when the message wasn't sent.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string mobile, string text, CancellationToken ct);
}

public interface IEmailSender
{
    Task SendAsync(string email, string subject, string body, CancellationToken ct);
}

/// <summary>Messages "sent" by the logging senders, newest last. Lets Development and tests read them.</summary>
public sealed class DevOutbox
{
    public sealed record Message(string To, string Subject, string Body, DateTime At);

    private readonly List<Message> _messages = [];

    public void Add(Message message)
    {
        lock (_messages)
        {
            _messages.Add(message);
            if (_messages.Count > 200) _messages.RemoveAt(0);
        }
    }

    public IReadOnlyList<Message> All()
    {
        lock (_messages) return [.. _messages];
    }

    public Message? LastTo(string to) => All().LastOrDefault(m => m.To == to);
}

public sealed class LoggingSmsSender(DevOutbox outbox, TimeProvider time, ILogger<LoggingSmsSender> logger)
    : ISmsSender
{
    public Task SendAsync(string mobile, string text, CancellationToken ct)
    {
        logger.LogInformation("SMS to {Mobile}: {Text}", mobile, text);
        outbox.Add(new DevOutbox.Message(mobile, "SMS", text, time.GetUtcNow().UtcDateTime));
        return Task.CompletedTask;
    }
}

public sealed class LoggingEmailSender(DevOutbox outbox, TimeProvider time, ILogger<LoggingEmailSender> logger)
    : IEmailSender
{
    public Task SendAsync(string email, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("Email to {Email}: {Subject}\n{Body}", email, subject, body);
        outbox.Add(new DevOutbox.Message(email, subject, body, time.GetUtcNow().UtcDateTime));
        return Task.CompletedTask;
    }
}
