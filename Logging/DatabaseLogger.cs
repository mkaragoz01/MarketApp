using System.Security.Claims;
using MarketApp.Models;

namespace MarketApp.Logging;

public class DatabaseLogger : ILogger
{
    private readonly string _categoryName;
    private readonly DatabaseLogQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DatabaseLogger(
        string categoryName,
        DatabaseLogQueue queue,
        IHttpContextAccessor httpContextAccessor)
    {
        _categoryName = categoryName;
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Information
            && logLevel != LogLevel.None
            && _categoryName.StartsWith("MarketApp.", StringComparison.Ordinal);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
            return;

        var httpContext = _httpContextAccessor.HttpContext;
        var userIdValue = httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : (int?)null;

        _queue.TryWrite(new AppLog
        {
            CreatedAtUtc = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = TruncateRequired(_categoryName, 256),
            Message = TruncateRequired(message, 2000),
            Exception = exception?.ToString(),
            EventId = eventId.Id,
            EventName = Truncate(eventId.Name, 128),
            TraceId = Truncate(httpContext?.TraceIdentifier, 128),
            Method = Truncate(httpContext?.Request.Method, 16),
            Path = Truncate(httpContext?.Request.Path.Value, 512),
            UserId = userId,
            Username = Truncate(httpContext?.User.Identity?.Name, 100)
        });
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static string TruncateRequired(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
