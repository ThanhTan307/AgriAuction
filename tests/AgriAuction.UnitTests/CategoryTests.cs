using AgriAuction.Domain.Catalog;
using AgriAuction.Domain.Identity;
using Xunit;

namespace AgriAuction.UnitTests;

public sealed class CategoryTests
{
    [Fact]
    public void NormalizesCodeAndName()
    {
        var c = Category.Create("  fruit-01 ", " Trái cây ", true);
        Assert.Equal("FRUIT-01", c.Code);
        Assert.Equal("Trái cây", c.Name);
        Assert.True(c.Active);
        Assert.Equal(1, c.Version);
    }

    [Theory]
    [InlineData(null, "Tên", "code")]
    [InlineData("", "Tên", "code")]
    [InlineData("A B", "Tên", "code")]
    [InlineData("<script>", "Tên", "code")]
    [InlineData("A", " ", "name")]
    [InlineData("A", null, "name")]
    public void RejectsInvalidFields(string? code, string? name, string field)
        => Assert.Contains(field, Assert.Throws<RuleViolation>(() => Category.Create(code, name, true)).Errors.Keys);

    [Fact]
    public void EnforcesStorageBounds()
    {
        Assert.Throws<RuleViolation>(() => Category.Create(new string('A', 33), "Tên", true));
        Assert.Throws<RuleViolation>(() => Category.Create("A", new string('x', 121), true));
        Assert.Equal(32, Category.Create(new string('A', 32), new string('x', 120), true).Code.Length);
    }

    [Fact]
    public void InactivationKeepsIdentityAndIncrementsVersion()
    {
        var c = Category.Create("ONE", "Một", true);
        var id = c.Id;
        c.Update("one", "Tên sửa", false);
        Assert.Equal(id, c.Id);
        Assert.False(c.Active);
        Assert.Equal(2, c.Version);
        Assert.Equal("Tên sửa", c.Name);
    }

    [Fact]
    public void InvalidUpdateDoesNotPartiallyMutateEntity()
    {
        var c = Category.Create("ONE", "Một", true);
        Assert.Throws<RuleViolation>(() => c.Update("TWO", "", false));
        Assert.Equal("ONE", c.Code);
        Assert.True(c.Active);
        Assert.Equal(1, c.Version);
    }

    [Fact]
    public void RolesMatchTheThreeRequiredActors()
        => Assert.Equal(new[] { "Administrator", "Farmer", "Enterprise" }, Roles.All);
}
