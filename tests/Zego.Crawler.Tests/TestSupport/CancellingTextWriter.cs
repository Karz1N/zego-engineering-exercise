namespace Zego.Crawler.Tests.TestSupport;

internal sealed class CancellingTextWriter(CancellationTokenSource cancellation) : StringWriter
{
    public int LinksWritten { get; private set; }

    public override Task WriteLineAsync(string? value)
    {
        if (value?.StartsWith("  LINK ", StringComparison.Ordinal) == true)
        {
            LinksWritten++;
            cancellation.Cancel();
        }
        return base.WriteLineAsync(value);
    }

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteLineAsync(buffer.ToString());
    }
}
