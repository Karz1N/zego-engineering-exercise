# Controlled crawler throughput measurement

Measured locally on 16 October 2026, Windows, SDK 10.0.402 and .NET 10.0.13, Release build, after the encoding and CLI review fixes. Raw observations: [latency workload](performance-results.json) and [large graph](performance-large-graph.json).

| Workload | Fetch concurrency | Median elapsed | Median process CPU | Median process allocations | URLs per trial |
| --- | --- | --- | --- | --- | --- |
| Simulated latency, five trials | 1 | 1242.84 ms | 187.50 ms | 1.66 MiB | 41 |
| Simulated latency, five trials | 4 | 310.96 ms | 93.75 ms | 1.84 MiB | 41 |
| Large graph, three trials | 1 | 1011.61 ms | 1859.38 ms | 394.72 MiB | 10000 |
| Large graph, three trials | 4 | 382.01 ms | 1406.25 ms | 399.44 MiB | 10000 |

Median elapsed-time ratios were approximately **4.00×** for simulated latency and **2.65×** for the large graph. All trials processed the complete graph without duplicate requests or concurrency-limit violations. These are observations on one machine, not promised real-site speedups. CPU time can exceed wall time because the process uses multiple cores; timer granularity and runtime warm-up also affect it.

The OS-reported process peak working set across warm-up and all trials was **79.37 MiB** for the latency workload and **119.40 MiB** for the large graph, each run in a separate process. These values include the fixture server, JIT, runtime and crawler. Total allocated bytes measure allocation volume, not retained memory: objects can be collected and memory reused throughout a trial. The 399 MiB allocation volume therefore does not imply 399 MiB retained memory.

## Method

The default fixture contains one root and 40 child pages. Every child waits 25 ms before serving a small HTML response and links back to the root. The large-graph fixture uses one root and 9999 children with no deliberate delay. The root links to every child, producing a wide frontier, while the backlinks exercise deduplication. Neither fixture downloads public sites or writes page output to the terminal.

The harness warms up both configurations and alternates their order over five repetitions (three for the large graph). Every measured crawl uses a fresh HTTP client and connection pool and a no-op result callback. Elapsed time uses Stopwatch, CPU time uses Process.TotalProcessorTime, allocated bytes use GC.GetTotalAllocatedBytes and the peak working set uses Process.PeakWorkingSet64. The server and crawler share the process; the measures do not attribute resource use exclusively to the crawler. Working-set peaks include warm-up; per-trial CPU/allocation deltas exclude it. No forced GC is performed between trials.

Each trial fails if the crawl is incomplete, does not process all 41 URLs, requests any path twice, exceeds configured server concurrency or leaves an active server request. Page-count and request-count checks prevent a faster incomplete crawl being mistaken for a performance improvement.

## Reproduce

From the repository root, after restore and Release build:

```sh
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build -- --large-graph
```

This deliberately uses local HTTP only. The 25 ms delay simulates service latency; it is not a delay added to the production crawler.

## What has not been measured

These runs do not isolate crawler-only memory, model arbitrary large individual DOMs, prove asymptotic scaling across many graph sizes or measure DNS/TLS on external networks, server rate limiting or slow stdout. The implementation bounds dispatch, scheduled URLs and decoded body size; the DOM and deduplication state still consume memory proportional to input. The observations do not establish a universal memory bound. Further tuning should use representative pages and a dedicated allocation/CPU profiler.
