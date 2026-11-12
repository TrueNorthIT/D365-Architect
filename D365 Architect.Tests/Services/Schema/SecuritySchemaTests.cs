using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Schema;
using Xunit;
using D365Architect.Tests.TestSupport;

namespace D365Architect.Tests.Services.Schema;

/// <summary>
/// Issue #21 (security role privileges as YAML), REQ-21: "`schema export --for security` and `--for role` produce JSON schemas."
/// Example: VS Code flags `delete: Everyone` as invalid. Covers <see cref="YamlSchemaGenerator"/> over the new models
/// (<see cref="TableSecurityDefinition"/> for <c>*.security.yml</c>, <see cref="RoleDefinition"/> for <c>*.role.yml</c>): the schema must
/// constrain every access level to the five allowed values, which is what makes an editor reject <c>delete: Everyone</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecuritySchemaTests
{
    private static readonly string[] AccessLevels = ["BusinessUnit", "None", "Organisation", "ParentChildBusinessUnit", "User"];

    private static List<string> EnumValues(JsonNode? node)
    {
        var values = node?["enum"]?.AsArray().Select(v => (string)v!).OrderBy(v => v, StringComparer.Ordinal).ToList();
        Assert.True(values is not null, $"expected an \"enum\" listing the access levels, but the schema node was: <{node?.ToJsonString()}>.");
        return values!;
    }

    // REQ-21: the table security schema: table and roles are required, a role needs a name, and each of the eight privilege keys is one of the five access levels.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public void Generate_TableSecurity_ConstrainsEveryPrivilegeToTheFiveAccessLevels()
    {
        var schema = YamlSchemaGenerator.Generate(typeof(TableSecurityDefinition), "Table security", "Who can do what to a table");

        var required = schema["required"]!.AsArray().Select(v => (string)v!).OrderBy(v => v, StringComparer.Ordinal).ToList();
        Expect.Sequence(["roles", "table"], required, "REQ-21: table and roles are required in a table security file.");

        var role = schema["properties"]!["roles"]!["items"]!;
        Expect.Equal("array", (string)schema["properties"]!["roles"]!["type"]!, "REQ-21: roles is a list.");
        Expect.Equal("string", (string)role["properties"]!["name"]!["type"]!, "REQ-21: a role is identified by its name, a string.");
        foreach (var privilege in new[] { "create", "read", "write", "delete", "append", "appendTo", "assign", "share" })
        {
            Expect.Sequence(AccessLevels, EnumValues(role["properties"]![privilege]), $"REQ-21: '{privilege}' should accept only None, User, BusinessUnit, ParentChildBusinessUnit or Organisation, so an editor flags 'delete: Everyone'.");
        }
    }

    // REQ-21: the role schema: non-table privileges are a map of privilege name to access level.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public void Generate_Role_ConstrainsNonTablePrivilegesToTheFiveAccessLevels()
    {
        var schema = YamlSchemaGenerator.Generate(typeof(RoleDefinition), "Role", "A security role's other privileges");

        Expect.Sequence(["name"], schema["required"]!.AsArray().Select(v => (string)v!).ToList(), "REQ-21: only the role's name is required in a role file.");
        var privileges = schema["properties"]!["privileges"]!;
        Expect.Equal("object", (string)privileges["type"]!, "REQ-21: privileges is a map keyed by platform privilege name.");
        Expect.Sequence(AccessLevels, EnumValues(privileges["additionalProperties"]), "REQ-21: each non-table privilege should accept only the five access levels.");
    }

    // REQ-16/21: outside tables are a map of table name to the same eight keys, each one of the five access levels.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public void Generate_Role_ConstrainsOutsideTablePrivilegesToTheFiveAccessLevels()
    {
        var schema = YamlSchemaGenerator.Generate(typeof(RoleDefinition), "Role", "A security role's other privileges");

        var table = schema["properties"]!["tables"]!["additionalProperties"]!;
        foreach (var privilege in new[] { "create", "read", "write", "delete", "append", "appendTo", "assign", "share" })
        {
            Expect.Sequence(AccessLevels, EnumValues(table["properties"]![privilege]), $"REQ-16/21: tables: <table>: '{privilege}' should accept only the five access levels.");
        }
    }
}
