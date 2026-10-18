using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StormBench.Infrastructure;

/// <summary>
/// Hands records to a store that may be slow or absent without ever blocking the caller: a
/// bounded queue, one consumer that writes batches, and a batch that fails is logged and dropped.
/// The historian and the alarm journal write through it, so a missing database never slows the
/// UNS host or the lockstep.
/// </summary>
public sealed class BatchWriter<T> : BackgroundService
{
    private readonly string _name;
    private readonly Func<IReadOnlyList<T>, CancellationToken, Task> _write;
    private readonly ILogger _logger;
    private readonly int _maxBatch;
    private readonly TimeSpan _linger;
    private readonly Channel<T> _queue;
    private long _dropped;
    private long _written;
    private long _failed;

    /// <param name="name">Store name for the log.</param>
    /// <param name="write">Writes one batch; an exception drops the batch.</param>
    /// <param name="logger">Where failures and drops are reported.</param>
    /// <param name="capacity">Records the queue holds; further records are dropped and counted.</param>
    /// <param name="maxBatch">Records per write at most.</param>
    /// <param name="linger">Wait after the first record so that one write carries many.</param>
    public BatchWriter(
        string name,
        Func<IReadOnlyList<T>, CancellationToken, Task> write,
        ILogger logger,
        int capacity = 100_000,
        int maxBatch = 5_000,
        TimeSpan? linger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBatch, 1);
        _name = name;
        _write = write;
        _logger = logger;
        _maxBatch = maxBatch;
        _linger = linger ?? TimeSpan.FromMilliseconds(250);
        _queue = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Records dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Records written.</summary>
    public long Written => Interlocked.Read(ref _written);

    /// <summary>Records lost because their batch failed.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Queues a record; never waits. A full queue drops it.</summary>
    public void Enqueue(T record) => _queue.Writer.TryWrite(record);

    public void EnqueueRange(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            _queue.Writer.TryWrite(record);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;
        var batch = new List<T>(_maxBatch);
        var droppedReported = 0L;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                if (_linger > TimeSpan.Zero)
                {
                    await Task.Delay(_linger, stoppingToken);
                }
                while (batch.Count < _maxBatch && reader.TryRead(out var record))
                {
                    batch.Add(record);
                }
                await WriteAsync(batch, stoppingToken);
                batch.Clear();

                var dropped = Dropped;
                if (dropped != droppedReported)
                {
                    BatchWriterLog.Dropped(_logger, _name, dropped - droppedReported);
                    droppedReported = dropped;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Writes whatever is queued now (tests, shutdown).</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = new List<T>(_maxBatch);
        while (_queue.Reader.TryRead(out var record))
        {
            batch.Add(record);
            if (batch.Count == _maxBatch)
            {
                await WriteAsync(batch, cancellationToken);
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            await WriteAsync(batch, cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    private async Task WriteAsync(List<T> batch, CancellationToken cancellationToken)
    {
        try
        {
            await _write(batch, cancellationToken);
            Interlocked.Add(ref _written, batch.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Add(ref _failed, batch.Count);
            BatchWriterLog.WriteFailed(_logger, ex, _name, batch.Count);
        }
    }
}

internal static partial class BatchWriterLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Store}: write failed, {Count} records dropped")]
    public static partial void WriteFailed(ILogger logger, Exception exception, string store, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Store}: queue full, {Count} records dropped")]
    public static partial void Dropped(ILogger logger, string store, long count);
}
