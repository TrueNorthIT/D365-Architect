using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// REQ-22, REQ-29, REQ-31 and REQ-33 for forms. A form file with no
/// <c>formId</c> is matched by name AND type (<c>type</c> absent = main) through
/// <see cref="IDataverseClient.FindSystemFormsAsync"/>; no match creates a form,
/// more than one refuses and lists the candidates, one updates it as today.
/// The fake's live forms are a fresh table's three "Information" forms (main,
/// card, quick view), so the type filter is what tells them apart, as live.
///
/// The create body is the writable SystemForm columns Learn lists - <c>name</c>,
/// <c>objecttypecode</c>, <c>type</c> (the option-set integer: 2 Main, 6 Quick View
/// Form, 7 Quick Create, 11 Card), <c>formxml</c> - posted to <c>systemforms</c>
/// (https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/systemform ).
/// Learn has no worked example of a form create, so the minimum set is unconfirmed until live.
/// </summary>
public sealed class FormImportServiceCreateTests
{
    private static FormImportService CreateService(FakeDataverseClient client) => new(client, new FormJsonDefinitionReader());

    private static FakeDataverseClient ClientWithFreshTableForms()
    {
        var client = ClientWithTable();
        AddFreshTableForms(client);
        return client;
    }

    private static Task<FormImportPreview> Preview(FakeDataverseClient client, FormDefinition form) =>
        CreateService(client).PreviewAsync(EnvironmentUrl, "token", form, CancellationToken.None);

    private static async Task<(FakeDataverseClient Client, FormImportPreview Preview)> Apply(FakeDataverseClient client, FormDefinition form, string? solution = null)
    {
        var service = CreateService(client);
        var preview = await service.PreviewAsync(EnvironmentUrl, "token", form, CancellationToken.None);
        await service.ApplyAsync(EnvironmentUrl, "token", preview, solution, CancellationToken.None);
        return (client, preview);
    }

    // ---- REQ-29: no match creates ----

    // REQ-29 example: `form: Invoice Centre` on a new table -> `+ form 'Invoice Centre' (main, create)`.
    // The table's forms are all "Information"; nothing is named "Invoice Centre", so it's a create. Code path: FormImportService.PreviewAsync (today throws FormNotFoundException).
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_NoFormIdAndNoFormOfThatNameAndType_IsACreate()
    {
        var preview = await Preview(ClientWithFreshTableForms(), FormOf("Invoice Centre"));

        Expect.True(preview.IsCreate, "REQ-29: a form file with no formId matching no form by name + type should preview as a create, not throw FormNotFoundException.");
        Expect.True(preview.HasChanges, "REQ-29: a create is always a change to show and confirm.");
    }

    // Precondition for the command-level tests too: a form built from scratch passes the blocking schema checks, so a create isn't refused for the wrong reason.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_Create_HasNoBlockingSchemaViolations()
    {
        var preview = await Preview(ClientWithFreshTableForms(), FormOf("Invoice Centre"));

        Expect.True(preview.Violations.All(v => v.IsKnownHarmless), "REQ-29: a freshly built form should not carry blocking schema violations. Found: " + string.Join(" | ", preview.Violations.Where(v => !v.IsKnownHarmless).Select(v => v.Message)));
    }

    // The body: name, table, type as an integer, and the form's rebuilt FormXML.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task ApplyAsync_Create_PostsNameTableTypeAndTheRebuiltFormXml()
    {
        var form = FormOf("Invoice Centre");

        var (client, preview) = await Apply(ClientWithFreshTableForms(), form);

        var body = Assert.Single(client.CreateSystemFormCalls).Body;
        Expect.Equal("Invoice Centre", (string?)body["name"], "REQ-29: the created form should carry the file's name.");
        Expect.Equal(TableName, (string?)body["objecttypecode"], "REQ-29: 'objecttypecode' should be the table's logical name.");
        Expect.Equal(MainType, (int?)body["type"], "REQ-29: a form with no type is a main form (type 2).");
        Expect.Equal(FormXmlWriter.Write(form), (string?)body["formxml"], "REQ-29: 'formxml' should be what FormXmlWriter builds from the file, the same document the plan validated.");
        Expect.Equal(preview.NewFormXml, (string?)body["formxml"], "REQ-29: and the one in the preview.");
    }

    // REQ-29: "existing forms such as 'Information' are left alone."
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task ApplyAsync_Create_LeavesTheExistingInformationFormsAlone()
    {
        var (client, _) = await Apply(ClientWithFreshTableForms(), FormOf("Invoice Centre"));

        Expect.Empty(client.UpdateSystemFormXmlCalls, "REQ-29: creating 'Invoice Centre' must not rewrite any of the table's existing 'Information' forms.");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "REQ-29: exactly one form should be created.");
    }

    // REQ-29: type default "main" only when absent; the label is matched case-insensitively ("main" is how a human writes it) and
    // the same integer is sent as the type on the create. Labels are the ones `form export` writes (FormJsonDefinitionReader's TypeNames).
    [Theory]
    [Trait("Requirement", "REQ-29")]
    [InlineData(null, 2)]
    [InlineData("Main", 2)]
    [InlineData("main", 2)]
    [InlineData("Quick View Form", 6)]
    [InlineData("Quick Create", 7)]
    [InlineData("Card", 11)]
    public async Task ApplyAsync_FormType_IsMatchedOnAndCreatedWithTheOptionSetValue(string? type, int expectedCode)
    {
        var (client, _) = await Apply(ClientWithFreshTableForms(), FormOf("Invoice Centre", type: type));

        var lookup = Assert.Single(client.FindSystemFormsCalls);
        Expect.Equal(expectedCode, lookup.FormType, $"REQ-29: type '{type ?? "(absent)"}' should look existing forms up as type {expectedCode}.");
        Expect.Equal(expectedCode, (int?)Assert.Single(client.CreateSystemFormCalls).Body["type"], $"REQ-29: type '{type ?? "(absent)"}' should be created as type {expectedCode}.");
    }

    // A type label that isn't one of Dataverse's is refused, not guessed.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_UnknownFormType_IsRefusedNamingIt()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithFreshTableForms(), FormOf("Invoice Centre", type: "Fancy")));

        Expect.Contains("Fancy", ex.Message, "REQ-29: an unrecognised 'type' should be refused, naming it.");
    }

    // ---- REQ-29: matching resolves the "three forms named Information" case ----

    // Live fact: a fresh table's main, card and quick view forms are all named "Information". A file named "Information"
    // with no formId and type main (or none) now matches exactly the main one, where it used to fail as ambiguous.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_NameOfThreeInformationFormsButTypeMain_UpdatesTheMainOne()
    {
        var client = ClientWithFreshTableForms();

        var (_, preview) = await Apply(client, FormOf("Information", label: "Information, edited"));

        Expect.True(!preview.IsCreate, "REQ-29: a form named 'Information' of type main exists, so this is an update.");
        var call = Assert.Single(client.UpdateSystemFormXmlCalls);
        Expect.Equal(MainInformationId, call.FormId, "REQ-29: the update should go to the main 'Information' form, not the card or quick view one.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-29: matching an existing form must not create another.");
    }

    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_NameOfThreeInformationFormsButTypeCard_UpdatesTheCardOne()
    {
        var client = ClientWithFreshTableForms();

        await Apply(client, FormOf("Information", type: "Card", label: "Information, edited"));

        Expect.Equal(CardInformationId, Assert.Single(client.UpdateSystemFormXmlCalls).FormId, "REQ-29: 'type: Card' should pick the card 'Information' form.");
    }

    // ---- REQ-31: more than one match refuses and lists ----

    // REQ-31 example: Two main forms named "Information" -> refusal listing both ids.
    [Fact]
    [Trait("Requirement", "REQ-31")]
    public async Task PreviewAsync_TwoMainFormsWithTheSameName_RefusesListingBothIds()
    {
        var first = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
        var second = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var client = ClientWithTable();
        client.LiveSystemForms.Add(InformationForm(first, MainType));
        client.LiveSystemForms.Add(InformationForm(second, MainType));

        var ex = await Assert.ThrowsAsync<AmbiguousSystemFormException>(() => Preview(client, FormOf("Information")));

        Expect.Contains(first.ToString(), ex.Message, "REQ-31: the refusal should list the first matching form's id so the user can add the right formId.");
        Expect.Contains(second.ToString(), ex.Message, "REQ-31: the refusal should list the second matching form's id.");
        Expect.Equal(2, ex.CandidateIds.Count, "REQ-31: AmbiguousSystemFormException.CandidateIds should carry both ids.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-31: an ambiguous match must not create a third form.");
        Expect.Empty(client.UpdateSystemFormXmlCalls, "REQ-31: an ambiguous match must not guess which form to update.");
    }

    // ---- A formId is authoritative: it never creates ----

    // A formId that resolves to nothing is still FormNotFoundException (the form was deleted), never a quiet create.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task PreviewAsync_FormIdThatResolvesToNothing_IsStillAnErrorNeverACreate()
    {
        var client = ClientWithFreshTableForms(); // SystemForm stays null: no form has this id

        var ex = await Assert.ThrowsAsync<FormNotFoundException>(() => Preview(client, FormOf("Invoice Centre", formId: Guid.NewGuid())));

        Expect.True(ex.FormId is not null, "REQ-29: the by-id refusal should still name the id.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-29: a stale formId must never create a form.");
    }

    // ---- REQ-22: --solution ----

    // REQ-22: "anything they create ... is added to that solution".
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_CreateWithASolution_CreatesTheFormInThatSolution()
    {
        var (client, _) = await Apply(ClientWithFreshTableForms(), FormOf("Invoice Centre"), solution: "TableCreateTest");

        Expect.Equal("TableCreateTest", Assert.Single(client.CreateSystemFormCalls).SolutionUniqueName, "REQ-22: the created form should carry 'TableCreateTest' as the solution.");
    }

    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_CreateWithoutASolution_SendsNoSolution()
    {
        var (client, _) = await Apply(ClientWithFreshTableForms(), FormOf("Invoice Centre"), solution: null);

        Expect.Equal(null, Assert.Single(client.CreateSystemFormCalls).SolutionUniqueName, "REQ-22: with no --solution the create should name no solution.");
    }

    // REQ-22: "... or change" - an existing form that is updated joins the solution too (Learn: Update (PATCH) accepts the header).
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_UpdateWithASolution_ChangesTheFormInThatSolution()
    {
        var client = ClientWithFreshTableForms();

        await Apply(client, FormOf("Information", label: "Information, edited"), solution: "TableCreateTest");

        Expect.Equal("TableCreateTest", Assert.Single(client.UpdateSystemFormXmlCalls).SolutionUniqueName, "REQ-22: a changed form should be added to the solution the import was given.");
    }

    // ---- REQ-33: created forms are published ----

    // REQ-33: "Created forms are published, as form import already does." PublishAsync publishes the form's table - for a create, the YAML's own.
    [Fact]
    [Trait("Requirement", "REQ-33")]
    public async Task PublishAsync_AfterACreate_PublishesTheFormsTable()
    {
        var fake = ClientWithFreshTableForms();
        var svc = CreateService(fake);
        var preview = await svc.PreviewAsync(EnvironmentUrl, "token", FormOf("Invoice Centre"), CancellationToken.None);
        await svc.ApplyAsync(EnvironmentUrl, "token", preview, solutionUniqueName: null, CancellationToken.None);

        await svc.PublishAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        Expect.Sequence([TableName], fake.PublishEntityCalls, "REQ-33: after a create, the form's table should be published.");
    }
}
