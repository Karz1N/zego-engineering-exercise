using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Zego.Crawler.Tests.TestSupport;

/// <summary>Real loopback HTTP with OS-assigned ports and no external dependencies.</summary>
internal sealed class LocalHttpSite : IAsyncDisposable
{
    private readonly WebApplication app;
    public Uri BaseUrl { get; private set; } = null!;
    public ConcurrentDictionary<string, int> Hits { get; } = new(StringComparer.Ordinal);

    private LocalHttpSite(WebApplication app) => this.app = app;

    public static async Task<LocalHttpSite> StartAsync(Func<HttpContext, Task> handle)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        var site = new LocalHttpSite(app);
        app.Run(async context =>
        {
            site.Hits.AddOrUpdate(context.Request.Path.Value ?? "/", 1, (_, count) => count + 1);
            try
            {
                await handle(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client cancellation is expected in timeout/cancellation integration tests.
            }
        });
        try
        {
            await app.StartAsync();
            site.BaseUrl = new Uri(app.Urls.Single());
            return site;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.StopAsync(deadline.Token);
        await app.DisposeAsync();
    }

    public static async Task HtmlAsync(HttpContext context, string html)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html, context.RequestAborted);
    }
}
