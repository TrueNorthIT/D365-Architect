using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// What the fake-backed security tests can't prove: that privileges this tool writes (<see cref="SecurityImportService"/>, i.e.
/// <c>AddPrivilegesRole</c>) land in Dataverse exactly as the file asked - in the exported solution XML a pipeline commits, the role's
/// <c>&lt;RolePrivileges&gt;</c> are its starting privileges plus the requested ones at the matching level and nothing else - and that
/// what <see cref="SecurityExportService"/> then reads back imports again as "no changes". A new role is never empty (Dataverse gives
/// it default SharePoint, plug-in and SDK-message read privileges), so the test compares with the role's own export before the import.
/// Live test: skipped unless <see cref="LiveDataverse.Settings"/> is set (see docs/DEVELOPING.md); the role and solution it creates are
/// deleted in <see cref="DisposeAsync"/>, pass or fail.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveCollection.Name)]
public sealed class SecurityRoleShapeLiveTests : IAsyncLifetime
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(10);

    private readonly List<Guid> _rolesToDelete = [];
    private Guid? _solutionToDelete;
    private LiveDataverse? _live;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_live is null)
        {
            return;
        }

        var failures = new List<Exception>();
        foreach (var role in _rolesToDelete)
        {
            try
            {
                await _live.DeleteRoleAsync(role, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (_solutionToDelete is { } solutionId)
        {
            try
            {
                await _live.DeleteSolutionAsync(solutionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _live.Dispose();
        if (failures.Count > 0)
        {
            throw new AggregateException("Live test clean-up failed; delete the leftover <prefix>_lt* components by hand.", failures);
        }
    }

    [LiveFact]
    public async Task ImportedPrivileges_ExportAsRequestedAndRoundTrip()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        _live = await LiveDataverse.ConnectAsync(ct);
        var solution = _live.NewName();
        _solutionToDelete = await _live.CreateSolutionAsync(solution, ct);
        var roleName = _live.NewName("role");
        _rolesToDelete.Add(await _live.CreateRoleAsync(roleName, solution, ct));

        var before = RolePrivileges(await _live.ExportCustomizationsXmlAsync(solution, ct), roleName);

        // Every access level, on a standard table so no table has to be created.
        var file = new TableSecurityDefinition
        {
            Table = "account",
            Roles =
            [
                new TableRoleSecurity
                {
                    Name = roleName,
                    Create = SecurityAccessLevel.User,
                    Read = SecurityAccessLevel.Organisation,
                    Write = SecurityAccessLevel.BusinessUnit,
                    Delete = SecurityAccessLevel.None,
                    Append = SecurityAccessLevel.ParentChildBusinessUnit,
                    AppendTo = SecurityAccessLevel.Organisation,
                    Assign = SecurityAccessLevel.User,
                    Share = SecurityAccessLevel.User,
                },
            ],
        };
        var importer = new SecurityImportService(_live.Client);
        var preview = await importer.PreviewTableSecurityAsync(_live.EnvironmentUrl, _live.AccessToken, file, allowRemovals: false, ct);
        Expect.True(preview.HasChanges, "The new role should need changes.");
        await importer.ApplyAsync(_live.EnvironmentUrl, _live.AccessToken, preview, ct);

        // The solution XML: the starting privileges, plus exactly the requested ones at the platform's level names.
        var after = RolePrivileges(await _live.ExportCustomizationsXmlAsync(solution, ct), roleName);
        var expected = new SortedSet<string>(before, StringComparer.Ordinal)
        {
            "prvCreateAccount=Basic", "prvReadAccount=Global", "prvWriteAccount=Local", "prvAppendAccount=Deep",
            "prvAppendToAccount=Global", "prvAssignAccount=Basic", "prvShareAccount=Basic",
        };
        Expect.Equal(string.Join("\n", expected), string.Join("\n", after), $"Exported <RolePrivileges> should be the role's starting privileges plus the 7 imported.\nBefore:\n{string.Join("\n", before)}");

        // Round trip: the tool's own export of the role imports back as no changes.
        var exported = await new SecurityExportService(_live.Client).ExportSecurityAsync(_live.EnvironmentUrl, _live.AccessToken, solution, tables: null, roles: [roleName], ct);
        var yaml = Assert.Single(exported.Roles).Yaml;
        var again = await importer.PreviewRoleAsync(_live.EnvironmentUrl, _live.AccessToken, RoleYamlDeserializer.FromYaml(yaml), solution, allowRemovals: true, ct);
        Expect.True(!again.HasChanges, $"Re-importing the exported role file should change nothing.\n{yaml}");
    }

    // name=level of each <RolePrivilege> of the role in the exported customizations.xml, sorted.
    private static SortedSet<string> RolePrivileges(System.Xml.Linq.XDocument customizations, string roleName)
    {
        var role = customizations.Descendants("Role").SingleOrDefault(r => (string?)r.Attribute("name") == roleName);
        Expect.True(role is not null, $"The exported customizations.xml should contain role '{roleName}'.");
        return new SortedSet<string>(
            role!.Element("RolePrivileges")?.Elements("RolePrivilege").Select(p => $"{(string)p.Attribute("name")!}={(string)p.Attribute("level")!}") ?? [],
            StringComparer.Ordinal);
    }
}
