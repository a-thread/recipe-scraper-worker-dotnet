using RecipeScraper.Core.Security;

namespace RecipeScraper.Tests;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("http://localhost/recipe")]
    [InlineData("http://127.0.0.1/recipe")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.5/recipe")]
    [InlineData("http://192.168.1.1/recipe")]
    [InlineData("http://internal.local/recipe")]
    [InlineData("file:///etc/passwd")]
    public void RejectsInternalOrNonHttpTargets(string target)
    {
        Assert.True(SsrfGuard.IsBlockedTarget(new Uri(target)));
    }

    [Theory]
    [InlineData("https://example.com/recipes/pancakes")]
    [InlineData("http://cooking.example.org/recipe/1")]
    public void AllowsOrdinaryPublicUrls(string target)
    {
        Assert.False(SsrfGuard.IsBlockedTarget(new Uri(target)));
    }
}
