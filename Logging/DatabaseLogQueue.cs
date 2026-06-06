using System.Threading.Channels;
using MarketApp.Models;

namespace MarketApp.Logging;

public class DatabaseLogQueue
{
    private readonly Channel<AppLog> _queue = Channel.CreateUnbounded<AppLog>();

    public bool TryWrite(AppLog log)
    {
        return _queue.Writer.TryWrite(log);
    }

    public IAsyncEnumerable<AppLog> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }
}
