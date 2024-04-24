using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NestedText.Tests;

[TestClass]
public class NestedTextTests
{
    const string TestCasesPath = @"official_tests\test_cases\";

    //[DeploymentItem]
    [DynamicData(nameof(GetInput), DynamicDataSourceType.Method)]
    [DataTestMethod]
    public async Task Test(string ntFile, string jsonFile, bool expectError)
    {
        ntFile = Path.Combine(TestCasesPath, ntFile);
        jsonFile = Path.Combine(TestCasesPath, jsonFile);
        if (expectError)
            await Assert.ThrowsExceptionAsync<NestedTextException>(() => NestedTextSerializer.Deserialize(ntFile));
        else
        {
            var jsonNode = await NestedTextSerializer.Deserialize(ntFile);
            using var stream = File.OpenRead(jsonFile);
            var expected = await JsonSerializer.DeserializeAsync<JsonNode>(stream);
            Assert.AreEqual(expected?.ToJsonString(), jsonNode?.ToJsonString());
        }
    }

    public static IEnumerable<object[]> GetInput()
    {
        foreach (var ntFile in Directory.EnumerateFiles(TestCasesPath, "load_in.nt", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(ntFile)!;
            var testCaseName = dir.Substring(TestCasesPath.Length).Split('\\')[0];
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

            var jsonFile = Path.Combine(dir, "load_out.json");
            var expectError = false;
            if (!File.Exists(jsonFile))
            {
                jsonFile = Path.Combine(dir, "load_err.json");
                if (!File.Exists(jsonFile))
                    continue;
                expectError = true;
            }
            yield return [ntFile.Substring(TestCasesPath.Length), jsonFile.Substring(TestCasesPath.Length), expectError];
        }
    }
}