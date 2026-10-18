using Microsoft.Extensions.Logging.Abstractions;
using StormBench.Infrastructure;

namespace StormBench.Scada.Tests;

/// <summary>The historian's writer never blocks the caller: a full queue drops, a failed write is logged and dropped.</summary>
public class BatchWriterTests
{
    [Fact]
    public async Task A_full_queue_drops_instead_of_waiting()
    {
        var written = new List<int>();
        var writer = new BatchWriter<int>("test", (batch, _) => { written.AddRange(batch); return Task.CompletedTask; }, NullLogger.Instance, capacity: 2);

        writer.EnqueueRange([1, 2, 3, 4, 5]);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], written);
        Assert.Equal(3, writer.Dropped);
    }

    [Fact]
    public async Task A_failed_batch_is_dropped_and_the_next_one_written()
    {
        var calls = 0;
        var written = new List<int>();
        var writer = new BatchWriter<int>(
            "test",
            (batch, _) =>
            {
                if (calls++ == 0)
                {
                    throw new HttpRequestException("store down");
                }
                written.AddRange(batch);
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            maxBatch: 2);

        writer.EnqueueRange([1, 2, 3]);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([3], written);
        Assert.Equal(2, writer.Failed);
        Assert.Equal(1, writer.Written);
    }

    [Fact]
    public async Task The_background_loop_writes_in_batches()
    {
        var batches = new List<int>();
        var done = new TaskCompletionSource();
        var writer = new BatchWriter<int>(
            "test",
            (batch, _) =>
            {
                batches.Add(batch.Count);
                if (batches.Sum() == 10)
                {
                    done.SetResult();
                }
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            linger: TimeSpan.FromMilliseconds(50));

        await writer.StartAsync(TestContext.Current.CancellationToken);
        writer.EnqueueRange(Enumerable.Range(0, 10));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await writer.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([10], batches);
    }
}
