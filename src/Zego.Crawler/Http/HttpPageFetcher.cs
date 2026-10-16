using System.Buffers;
using System.Net;
using System.Text;
using Zego.Crawler.Crawling;
using Zego.Crawler.Parsing;
using Zego.Crawler.Urls;

namespace Zego.Crawler.Http;

/// <summary>Owns HTTP configuration so callers cannot accidentally enable cross-domain automatic redirects.</summary>
public sealed class HttpPageFetcher : IPageFetcher, IDisposable
{
    private readonly HttpClient client;
    private readonly CrawlOptions options;
    private readonly HtmlLinkExtractor extractor = new();

    public HttpPageFetcher(CrawlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            MaxConnectionsPerServer = options.MaxConcurrency,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZegoInterviewCrawler/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html, application/xhtml+xml");
    }

    public async Task<PageResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);
        CancellationToken token = timeout.Token;

        try
        {
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (IsRedirect(response.StatusCode))
            {
                Uri? location = response.Headers.Location;
                if (location is null || !Uri.TryCreate(url, location.ToString(), out Uri? target) || !UrlPolicy.IsWebUrl(target))
                {
                    return Failure(url, "Redirect has no valid HTTP(S) destination.", response.StatusCode);
                }

                // The coordinator schedules the destination through the same scope and deduplication rules.
                return new PageResult(url, PageOutcome.Redirected, new[] { target }, response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(url, $"HTTP {(int)response.StatusCode}.", response.StatusCode);
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                return new PageResult(url, PageOutcome.Skipped, Array.Empty<Uri>(), response.StatusCode);
            }

            using MemoryStream body = await ReadBoundedAsync(response.Content, token);
            Encoding encoding = GetEncoding(response.Content.Headers.ContentType?.CharSet);
            using var reader = new StreamReader(body, encoding, detectEncodingFromByteOrderMarks: true);
            string html = await reader.ReadToEndAsync(token);
            IReadOnlyList<Uri> links = await extractor.ExtractAsync(html, url, token);
            return new PageResult(url, PageOutcome.Success, links, response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(url, "Request timed out.");
        }
        catch (HttpRequestException)
        {
            return Failure(url, "HTTP transport failed.");
        }
        catch (IOException)
        {
            return Failure(url, "Response could not be read.");
        }
        catch (ResponseTooLargeException)
        {
            return Failure(url, $"Response exceeds {options.MaxResponseBytes} decoded bytes.");
        }
        catch (UnsupportedEncodingException)
        {
            return Failure(url, "Response declares an unsupported encoding.");
        }
    }

    public void Dispose() => client.Dispose();

    private async Task<MemoryStream> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > options.MaxResponseBytes)
        {
            throw new ResponseTooLargeException();
        }

        await using Stream stream = await content.ReadAsStreamAsync(token);
        var body = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) != 0)
            {
                if (body.Length + read > options.MaxResponseBytes)
                {
                    throw new ResponseTooLargeException();
                }

                body.Write(buffer, 0, read);
            }

            body.Position = 0;
            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Encoding GetEncoding(string? charset)
    {
        try
        {
            return string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"', '\''));
        }
        catch (ArgumentException exception)
        {
            throw new UnsupportedEncodingException(exception);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static PageResult Failure(Uri url, string error, HttpStatusCode? status = null) =>
        new(url, PageOutcome.Failed, Array.Empty<Uri>(), status, error);

    private sealed class ResponseTooLargeException : Exception;
    private sealed class UnsupportedEncodingException(Exception innerException) : Exception("Unsupported content encoding.", innerException);
}
