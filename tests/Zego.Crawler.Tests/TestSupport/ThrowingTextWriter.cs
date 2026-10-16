namespace Zego.Crawler.Tests.TestSupport;

internal sealed class ThrowingTextWriter : StringWriter
{
    public override Task WriteLineAsync(string? value) => throw new IOException("Writer closed.");

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        throw new IOException("Writer closed.");
}
