using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

public class SymbolNameRequestTests
{
    [Theory]
    [InlineData("Save", "Save", "Save", false)]
    [InlineData("  UserService.Save  ", "UserService.Save", "Save", true)]
    [InlineData("global::App.Services.UserService.Save", "App.Services.UserService.Save", "Save", true)]
    [InlineData("App.Outer+Inner", "App.Outer.Inner", "Inner", true)]
    [InlineData("Repository<T>.Save(T)", "Repository.Save", "Save", true)]
    [InlineData("Dictionary<string, List<int>>", "Dictionary", "Dictionary", false)]
    [InlineData("Result`1", "Result", "Result", false)]
    public void Parse_SplitsQualifiedAndSimpleName(string input, string qualifiedName, string simpleName, bool isQualified)
    {
        var request = SymbolNameRequest.Parse(input);

        request.Original.Should().Be(input);
        request.QualifiedName.Should().Be(qualifiedName);
        request.SimpleName.Should().Be(simpleName);
        request.IsQualified.Should().Be(isQualified);
    }

    [Theory]
    [InlineData("Result", null)]
    [InlineData("Result<T>", 1)]
    [InlineData("App.Result<T>", 1)]
    [InlineData("Dictionary<,>", 2)]
    [InlineData("Dictionary<string, List<int>>", 2)]
    [InlineData("Result`1", 1)]
    [InlineData("Get<T>(T)", 1)]
    [InlineData("Repository<T>.Save(T)", null)]
    [InlineData("Outer<T>+Inner", null)]
    public void Parse_ReadsTypeArgumentCountOfLastSegmentOnly(string input, int? arity)
    {
        SymbolNameRequest.Parse(input).Arity.Should().Be(arity);
    }

    [Fact]
    public void Parse_WithoutParameterList_HasNoParameterTypes()
    {
        SymbolNameRequest.Parse("UserService.Save").ParameterTypes.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyParameterList_HasNoParameters()
    {
        SymbolNameRequest.Parse("Save( )").ParameterTypes.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ParameterList_KeepsOnlyTypes()
    {
        var request = SymbolNameRequest.Parse(
            "Save(ref int count, params string[] names, Dictionary<string, int> map = null, this global::App.User user, (int, string) pair, int? id)");

        request.ParameterTypes.Should().Equal(
            "int", "string[]", "Dictionary<string,int>", "App.User", "(int,string)", "int?");
    }

    [Fact]
    public void Parse_ParameterList_TypeNamedLikeAModifierIsKept()
    {
        // 'int' starts with the 'in' modifier and 'outcome' with 'out'
        SymbolNameRequest.Parse("M(int, outcome, @in value)").ParameterTypes.Should().Equal("int", "outcome", "@in");
    }
}
