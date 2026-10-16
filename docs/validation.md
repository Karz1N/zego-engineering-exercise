# Local validation

Validated on 16 October 2026. Environment: Windows, SDK 10.0.402, runtime 10.0.13. All four projects target `net10.0`.

## Observed checks

- Release build: passed with zero warnings and zero errors.
- xUnit suite: 70 tests passed, none failed or skipped, including real HTTP loopback integration.
- Locked restore: passed using all four committed package lock files.
- Clean-source reproduction: archived the staged delivery without local SDK/cache/build outputs, then restored, built and ran all 70 tests successfully from that separate source directory. The SDK and normal NuGet cache were supplied externally.
- NuGet vulnerability query, including transitive dependencies: no known vulnerable packages reported by the configured source on 16 October 2026. This is a point-in-time advisory check, not a guarantee against undisclosed vulnerabilities.
- Code formatting: applied with `dotnet format`; final `--verify-no-changes` passed after the last source edit.
- CLI entry point: `dotnet run ... -- --help` executed successfully with exit code 0.
- Regression tests reproduce and then prevent incorrect meta-charset decoding, BOM/HTTP conflicts, closed-output exceptions and delayed cancellation during page output. Windows-1252 was also verified in an isolated test process to detect global initialisation/order dependencies.
- Controlled throughput harness: all ten latency trials and six 10,000-URL trials passed correctness/concurrency checks; results are in [performance-results.json](performance-results.json) and [performance-large-graph.json](performance-large-graph.json).

## Commands

```sh
dotnet restore Zego.Crawler.sln --locked-mode
dotnet build Zego.Crawler.sln --configuration Release --no-restore
dotnet test Zego.Crawler.sln --configuration Release --no-build --no-restore
dotnet format Zego.Crawler.sln --verify-no-changes --no-restore
dotnet list Zego.Crawler.sln package --vulnerable --include-transitive --no-restore
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build -- --large-graph
```

Run from the repository root with the .NET 10 SDK installed. Restore requires NuGet access; the test runner and integration fixture require local IPC and loopback sockets. In the managed development environment, single-node MSBuild (`-m:1`) and disabled shared compilation (`-p:UseSharedCompilation=false`) were used to accommodate process communication restrictions. These settings do not alter application behaviour.

## Verification limits

The initial published commit `8f264c2` passed [GitHub Actions on Windows and Linux](https://github.com/Karz1N/zego-engineering-exercise/actions/runs/38877825753), including locked restore, Release build, all 59 tests then present and formatting. Local review fixes subsequently increased the suite to 70 tests; hosted validation of the final commit is available in the [Actions history](https://github.com/Karz1N/zego-engineering-exercise/actions). Visual Studio compilation and public websites have not been independently validated. Resource measurements cover a synthetic process containing both crawler and server; no production scalability or isolated crawler-memory claim is made. Known functional limitations are described in the [README](../README.md).
