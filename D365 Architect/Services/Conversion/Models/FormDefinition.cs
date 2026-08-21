using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated, hand-designed YAML shape for a single Dynamics form (a Dataverse
/// <c>systemform</c> record) — the <see cref="ViewDefinition"/> counterpart
/// for forms. Sourced live from the Dataverse Web API (see
/// <see cref="Dataverse.IDataverseClient.GetFormDefinitionsJsonAsync"/>).
///
/// As with views, there's no XML-vs-JSON reader split: <c>formxml</c> comes
/// back from the Web API as a plain string property, not wrapped in the
/// metadata API's managed-property shapes.
///
/// Deliberately excluded: <c>formid</c>/<c>formidunique</c> — GUIDs with no
/// meaning to a human editing this YAML; <see cref="Name"/> is this asset's
/// only practical identity, same reasoning as <see cref="ViewDefinition.Name"/>.
/// Also excluded: <c>formpresentation</c> (Classic/Air/ConvertedIC) — a
/// rendering detail rather than something worth round-tripping until a real
/// need for it shows up.
/// </summary>
public sealed class FormDefinition
{
    /// <summary>The form's display name, e.g. "Account Main Form". Serialises as the top-level "form" key.</summary>
    [YamlMember(Alias = "form", Order = 0)]
    public required string Name { get; init; }

    /// <summary>Logical name of the table this form belongs to, e.g. "account" (the systemform's <c>objecttypecode</c>).</summary>
    [YamlMember(Order = 1)]
    public required string Entity { get; init; }

    [YamlMember(Order = 2)]
    public string? Description { get; init; }

    /// <summary>
    /// The kind of form this is, e.g. "Quick Create" or "Dashboard" — the
    /// systemform "type" option set's own label (see
    /// https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/systemform).
    /// Only present when it's something other than "Main" — an ordinary
    /// main form, and by far the most common case.
    /// </summary>
    [YamlMember(Order = 3)]
    public string? Type { get; init; }

    /// <summary>Only present when true — a table typically has one default form per <see cref="Type"/>, and false is the common case for any given form.</summary>
    [YamlMember(Order = 4)]
    public bool? IsDefault { get; init; }

    /// <summary>
    /// "Inactive" for a form that hasn't been published/activated yet.
    /// Absent — Dataverse's "Active" state — is the common case for any
    /// form that's actually in use.
    /// </summary>
    [YamlMember(Order = 5)]
    public string? FormActivationState { get; init; }

    /// <summary>Whether this form can be customized. Absent when Dataverse doesn't report it.</summary>
    [YamlMember(Order = 6)]
    public bool? IsCustomizable { get; init; }

    /// <summary>
    /// The form's layout and controls, as FormXML. Null for the same kind of
    /// internal/system case a view's FetchXML can be missing for — see
    /// <see cref="ViewDefinition.FetchXml"/>; not yet hit live for a form,
    /// but not assumed away either.
    /// </summary>
    [YamlMember(Order = 7, ScalarStyle = ScalarStyle.Literal)]
    public string? FormXml { get; init; }
}
