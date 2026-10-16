using System.Net;

namespace Zego.Crawler.Crawling;

public sealed record PageResult(
    Uri Url,
    PageOutcome Outcome,
    IReadOnlyList<Uri> Links,
    HttpStatusCode? StatusCode = null,
    string? Error = null);
