using System.Text.Json;
using System.Text.Json.Nodes;

namespace NestedText.Tests;

[TestClass]
public class NestedTextTests
{
    [TestMethod]
    public async Task Test()
    {
        var filesFound = false;
        foreach (var file in Directory.EnumerateFiles(".", "*.nt"))
        {
            filesFound = true;
            var jsonNode = await NestedTextSerializer.Deserialize(file);
            Assert.IsNotNull(jsonNode);
            using var stream = File.OpenRead(Path.ChangeExtension(file, ".json"));
            var expected = await JsonSerializer.DeserializeAsync<JsonNode>(stream);
            Assert.IsNotNull(expected);
            Assert.AreEqual(expected.ToJsonString(), jsonNode.ToJsonString());
        }
        Assert.IsTrue(filesFound);
        var obj = await NestedTextSerializer.Deserialize("TestDocument.nt");
        var json = obj?.ToJsonString(new(JsonSerializerOptions.Default) { WriteIndented = true });
    }
}