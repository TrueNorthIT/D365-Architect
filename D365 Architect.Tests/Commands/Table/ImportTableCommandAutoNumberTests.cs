using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// What <c>table import</c> prints for auto-number columns (the plan
/// line, and the "seed ignored" note): <see cref="ImportTableCommand"/> run
/// through the real <see cref="ImportRunner"/> and <see cref="TableImportService"/>
/// with <c>--whatif</c>, Dataverse (<see cref="FakeDataverseClient"/>), sign-in
/// and the console faked.
///
/// Sits in the "Console" collection because it swaps the process-wide
/// <c>AnsiConsole.Console</c>; see <see cref="ConsoleHarness"/>.
/// Tests marked "guard" already pass against the current code.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportTableCommandAutoNumberTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportTableCommandAutoNumberTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteTableFile(AttributeDefinition column)
    {
        var path = Path.Combine(_directory, "tn_test.table.yml");
        File.WriteAllText(path, EntityYamlSerializer.ToYaml(new EntityDefinition { LogicalName = "tn_test", Attributes = [column] }));
        return path;
    }

    private static AttributeDefinition AutoNumberColumn(string format, int? seed) =>
        AttributeDefinitionBuilder.Create("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b =>
        {
            b.Name = RichTextAndAutoNumberFixtures.AutoNumberLogicalName;
            b.AutoNumberFormat = format;
            b.AutoNumberSeed = seed;
        });

    private static (ImportTableCommand Command, FakeDataverseClient Client) CreateCommand(string liveEntityJson, string? liveAttributeJson = null)
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = liveEntityJson };
        if (liveAttributeJson is not null)
        {
            client.AttributeMetadataJson = (_, _) => liveAttributeJson;
        }

        var service = new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        return (new ImportTableCommand(service, new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportTableCommand.Settings WhatIf(string input) => new() { Input = input, WhatIf = true };

    // Example: "Plan shows `+ bogdan_ref (create, auto-number seed 1 — this environment only)`."
    // Code path: ImportTableCommand.PrintPlanLine (Create case) using AttributeImportPlan.AutoNumberSeed.
    [Fact]
    public async Task RunAsync_NewSeededAutoNumberColumn_PlanLineStatesTheSeedAndThatItIsPerEnvironment()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(RichTextAndAutoNumberFixtures.Entity());

        var exitCode = await command.RunAsync(WhatIf(WriteTableFile(AutoNumberColumn("MSUM{SEQNUM:6}", seed: 1))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--whatif on a valid file should succeed. Output: <{console.Output}>");
        Expect.Contains("+ bogdan_ref (create, auto-number seed 1 — this environment only)", console.Output,
            "the plan should say a seed will be set and that it applies to this environment only.");
        Expect.Empty(client.SetAutoNumberSeedCalls, "--whatif must not set the seed.");
    }

    // No seed, no seed wording: a new auto-number column without autoNumberSeed prints the plain create line. Guard.
    // Code path: ImportTableCommand.PrintPlanLine (Create case).
    [Fact]
    public async Task RunAsync_NewAutoNumberColumnWithoutSeed_PlanLineIsAPlainCreate()
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand(RichTextAndAutoNumberFixtures.Entity());

        await command.RunAsync(WhatIf(WriteTableFile(AutoNumberColumn("MSUM{SEQNUM:6}", seed: null))), CancellationToken.None);

        Expect.Contains("+ bogdan_ref (create)", console.Output, "without a seed the plan line should be the ordinary '+ bogdan_ref (create)'.");
    }

    // The "seed ignored — column already exists" note: the column exists and is otherwise unchanged, so the run has
    // no writes - but the maker who added autoNumberSeed must still be told it did nothing, not left with a bare "No changes".
    // Code path: ImportTableCommand (SkipBeforePrinting / PrintPlanLine) surfacing AttributeImportPlan.Warnings of an Unchanged plan.
    [Fact]
    public async Task RunAsync_SeedOnAnExistingUnchangedColumn_TellsTheUserTheSeedWasIgnored()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString));

        var exitCode = await command.RunAsync(WhatIf(WriteTableFile(AutoNumberColumn("MSUM{SEQNUM:6}", seed: 1))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"a run with nothing to write should still succeed. Output: <{console.Output}>");
        Expect.Contains("seed ignored", console.Output, "the output should say the seed was ignored because the column already exists.");
        Expect.Empty(client.SetAutoNumberSeedCalls, "the seed must not be set on an existing column.");
    }

    // The same note when the column is also being updated (format changed): it prints beside the update line.
    // Code path: ImportTableCommand.PrintPlanLine (Warnings of an Update plan).
    [Fact]
    public async Task RunAsync_SeedOnAnExistingColumnBeingUpdated_ShowsTheUpdateAndTheIgnoredSeed()
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString), RichTextAndAutoNumberFixtures.LiveAutoNumberString);

        await command.RunAsync(WhatIf(WriteTableFile(AutoNumberColumn("MSUM{SEQNUM:8}", seed: 1))), CancellationToken.None);

        Expect.Contains("~ bogdan_ref (update)", console.Output, "the changed format should show as an update.");
        Expect.Contains("seed ignored", console.Output, "the plan should say the seed was ignored because the column already exists.");
    }
}
