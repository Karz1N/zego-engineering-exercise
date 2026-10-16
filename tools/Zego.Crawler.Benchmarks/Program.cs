using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Zego.Crawler.Benchmarks.Measurements;
using Zego.Crawler.Crawling;
using Zego.Crawler.Http;

const int childPages = 40;
const int delayMilliseconds = 25;
const int repetitions = 5;
var requests = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
int active = 0, peak = 0;
string rootHtml = string.Concat(Enumerable.Range(0, childPages).Select(index => $"<a href='/page/{index}'>page</a>"));
WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
await using WebApplication app = builder.Build();
app.Run(async context =>
{
    int count = Interlocked.Increment(ref active);
    int observed;
    do
    {
        observed = Volatile.Read(ref peak);
    } while (count > observed && Interlocked.CompareExchange(ref peak, count, observed) != observed);

    try
    {
        requests.AddOrUpdate(context.Request.Path.Value ?? "/", 1, (_, existing) => existing + 1);
        context.Response.ContentType = "text/html; charset=utf-8";
        if (context.Request.Path == "/") await context.Response.WriteAsync(rootHtml, context.RequestAborted);
        else
        {
            // Deliberate simulated network/service latency, not production crawling behaviour.
            await Task.Delay(delayMilliseconds, context.RequestAborted);
            await context.Response.WriteAsync("<a href='/'>cycle</a>", context.RequestAborted);
        }
    }
    finally
    {
        Interlocked.Decrement(ref active);
    }
});
await app.StartAsync();
var startUrl = new Uri(app.Urls.Single());
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));

try
{
    // Warm up JIT/parser/network setup. Each measured crawl still owns a new connection pool,
    // matching separate CLI invocations rather than favouring a warmed shared client.
    await MeasureAsync(1);
    await MeasureAsync(4);
    var trials = new List<Trial>();
    for (int repetition = 0; repetition < repetitions; repetition++)
    {
        // Alternate order to reduce systematic warm-up/order bias.
        foreach (int concurrency in repetition % 2 == 0 ? new[] { 1, 4 } : new[] { 4, 1 })
        {
            trials.Add(await MeasureAsync(concurrency));
        }
    }

    double sequentialMedian = Median(trials.Where(trial => trial.Concurrency == 1).Select(trial => trial.ElapsedMilliseconds));
    double concurrentMedian = Median(trials.Where(trial => trial.Concurrency == 4).Select(trial => trial.ElapsedMilliseconds));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Runtime = RuntimeInformation.FrameworkDescription,
        OperatingSystem = RuntimeInformation.OSDescription,
        Pages = childPages + 1,
        SimulatedDelayMilliseconds = delayMilliseconds,
        Repetitions = repetitions,
        SequentialMedianMilliseconds = sequentialMedian,
        ConcurrentMedianMilliseconds = concurrentMedian,
        Speedup = sequentialMedian / concurrentMedian,
        Trials = trials,
        Scope = "Local synthetic I/O workload; not a real-site benchmark or a peak-memory measurement."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    await app.StopAsync();
}

async Task<Trial> MeasureAsync(int concurrency)
{
    requests.Clear();
    peak = 0;
    var options = new CrawlOptions { MaxConcurrency = concurrency };
    using var fetcher = new HttpPageFetcher(options);
    var crawler = new WebCrawler(fetcher, options);
    var stopwatch = Stopwatch.StartNew();
    CrawlSummary summary = await crawler.RunAsync(startUrl, (_, _) => ValueTask.CompletedTask, deadline.Token);
    stopwatch.Stop();

    if (!summary.IsComplete || summary.SuccessfulPages != childPages + 1
        || requests.Count != childPages + 1 || requests.Values.Any(count => count != 1)
        || peak > concurrency || active != 0)
    {
        throw new InvalidOperationException("Benchmark failed correctness or concurrency checks.");
    }

    return new Trial(concurrency, stopwatch.Elapsed.TotalMilliseconds, peak, summary.SuccessfulPages);
}

static double Median(IEnumerable<double> values)
{
    double[] sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}
