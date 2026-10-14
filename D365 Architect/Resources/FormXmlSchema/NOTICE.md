# FormXML XSD schema (vendored)

`FormXml.xsd`, `RibbonCore.xsd`, `RibbonTypes.xsd`, and `RibbonWSS.xsd` are
Microsoft's own official XSD schema files for FormXML, downloaded verbatim
(unmodified) from the "Schemas.zip" package linked from
[Edit the customizations XML file with schema validation](https://learn.microsoft.com/power-apps/developer/model-driven-apps/edit-customizations-xml-file-schema-validation) —
version `9.0.0.2090`, retrieved from
`https://download.microsoft.com/download/B/9/7/B97655A4-4E46-4E51-BA0A-C669106D563F/Schemas.zip`.

Only these four of the zip's ten files are included: `FormXml.xsd`'s own
`<xs:include>` chain is `FormXml.xsd` → `RibbonCore.xsd` →
(`RibbonTypes.xsd`, `RibbonWSS.xsd`) — nothing else in the download is
needed to validate a `<form>` document, confirmed by reading each file's own
includes rather than assumed. `Fetch.xsd`, `SiteMap.xsd`, and the rest are
for validating other parts of a solution's `customizations.xml`, not forms.

Used by `FormXmlValidator` (see `Services/Conversion/FormXmlValidator.cs`)
to validate FormXML that `form build-xml` produces, embedded into the
assembly (`<EmbeddedResource>` in the `.csproj`) rather than shipped as
loose files, so this still works from the
[standalone single-file build](../../../README.md#getting-the-tool).

**Worth knowing before treating a validation result as gospel**: even this
official, downloadable XSD — not just the informal prose on Microsoft's own
FormXML schema *documentation* page — disagrees with real, live Dataverse
output in at least two confirmed ways:

- A `<form>`'s own `headerdensity` and `showinformselector` attributes (seen
  on every real form checked this project) aren't declared anywhere in
  `FormType`, and `FormType` has no `xs:anyAttribute` wildcard to fall back
  on — so a real form's FormXML can (and does) fail strict validation
  against Microsoft's own schema in ways that have nothing to do with
  whether this tool's `FormXmlWriter` rebuilt it correctly.
- The Timeline control's own `UnifiedClientTimelineWallParameters` group
  doesn't declare `UClientActivitiesConfigurationJSON` or
  `UClientNotesConfigurationJSON` — the standard, Microsoft-shipped default
  per-activity-type JSON config that control ships with on effectively
  every entity form with a timeline. Found byte-identical across two
  independently-exported real forms, so this schema download's own
  `UnifiedClientTimelineWallParameters` group is simply stale relative to
  whatever platform update added per-activity-type configuration.

See `docs/yaml-conventions.md` for the fuller list, corrected against this
actual XSD rather than the docs page's prose (which turned out to be the
less accurate of the two in a few more places — e.g. it describes
`customControl`'s `id` as required and a cell's nested `<events>` as
undocumented, while this XSD already declares both correctly).
