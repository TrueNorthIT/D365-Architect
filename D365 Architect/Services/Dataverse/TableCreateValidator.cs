using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Pre-flight checks before a table is created from a <c>*.table.yml</c> -
/// nothing here is ever guessed or defaulted. Returns every problem found
/// (empty = creatable). Stub added by the test-first pass; the behaviour is
/// pinned by <c>TableCreateValidatorTests</c>.
/// </summary>
public static class TableCreateValidator
{
    public static IReadOnlyList<string> Validate(EntityDefinition entity) => throw new NotImplementedException();
}
