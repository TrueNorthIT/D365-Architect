namespace D365Architect.Services.Conversion;

/// <summary>
/// Just enough about a live form to list it for a human to choose from —
/// its id (for <c>form export --form-id</c> and for driving the actual
/// export once chosen), name, and type. See
/// <see cref="FormJsonDefinitionReader.ReadSummaries(string, System.Collections.Generic.IReadOnlySet{System.Guid}?)"/>.
/// Deliberately not <see cref="Models.FormDefinition"/>: this exists purely
/// to populate `form export`'s interactive picker cheaply, before
/// committing to fetching and decomposing the one actually chosen — it's
/// never written out as YAML itself.
/// </summary>
public sealed record FormSummary(Guid FormId, string Name, string Type);
