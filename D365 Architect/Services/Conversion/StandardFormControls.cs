using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Maps a Dataverse standard (built-in, non-custom/non-PCF) control's raw
/// FormXML <c>classid</c> GUID to a human-friendly name, and back — what
/// lets <see cref="FormControl.Control"/> read as a word instead of a
/// GUID, per this tool's own YAML-friendly-keys convention. Unlike a
/// custom/PCF control (endless, unenumerable by design — see
/// <see cref="FormControl.CustomControlId"/>), Dataverse's own standard
/// controls are a small, knowable set — but still one no single Microsoft
/// page fully and currently enumerates, so every entry here was cross-
/// checked against real, live, Microsoft-published FormXML (not just a
/// docs page) before being trusted for a round trip that writes back to a
/// real form.
///
/// A wrong entry here would be worse than an under-description: reversed
/// back to a GUID on <c>form import</c>, it would silently swap which
/// control renders a real cell. So every entry is one of:
/// <list type="bullet">
/// <item>Confirmed: found verbatim in real, live Microsoft-published
/// FormXML (not just a docs page or a third-party tool's own internal
/// table) — see each region's own comment for the specific source.</item>
/// <item>Corroborated, not personally live-confirmed: the archived
/// Microsoft Learn `&lt;control&gt;` (FormXml) reference page agrees with
/// at least one independent, well-established community tool's own
/// hard-coded table, but no live FormXML sample containing it was actually
/// found — marked explicitly below.</item>
/// </list>
/// Deliberately absent, not guessed: <c>BigInt</c> (Dataverse doesn't
/// support it on forms at all — API-only), <c>UniqueIdentifier</c> (no
/// documented or live-observed control found for it), and Business
/// Process Flow (confirmed to live inside a <c>workflow</c> record's own
/// Xaml, not a <c>systemform</c>'s FormXML at all — out of scope here by
/// construction, not by omission).
/// </summary>
public static class StandardFormControls
{
    private sealed record Entry(string FriendlyName, Guid ClassId);

    // Confirmed live: found verbatim in real, Microsoft-published FormXML
    // (the stock Account main form via WaelHamze/xrm-ci-framework — a
    // widely-used Dynamics MVP CI/CD sample; the Nonprofits Accelerator
    // solution via microsoft/Nonprofits; a Case main form; an Appointment
    // main form) as well as the archived Microsoft Learn `<control>`
    // (FormXml) reference page.
    private static readonly Entry[] Confirmed =
    [
        new("SingleLineText", Guid.Parse("4273EDBD-AC1D-40D3-9FB2-095C621B552D")),
        new("Email", Guid.Parse("ADA2203E-B4CD-49BE-9DDF-234642B43B52")),
        new("Url", Guid.Parse("71716B6C-711E-476C-8AB8-5D11542BFB47")),
        new("TickerSymbol", Guid.Parse("1E1FC551-F7A8-43AF-AC34-A8DC35C7B6D4")),
        // A Phone-format String field has been seen live rendered with
        // EITHER this control OR plain SingleLineText — both are real;
        // this is simply the dedicated one, not the only valid one.
        new("Phone", Guid.Parse("8C10015A-B339-4982-9474-A95FE05631A5")),
        // Also what a composite address control uses — no separate entry
        // needed, it really is this same classid.
        new("MultilineText", Guid.Parse("E0DECE4B-6FC8-4A8F-A065-082708572369")),
        new("WholeNumber", Guid.Parse("C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F")),
        // Confirmed against real live FormXML deliberately: a public
        // "dataverse-skills" reference on GitHub has the wrong GUID for
        // this one (and for Currency below) — contradicted by the archived
        // MS Learn page, two independent community tools, and live data.
        new("Decimal", Guid.Parse("C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E")),
        new("Currency", Guid.Parse("533B9E00-756B-4312-95A0-DC888637AC78")),
        new("DateAndTime", Guid.Parse("5B773807-9FB2-42DB-97C3-7A91EFF8ADFF")),
        new("OptionSet", Guid.Parse("3EF39988-22BB-4F0B-BBBE-64B5A3748AEE")),
        // Postdates the archived MS Learn page (no page lists it at all),
        // but confirmed live plus agreed on by three independent
        // community-maintained tools.
        new("MultiSelectOptionSet", Guid.Parse("4AA28AB7-9C13-4F57-A73D-AD894D048B5F")),
        // The radio-button rendering specifically — the one actually seen
        // live; see TwoOptionsCheckbox below for the other rendering.
        new("TwoOptions", Guid.Parse("67FAC785-CD58-4F9F-ABB3-4B7DDC6ED5ED")),
        // Also used for Owner and Customer fields — neither has a control
        // of its own, confirmed live for both.
        new("Lookup", Guid.Parse("270BD3DB-D9AF-4782-9025-509E298DEC0A")),
        new("PartyList", Guid.Parse("CBFB742C-14E7-4A17-96BB-1A13F7F64AA2")),
        new("Subgrid", Guid.Parse("E7A81278-8635-4D9E-8D4D-59480B391C5B")),
        new("WebResource", Guid.Parse("9FDF5F91-88B1-47F4-AD53-C11EFC01A01D")),
        new("QuickViewForm", Guid.Parse("5C5600E0-1D6E-4205-A272-BE80DA87FD42")),
        new("Notes", Guid.Parse("06375649-C143-495E-A496-C962E5B4488E")),
    ];

    // Corroborated by the archived Microsoft Learn page plus at least one
    // independent, well-established community tool's own hard-coded
    // table (XrmToolBox plugins, Dynamics-Crm-DevKit, XrmTypesGen) — real,
    // sourced GUIDs, just not personally found inside a live FormXML
    // sample during this round of research. Included rather than left as
    // a raw GUID, since the value itself is still corroborated by more
    // than one credible source; flagged here so that distinction isn't
    // lost.
    private static readonly Entry[] Corroborated =
    [
        new("TwoOptionsCheckbox", Guid.Parse("B0C6723A-8503-4FD7-BB28-C8A06AC933C2")),
        new("IFrame", Guid.Parse("FD2A7985-3187-444E-908D-6624B21F69C0")),
        new("Timeline", Guid.Parse("6636847D-B74D-4994-B55A-A6FAF97ECEA2")),
        new("ActivitiesGrid", Guid.Parse("C72511AB-84E5-4FB7-A543-25B4FC01E83E")),
        new("WebResourceImage", Guid.Parse("587CDF98-C1D5-4BDE-8473-14A0BC7644A7")),
        new("WebResourceSilverlight", Guid.Parse("080677DB-86EC-4544-AC42-F927E74B491F")),
    ];

    private static readonly IReadOnlyDictionary<string, Guid> ByName =
        Confirmed.Concat(Corroborated).ToDictionary(e => e.FriendlyName, e => e.ClassId, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<Guid, string> ByClassId =
        Confirmed.Concat(Corroborated).ToDictionary(e => e.ClassId, e => e.FriendlyName);

    /// <summary>Every recognized friendly name, sorted — used for the generated JSON Schema's own <c>enum</c> on <see cref="FormControl.Control"/> (see <see cref="Schema.SchemaEnumAttribute"/>), so an editor can offer autocomplete and catch a typo before this tool ever has to.</summary>
    public static readonly IReadOnlyList<string> FriendlyNames = ByName.Keys.Order(StringComparer.Ordinal).ToList();

    /// <returns>The friendly name for <paramref name="rawClassId"/> (e.g. from a live control's own <c>classid</c> attribute), or null when it isn't one of Dataverse's recognized standard controls.</returns>
    public static string? TryGetFriendlyName(string rawClassId) =>
        Guid.TryParse(rawClassId, out var guid) && ByClassId.TryGetValue(guid, out var name) ? name : null;

    /// <returns>The raw <c>classid</c> (braced, uppercase) for <paramref name="friendlyName"/>, or null when it isn't a recognized name.</returns>
    public static string? TryGetClassId(string friendlyName) =>
        ByName.TryGetValue(friendlyName, out var guid) ? FormatClassId(guid) : null;

    public static bool IsKnownFriendlyName(string friendlyName) => ByName.ContainsKey(friendlyName);

    /// <summary>
    /// The raw <c>classid</c> <paramref name="control"/> would actually
    /// write to FormXML — <see cref="FormControl.Control"/> reversed via
    /// this table when set, <see cref="FormControl.CustomControlId"/>
    /// verbatim otherwise, falling back to the legacy
    /// <see cref="FormControl.ClassId"/> for a `*.form.yml` exported
    /// before this split existed. Null when none of the three are set, or
    /// when <see cref="FormControl.Control"/> names something
    /// unrecognized (that's a validation error — see
    /// <see cref="FormControlValidator"/> — not silently treated as "no
    /// classid" here).
    /// </summary>
    public static string? Resolve(FormControl control) =>
        control.Control is not null ? TryGetClassId(control.Control)
        : control.CustomControlId ?? control.ClassId;

    private static string FormatClassId(Guid guid) => $"{{{guid.ToString().ToUpperInvariant()}}}";
}
