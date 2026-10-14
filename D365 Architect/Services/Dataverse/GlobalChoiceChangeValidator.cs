using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Catches the common ways a global choice create would fail before
/// <c>choice import</c> ever sends a request — the <c>GlobalChoiceDefinition</c>
/// counterpart to <see cref="AttributeChangeValidator"/>, reusing the same
/// <see cref="DataverseSchemaNaming.SchemaNamePattern"/> a custom column's
/// SchemaName is checked against (a global choice's own <c>Name</c> needs
/// the identical customization-prefix shape) and the same "never invent an
/// option Value" policy <see cref="AttributeOptionDefinition"/> documents.
/// </summary>
internal static class GlobalChoiceChangeValidator
{
    /// <returns>Why creating <paramref name="local"/> would fail, or null when it looks safe to attempt.</returns>
    public static string? ValidateCreate(GlobalChoiceDefinition local)
    {
        if (!DataverseSchemaNaming.SchemaNamePattern.IsMatch(local.Name))
        {
            var example = local.Name.Contains('_') ? "letters/digits/underscores only, e.g. 'new_colors'" : $"a customization prefix, e.g. 'new_{local.Name}'";
            return $"Name '{local.Name}' isn't valid — expected {example}, and this tool never invents or corrects one.";
        }

        if (local.Options is null || local.Options.Count == 0)
        {
            return $"'{local.Name}' has no Options in the local YAML — required to create a global choice, and this tool never invents choice values.";
        }

        var duplicateValues = local.Options.GroupBy(o => o.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateValues.Count > 0)
        {
            return $"'{local.Name}' has duplicate option Value(s): {string.Join(", ", duplicateValues)}.";
        }

        return null;
    }
}
