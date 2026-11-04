using D365Architect.Commands;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands;

[Collection(ConsoleCollection.Name)]
public class ErrorConsoleTests
{
    [Fact]
    public void PrintIncludesInnerExceptionDetail()
    {
        var inner = new HttpRequestException("inner detail");
        var exception = new InvalidOperationException("top level", inner);

        using var harness = new ConsoleHarness();

        ErrorConsole.Print(exception);

        Assert.Contains("top level", harness.Output);
        Assert.Contains("inner detail", harness.Output);
    }
}
