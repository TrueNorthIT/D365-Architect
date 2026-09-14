using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

public sealed class DataverseSchemaNamingTests
{
    [Theory]
    [InlineData("new_BankName")]
    [InlineData("cr7a3_Account_Rating")]
    [InlineData("new_contact_new_bankaccount")]
    [InlineData("a_b")]
    public void SchemaNamePattern_ValidNames_Match(string name) =>
        Assert.Matches(DataverseSchemaNaming.SchemaNamePattern, name);

    [Theory]
    [InlineData("NoUnderscoreAtAll")]
    [InlineData("_LeadingUnderscore")]
    [InlineData("new_")] // nothing after the underscore
    [InlineData("new_Bank-Name")] // dash not allowed
    [InlineData("new_Bank Name")] // space not allowed
    [InlineData("1new_Bank")] // must start with a letter
    [InlineData("")]
    public void SchemaNamePattern_InvalidNames_DoNotMatch(string name) =>
        Assert.DoesNotMatch(DataverseSchemaNaming.SchemaNamePattern, name);

    [Fact]
    public void SchemaNamePattern_IsFullStringMatch_NotJustAPrefix()
    {
        // A trailing invalid character must fail the whole match, not just be ignored.
        Assert.DoesNotMatch(DataverseSchemaNaming.SchemaNamePattern, "new_BankName!");
    }
}
