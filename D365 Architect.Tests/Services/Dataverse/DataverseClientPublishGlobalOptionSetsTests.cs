using System.Text.Json;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// The wire shape of <see cref="IDataverseClient.PublishGlobalOptionSetsAsync"/> (REQ-38), against Microsoft Learn,
/// "PublishXmlRequest.ParameterXml" (https://learn.microsoft.com/dotnet/api/microsoft.crm.sdk.messages.publishxmlrequest.parameterxml):
/// an option set is published with <c>&lt;importexportxml&gt;&lt;optionsets&gt;&lt;optionset&gt;unique name&lt;/optionset&gt;&lt;/optionsets&gt;&lt;/importexportxml&gt;</c>,
/// sent to the <c>PublishXml</c> action as <c>ParameterXml</c> in exact PascalCase (see <see cref="DataverseClientTests"/>).
/// </summary>
public sealed class DataverseClientPublishGlobalOptionSetsTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    // REQ-38: every named choice goes into one PublishXml call, as an <optionset> each under <optionsets>, in the order given.
    // Code path: DataverseClient.PublishGlobalOptionSetsAsync, next to PublishEntityAsync.
    [Fact]
    [Trait("Requirement", "REQ-38")]
    public async Task PublishGlobalOptionSetsAsync_SendsOneOptionsetElementPerNameUnderOptionsets()
    {
        var handler = new CapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.PublishGlobalOptionSetsAsync(new Uri("https://example.crm.dynamics.com"), "token", ["bogdan_status", "bogdan_priority"], CancellationToken.None);

        Expect.True(handler.Request?.RequestUri?.AbsolutePath.EndsWith("/PublishXml", StringComparison.Ordinal) == true, $"REQ-38: the request should go to the PublishXml action. Actual: <{handler.Request?.RequestUri}>.");
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Expect.True(body.RootElement.TryGetProperty("ParameterXml", out var parameterXml), $"REQ-38: the body should carry ParameterXml in exact PascalCase. Actual: <{handler.RequestBody}>.");
        Expect.Equal(
            "<importexportxml><optionsets><optionset>bogdan_status</optionset><optionset>bogdan_priority</optionset></optionsets></importexportxml>",
            parameterXml.GetString(),
            "REQ-38: ParameterXml should list each global choice as an <optionset> under <optionsets>, per Microsoft Learn.");
    }
}
