namespace Zego.Crawler.Benchmarks.Measurements;

internal sealed record Trial(int Concurrency, double ElapsedMilliseconds, int PeakServerRequests, int Pages, double ProcessCpuMilliseconds, long ProcessAllocatedBytes);
