using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>One JavaScript function bound to a <see cref="FormEvent"/> — FormXML's `&lt;Handler&gt;`.</summary>
public sealed class FormEventHandler
{
    /// <summary>The bound function's fully-qualified name, e.g. "AppCommon.Account.Instance.parentaccountid_setadditionalparams".</summary>
    [YamlMember(Order = 0)]
    public required string FunctionName { get; init; }

    /// <summary>The web resource this function comes from, e.g. "AppCommon/Account/Account_main_system_library.js".</summary>
    [YamlMember(Order = 1)]
    public required string LibraryName { get; init; }

    /// <summary>Whether this handler is enabled.</summary>
    [YamlMember(Order = 2)]
    public bool? Enabled { get; init; }

    /// <summary>Whether the platform passes its execution context as this function's first argument.</summary>
    [YamlMember(Order = 3)]
    public bool? PassExecutionContext { get; init; }
}
