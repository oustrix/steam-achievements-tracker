using SteamAchievements.Core.Local;

namespace SteamAchievements.Core.Tests.Local;

public class VdfParserTests
{
    [Fact]
    public void ParsesNestedSections()
    {
        var node = VdfParser.Parse("""
            "users"
            {
                "123"
                {
                    "AccountName"		"someone"
                }
            }
            """);

        Assert.Equal("someone", node["users"]["123"]["AccountName"].Value);
    }

    [Fact]
    public void ParsesEscapedQuotesInsideValues()
    {
        var node = VdfParser.Parse("""
            "root"
            {
                "PersonaName"		"Current \"Quoted\" User"
            }
            """);

        Assert.Equal("Current \"Quoted\" User", node["root"]["PersonaName"].Value);
    }

    [Fact]
    public void IgnoresCommentLines()
    {
        var node = VdfParser.Parse("""
            // leading comment
            "root"
            {
                "key"		"value"   // trailing comment
            }
            """);

        Assert.Equal("value", node["root"]["key"].Value);
    }

    [Fact]
    public void ReturnsEmptyNodeForMissingKey()
    {
        var node = VdfParser.Parse("\"root\"\n{\n}\n");

        Assert.Null(node["root"]["absent"].Value);
        Assert.Empty(node["root"]["absent"].Children);
    }

    [Fact]
    public void ThrowsOnUnbalancedBraces()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n"));
    }

    [Fact]
    public void EmptySingletonIsNotCastableToMutableDictionary()
    {
        var a = VdfParser.Parse("\"root\"\n{\n}\n");
        var absent = a["root"]["absent"];

        Assert.Throws<InvalidCastException>(() =>
            (Dictionary<string, VdfNode>)absent.Children
        );
    }

    [Fact]
    public void DoesNotPoisonEmptySingletonAcrossParses()
    {
        var a = VdfParser.Parse("\"root\"\n{\n}\n");
        _ = a["root"]["absent"];

        var b = VdfParser.Parse("\"other\"\n{\n}\n");

        Assert.Null(b["other"]["absent"]["poisoned"].Value);
    }

    [Fact]
    public void ThrowsOnUnterminatedQuotedString()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"key\"\t\"unterminated"));
    }

    [Fact]
    public void ThrowsOnTrailingGarbageAfterTopLevelSection()
    {
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"root\"\n{\n}\n/"));
    }
}
