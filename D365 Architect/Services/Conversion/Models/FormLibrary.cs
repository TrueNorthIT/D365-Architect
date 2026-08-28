using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>A JavaScript web resource library this form loads — FormXML's `&lt;formLibraries&gt;/&lt;Library&gt;`.</summary>
public sealed class FormLibrary
{
    /// <summary>The web resource's path, e.g. "AppCommon/Account/Account_main_system_library.js".</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }
}
