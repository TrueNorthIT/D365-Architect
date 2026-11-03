using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// This repository is public: the committed test forms must be anonymised. Scans every
/// <c>Fixtures/FormXml/*.xml</c> for the words (customer names, publisher prefixes) that gave the real forms away.
/// The words are listed as SHA-256 hashes so this test doesn't publish them itself.
/// </summary>
public sealed class FormXmlFixtureHygieneTests
{
    // ponytail: hashes keep the words out of plain text and search, not secret; short ones can be brute-forced.
    private static readonly HashSet<string> ForbiddenWordHashes =
    [
        "07e07b32212ceda48aeb8524b04bb949130db8cb19ddf6eb7d976682024550d9",
        "c9996c0e58a02181c81e9003846761e38039c7f664fe2ba737876ba76b5a7685",
        "6db05ea97727c665a8b80415fde958bc4def376b18a77f344f2d44be20eed92a",
        "590d75a7b5b50aa3822c02738e5fc2059ba09fcb62a17339323e9d6b61c6fc70",
        "7ac3fda7d6a4a6c2d5d1eb86d34c0f992cdae3f5a64fce7109b0eeffde3cdc5d",
    ];

    // Example: a fixture containing a forbidden publisher prefix as a word ("xx_account" -> "xx") -> the test fails and names the file and word.
    [Fact]
    public void CommittedFormFixtures_ContainNothingThatIdentifiesACustomer()
    {
        var directory = Path.GetDirectoryName(FormPatchFixtures.FixturePath("any.xml"))!;
        Expect.True(Directory.Exists(directory), $"The fixtures folder should be copied to the test output: <{directory}>.");
        var files = Directory.GetFiles(directory, "*.xml");
        Expect.True(files.Length > 0, $"There should be at least one form fixture to scan in <{directory}>.");

        var offences = files
            .SelectMany(file => Regex.Split(File.ReadAllText(file).ToLowerInvariant(), "[^a-z0-9]+")
                .Distinct()
                .Where(word => ForbiddenWordHashes.Contains(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(word)))))
                .Select(word => $"{Path.GetFileName(file)} contains '{word}'"))
            .ToList();

        Expect.Empty(offences, "Committed form fixtures must be anonymised.");
    }
}
