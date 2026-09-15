using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="ODataBatchResponseParser"/> against the two response
/// shapes Dataverse's own documented <c>$batch</c> examples show: a
/// successful changeset (one nested <c>changesetresponse_*</c> multipart
/// wrapping a leaf response per write) and a failed one (a single flat leaf
/// directly under the outer boundary — no nested changeset response at all,
/// since nothing in it was kept). Bodies are built with explicit <c>\r\n</c>
/// rather than a triple-quoted string, matching the OData batch spec's own
/// CRLF requirement that <see cref="DataverseClient"/>'s own request builder
/// takes the same care over.
/// </summary>
public sealed class ODataBatchResponseParserTests
{
    private static string Join(params string[] lines) => string.Join("\r\n", lines);

    [Fact]
    public void ParseParts_SuccessfulChangeset_ReturnsOneLeafPerWrite()
    {
        var body = Join(
            "--batchresponse_outer",
            "Content-Type: multipart/mixed; boundary=changesetresponse_inner",
            "",
            "--changesetresponse_inner",
            "Content-Type: application/http",
            "Content-Transfer-Encoding: binary",
            "Content-ID: 1",
            "",
            "HTTP/1.1 204 No Content",
            "OData-Version: 4.0",
            "",
            "--changesetresponse_inner",
            "Content-Type: application/http",
            "Content-Transfer-Encoding: binary",
            "Content-ID: 2",
            "",
            "HTTP/1.1 201 Created",
            "OData-Version: 4.0",
            "",
            "--changesetresponse_inner--",
            "--batchresponse_outer--");

        var parts = ODataBatchResponseParser.ParseParts(body, "batchresponse_outer");

        Assert.Equal(2, parts.Count);
        Assert.Equal(204, parts[0].StatusCode);
        Assert.Equal(201, parts[1].StatusCode);
    }

    [Fact]
    public void ParseParts_FailedChangeset_ReturnsSingleFlatErrorLeaf_NoNestedChangesetResponse()
    {
        var body = Join(
            "--batchresponse_outer",
            "Content-Type: application/http",
            "Content-Transfer-Encoding: binary",
            "",
            "HTTP/1.1 400 Bad Request",
            "Content-Type: application/json; odata.metadata=minimal",
            "",
            "{\"error\":{\"code\":\"0x80044331\",\"message\":\"boom\"}}",
            "--batchresponse_outer--");

        var parts = ODataBatchResponseParser.ParseParts(body, "batchresponse_outer");

        var part = Assert.Single(parts);
        Assert.Equal(400, part.StatusCode);
        Assert.Contains("boom", part.Body);
    }

    [Fact]
    public void ParseParts_QuotedBoundaryOnNestedPart_StillParses()
    {
        // Dataverse's own examples are inconsistent about quoting the
        // boundary parameter between the request and the response side —
        // this only needs to tolerate whichever one shows up.
        var body = Join(
            "--batchresponse_outer",
            "Content-Type: multipart/mixed; boundary=\"changesetresponse_inner\"",
            "",
            "--changesetresponse_inner",
            "Content-Type: application/http",
            "Content-Transfer-Encoding: binary",
            "Content-ID: 1",
            "",
            "HTTP/1.1 204 No Content",
            "",
            "--changesetresponse_inner--",
            "--batchresponse_outer--");

        var parts = ODataBatchResponseParser.ParseParts(body, "batchresponse_outer");

        var part = Assert.Single(parts);
        Assert.Equal(204, part.StatusCode);
    }
}
