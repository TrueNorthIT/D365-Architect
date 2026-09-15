namespace D365Architect.Services.Dataverse;

/// <summary>
/// Parses the raw <c>multipart/mixed</c> text body of a Dataverse Web API
/// <c>$batch</c> response into a flat list of the individual HTTP responses
/// it carries — see <see cref="DataverseClient.ExecuteTransactionAsync"/>.
/// Hand-rolled rather than pulled from a library: the only <c>.NET</c> type
/// that parses this (<c>System.Net.Http.Formatting</c>'s
/// <c>ReadAsMultipartAsync</c>) lives in the old ASP.NET Web API client
/// package, not the base class library, and this format is simple enough
/// (plain, boundary-delimited text — Dataverse never returns binary payloads
/// here) that hand-parsing it is less risk than taking on that dependency.
///
/// A successful changeset response nests one level: the outer
/// <c>batchresponse_*</c> boundary wraps a single part whose own
/// <c>Content-Type</c> is itself <c>multipart/mixed; boundary=changesetresponse_*</c>,
/// which in turn wraps one leaf HTTP response per write that was sent. A
/// failed changeset instead reports its rollback as a single flat leaf
/// directly under the outer boundary — no nested <c>changesetresponse_*</c>
/// at all (confirmed against Microsoft's own documented example). Recursing
/// on any part whose own <c>Content-Type</c> is <c>multipart/mixed</c>
/// handles both shapes uniformly without the caller needing to know which
/// one it got.
/// </summary>
internal static class ODataBatchResponseParser
{
    /// <summary>One leaf HTTP response found somewhere inside the (possibly nested) multipart body.</summary>
    public readonly record struct Part(int StatusCode, string Body);

    public static IReadOnlyList<Part> ParseParts(string multipartBody, string boundary)
    {
        var parts = new List<Part>();
        ParseInto(multipartBody, boundary, parts);
        return parts;
    }

    private static void ParseInto(string multipartBody, string boundary, List<Part> parts)
    {
        var marker = "--" + boundary;
        var segments = multipartBody.Split(marker, StringSplitOptions.None);

        // segments[0] is always the preamble before the first boundary line
        // (empty in practice); the terminator ("--") produces a trailing
        // segment starting with "--" once its own leading "--boundary" was
        // already consumed by Split — both are skipped below rather than
        // parsed as parts.
        foreach (var raw in segments.Skip(1))
        {
            var segment = raw.TrimStart('\r', '\n');
            if (segment.Length == 0 || segment.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            ParsePart(segment, parts);
        }
    }

    /// <summary>
    /// One part's own text: its wrapper headers (<c>Content-Type</c>/
    /// <c>Content-Transfer-Encoding</c>/<c>Content-ID</c>), a blank line,
    /// then either a nested multipart body or a raw HTTP response.
    /// </summary>
    private static void ParsePart(string segment, List<Part> parts)
    {
        var (headers, body) = SplitHeadersAndBody(segment);

        var contentType = headers.GetValueOrDefault("content-type", "");
        if (contentType.Contains("multipart/mixed", StringComparison.OrdinalIgnoreCase))
        {
            var nestedBoundary = ExtractBoundary(contentType)
                ?? throw new FormatException($"A nested multipart batch part had no boundary parameter: '{contentType}'.");
            ParseInto(body, nestedBoundary, parts);
            return;
        }

        parts.Add(ParseHttpResponse(body));
    }

    /// <summary>Splits on the first blank line (a bare <c>\r\n\r\n</c>) into headers-as-a-map and everything after.</summary>
    private static (Dictionary<string, string> Headers, string Body) SplitHeadersAndBody(string text)
    {
        var separatorIndex = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerBlock = separatorIndex < 0 ? text : text[..separatorIndex];
        var body = separatorIndex < 0 ? "" : text[(separatorIndex + 4)..];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in headerBlock.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            headers[line[..colonIndex].Trim()] = line[(colonIndex + 1)..].Trim();
        }

        return (headers, body);
    }

    /// <summary>
    /// <paramref name="text"/> is a raw HTTP response: a status line
    /// (<c>HTTP/1.1 204 No Content</c>), headers, a blank line, then the
    /// response body (JSON on error, empty on a bare 204/200 with no
    /// content). Trailing boundary/CRLF noise left over from the caller's
    /// own split is trimmed off the end.
    /// </summary>
    private static Part ParseHttpResponse(string text)
    {
        var trimmed = text.TrimEnd('\r', '\n');
        var statusLineEnd = trimmed.IndexOf("\r\n", StringComparison.Ordinal);
        var statusLine = statusLineEnd < 0 ? trimmed : trimmed[..statusLineEnd];

        var statusParts = statusLine.Split(' ', 3);
        var statusCode = statusParts.Length >= 2 && int.TryParse(statusParts[1], out var code) ? code : 0;

        var (_, body) = SplitHeadersAndBody(trimmed);
        return new Part(statusCode, body.TrimEnd('\r', '\n'));
    }

    /// <summary>Pulls <c>boundary=...</c> out of a <c>Content-Type</c> header value, with or without surrounding quotes.</summary>
    private static string? ExtractBoundary(string contentType)
    {
        foreach (var segment in contentType.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["boundary=".Length..].Trim('"');
            }
        }

        return null;
    }
}
