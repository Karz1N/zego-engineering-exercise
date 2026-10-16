# Local validation

Validated on 16 October 2026. Environment: Windows, SDK 10.0.402, runtime 10.0.13. All four projects target `net10.0`.

## Observed checks

- Release build: passed with zero warnings and zero errors.
- xUnit suite: 59 tests passed, none failed or skipped, including real HTTP loopback integration.
- Locked restore: passed using all four committed package lock files.
- Code formatting: applied with `dotnet format`; final `--verify-no-changes` passed after the last source edit.
- CLI entry point: `dotnet run ... -- --help` executed successfully with exit code 0.
- Controlled throughput harness: all ten measured trials passed correctness/concurrency checks; results are in [performance-results.json](performance-results.json).

## Commands

```sh
dotnet restore Zego.Crawler.sln --locked-mode
dotnet build Zego.Crawler.sln --configuration Release --no-restore
dotnet test Zego.Crawler.sln --configuration Release --no-build --no-restore
dotnet format Zego.Crawler.sln --verify-no-changes --no-restore
dotnet run --project tools/Zego.Crawler.Benchmarks --configuration Release --no-build
```

Run from the repository root with the .NET 10 SDK installed. Restore requires NuGet access; the test runner and integration fixture require local IPC and loopback sockets. In the managed development environment, single-node MSBuild (`-m:1`) and disabled shared compilation (`-p:UseSharedCompilation=false`) were used to accommodate process communication restrictions. These settings do not alter application behaviour.

## Verification limits

GitHub Actions is configured for Windows and Linux using .NET 10 but has not executed. Linux, Visual Studio compilation, public websites and peak-memory usage have not been validated. The measurements use a synthetic local workload; no production scalability claim is made. Known functional limitations are described in the [README](../README.md).
