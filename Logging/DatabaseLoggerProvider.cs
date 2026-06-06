namespace MarketApp.Logging;

public class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly DatabaseLogQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DatabaseLoggerProvider(DatabaseLogQueue queue, IHttpContextAccessor httpContextAccessor)
    {
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DatabaseLogger(categoryName, _queue, _httpContextAccessor);
    }

    public void Dispose()
    {
    }
}
