# Controlled crawler throughput measurement

Measured locally on 16 October 2026, Windows, SDK 10.0.402 and .NET 10.0.13, Release build. Raw observations: [performance-results.json](performance-results.json).

| Fetch concurrency | Median elapsed time, five trials | Peak server requests | Successfully processed URLs |
| --- | --- | --- | --- |
| 1 | 1217.99 ms | 1 | 41 in every trial |
| 4 | 305.17 ms | 4 | 41 in every trial |

The median ratio was approximately **3.99×** on this synthetic workload. It is evidence that independent HTTP requests overlap while respecting the configured limit. Values are observations on one machine, not a promised speedup for real sites.

## Method

The fixture contains one root and 40 child pages. Every child waits 25 ms before serving a small HTML response and links back to the root. The root links to all children. This provides predictable I/O latency, parallelizable work and cycles that should not create extra logical fetches.

The harness warms up both configurations and alternates their order over five repetitions. Every measured crawl uses a fresh HTTP client and connection pool, excluding console page output by using a no-op result callback. The server and crawler share the process, so this is a reproducible comparison rather than an isolated production benchmark.

Each trial fails if the crawl is incomplete, does not process all 41 URLs, requests any path twice, exceeds configured server concurrency or leaves an active server request. Page-count and request-count checks prevent a faster incomplete crawl being mistaken for a performance improvement.

## Reproduce

From the repository root, after restore and Release build:

```sh
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build
```

This deliberately uses local HTTP only. The 25 ms delay simulates service latency; it is not a delay added to the production crawler.

## What has not been measured

This run does not measure peak resident memory, large-DOM CPU performance, network throughput, DNS/TLS costs on external networks, large frontiers, server rate limiting or slow stdout. The implementation bounds dispatch, scheduled URLs and decoded body size, but those are structural guarantees rather than a measured total-memory limit. Further performance work should use representative pages and memory/CPU profiling before tuning concurrency or parser allocation.
