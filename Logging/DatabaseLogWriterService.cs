using MarketApp.Data;

namespace MarketApp.Logging;

public class DatabaseLogWriterService : BackgroundService
{
    private readonly DatabaseLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseLogWriterService(DatabaseLogQueue queue, IServiceScopeFactory scopeFactory)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var log in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                context.AppLogs.Add(log);
                await context.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Log veritabanına yazılamadı: {ex.Message}");
            }
        }
    }
}
