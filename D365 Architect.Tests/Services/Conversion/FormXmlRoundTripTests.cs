using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issues #58/#59: a real form exported to YAML and built back with no edits must come back unchanged, as
/// <see cref="FormXmlComparer"/> sees it. Also pins what must NOT change: the from-scratch build of a brand-new form.
/// </summary>
public sealed class FormXmlRoundTripTests
{
    // ---- an unedited round trip is lossless ----

    // Example: Account fixture -> model -> YAML -> model -> FormXmlWriter.Write on top of the fixture -> 0 differences.
    // Code path: FormXmlWriter.Write(form, existingForm) with every block taken from the live FormXML.
    [Fact]
    public void RoundTrip_UneditedAccountFixture_ReportsNoDifferences()
    {
        var live = AccountMain();

        var rebuilt = Patch(live, ThroughYaml(AccountModel()));

        var differences = FormXmlComparer.Compare(live, rebuilt);
        Expect.Empty(differences, $"An unedited export then build must reproduce the form exactly. {Show(differences)}");
    }

    [Fact]
    public void RoundTrip_UneditedSmallForm_ReportsNoDifferences()
    {
        var live = SmallLive();

        var rebuilt = Patch(live, ThroughYaml(ModelOf(live)));

        var differences = FormXmlComparer.Compare(live, rebuilt);
        Expect.Empty(differences, $"An unedited export then build must reproduce the form exactly (spacers, empty row, label settings, locklevel, application, ids). {Show(differences)}");
    }

    // ---- creating a brand-new form works as it does today ----

    private static FormDefinition ScratchForm() => new()
    {
        Name = "Scratch",
        Entity = "tn_test",
        Tabs =
        [
            new FormTab
            {
                Name = "general",
                Label = "General",
                Columns =
                [
                    new FormColumn
                    {
                        Width = "100%",
                        Sections =
                        [
                            new FormSection
                            {
                                Name = "details",
                                Label = "Details",
                                Controls =
                                [
                                    new FormControl { Id = "tn_name", Field = "tn_name", Label = "Name", Control = "SingleLineText" },
                                    new FormControl { Id = "tn_phone", Field = "tn_phone", Label = "Phone", Control = "SingleLineText", Disabled = true },
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
        HeaderControls = [new FormControl { Id = "header_tn_name", Field = "tn_name", Label = "Name", Control = "SingleLineText" }],
        Libraries = [new FormLibrary { Name = "tn_/lib.js" }],
        Events =
        [
            new FormEvent
            {
                Name = "onload",
                Handlers = [new FormEventHandler { FunctionName = "Tn.onLoad", LibraryName = "tn_/lib.js", Enabled = true, PassExecutionContext = true }],
            },
        ],
    };

    // Example: a form with no live counterpart is built from scratch exactly as before the patcher existed (same ids, same attributes, same order).
    // Code path: FormXmlWriter.Write(form) with no existing form, and with removals allowed (which must change nothing for a new form).
    [Fact]
    public void Write_BrandNewForm_BuildsFromScratchExactlyAsItAlwaysHas()
    {
        var form = ScratchForm();

        Expect.Equal(GoldenScratchFormXml, FormXmlWriter.Write(form), "Building a new form from scratch must not change.");
        Expect.Equal(GoldenScratchFormXml, FormXmlWriter.Write(form, existingForm: null, allowRemovals: true), "allowRemovals must make no difference when there is no live form.");
    }

    // Produced by FormXmlWriter.Write(ScratchForm()) before the patcher work started.
    private const string GoldenScratchFormXml = """<form><tabs><tab id="{348152CD-BB90-F491-FF44-8DD4E2F8CB71}" name="general"><labels><label description="General" languagecode="1033" /></labels><columns><column width="100%"><sections><section id="{150CB935-CADE-60EE-ED8A-EE45509108E0}" name="details"><labels><label description="Details" languagecode="1033" /></labels><rows><row><cell id="{1FA20D32-186C-6BEC-2AC1-DD5D8ACED1CC}"><labels><label description="Name" languagecode="1033" /></labels><control id="tn_name" datafieldname="tn_name" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" /></cell></row><row><cell id="{C4A5ABDC-4147-6971-DDA9-459A9B2E393C}"><labels><label description="Phone" languagecode="1033" /></labels><control id="tn_phone" datafieldname="tn_phone" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" disabled="true" /></cell></row></rows></section></sections></column></columns></tab></tabs><header id="{95B99F09-6F34-C731-49F6-E40DB0F395E3}"><rows><row><cell id="{C8CD67C4-F98F-71BB-A553-F1A0E3A5F866}"><labels><label description="Name" languagecode="1033" /></labels><control id="header_tn_name" datafieldname="tn_name" classid="{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" /></cell></row></rows></header><events><event name="onload"><Handlers><Handler functionName="Tn.onLoad" libraryName="tn_/lib.js" handlerUniqueId="{17B79CB5-0C62-C33E-A396-C49ED008F1FE}" enabled="true" passExecutionContext="true" /></Handlers></event></events><formLibraries><Library name="tn_/lib.js" libraryUniqueId="{16536B18-39F4-DC50-20A1-42A0EFB8764A}" /></formLibraries></form>""";
}
