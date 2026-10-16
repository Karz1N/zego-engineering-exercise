using Zego.Crawler.Urls;

namespace Zego.Crawler.Tests.Unit.Urls;

public sealed class UrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com/path", true)]
    [InlineData("http://EXAMPLE.com:8080/path", true)]
    [InlineData("https://www.example.com/path", false)]
    [InlineData("https://api.example.com/path", false)]
    [InlineData("https://example.com.evil.test/path", false)]
    [InlineData("https://other.test/?host=example.com", false)]
    [InlineData("https://user:password@example.com/path", false)]
    [InlineData("ftp://example.com/path", false)]
    public void ScopeUsesExactHostWithoutCredentials(string candidate, bool expected)
    {
        var policy = new UrlPolicy(new Uri("https://example.com"));
        Assert.Equal(expected, policy.CanVisit(new Uri(candidate)));
    }

    [Theory]
    [InlineData("https://EXAMPLE.com:443/a/../Path?q=2&q=1#section", "https://example.com/Path?q=2&q=1")]
    [InlineData("https://example.com/Path/%2F?q=%2F", "https://example.com/Path/%2F?q=%2F")]
    [InlineData("https://example.com/item/", "https://example.com/item/")]
    [InlineData("https://example.com/item", "https://example.com/item")]
    public void IdentityRemovesFragmentsWithoutMergingDistinctPathsOrQueries(string input, string expected)
    {
        Assert.Equal(expected, UrlPolicy.Normalize(new Uri(input)).AbsoluteUri);
    }

    [Fact]
    public void InternationalHostHasOneIdentity()
    {
        var unicode = new Uri("https://bücher.example/path");
        var ascii = new Uri("https://xn--bcher-kva.example/path");
        Assert.True(new UrlPolicy(unicode).CanVisit(ascii));
        Assert.Equal(UrlPolicy.Normalize(ascii), UrlPolicy.Normalize(unicode));
    }

    [Fact]
    public void RelativeSeedIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new UrlPolicy(new Uri("/page", UriKind.Relative)));
    }
}
