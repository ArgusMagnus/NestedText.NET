using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NestedText.Tests;

[TestClass]
public class NestedTextTests
{
    const string OfficialTestCasesPath = @"official_tests\test_cases\";

    public static IEnumerable<object[]> OfficialTestsInput => GetTestsInput(OfficialTestCasesPath);

    //[DeploymentItem]
    [DynamicData(nameof(OfficialTestsInput))]
    [DataTestMethod]
    public Task OfficialTests(string ntFile, string jsonFile, bool expectError, string testCasesPath) => DoTests(ntFile, jsonFile, expectError, testCasesPath);

    public static IEnumerable<object[]> TestsInput => GetTestsInput(@"test_cases\");

    [DynamicData(nameof(TestsInput))]
    [DataTestMethod]
    public Task Tests(string ntFile, string jsonFile, bool expectError, string testCasesPath) => DoTests(ntFile, jsonFile, expectError, testCasesPath);

    static async Task DoTests(string ntFile, string jsonFile, bool expectError, string testCasesPath)
    {
        ntFile = Path.Combine(testCasesPath, ntFile);
        jsonFile = Path.Combine(testCasesPath, jsonFile);
        if (expectError)
            await Assert.ThrowsExceptionAsync<NestedTextException>(() => NestedTextSerializer.Deserialize(ntFile));
        else
        {
            var actual = await NestedTextSerializer.Deserialize(new FileInfo(ntFile));
            var json = actual?.ToJsonString(new(JsonSerializerOptions.Default) { WriteIndented = true });
            using var stream = File.OpenRead(jsonFile);
            var expected = await JsonSerializer.DeserializeAsync<JsonNode>(stream);
            Assert.AreEqual(expected?.ToJsonString(), actual?.ToJsonString());
        }
    }

    static IEnumerable<object[]> GetTestsInput(string testCasesPath)
    {
        foreach (var ntFile in Directory.EnumerateFiles(testCasesPath, "load_in.nt", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(ntFile)!;

            if (ReferenceEquals(testCasesPath, OfficialTestCasesPath))
            {
                var testCaseName = dir.Substring(testCasesPath.Length).Split('\\')[0];
                switch (testCaseName)
                {
                    case "dict_17":
                    case "dict_23":
                    case "dict_25":
                    case "dict_26":
                    case "dict_28":
                        continue;
                }
                if (Regex.IsMatch(testCaseName, @"(?:^holistic|(?:^|_)inline)_"))
                    continue;
            }

            var jsonFile = Path.Combine(dir, "load_out.json");
            var expectError = false;
            if (!File.Exists(jsonFile))
            {
                jsonFile = Path.Combine(dir, "load_err.json");
                if (!File.Exists(jsonFile))
                    continue;
                expectError = true;
            }
            yield return [ntFile.Substring(testCasesPath.Length), jsonFile.Substring(testCasesPath.Length), expectError, testCasesPath];
        }
    }
}