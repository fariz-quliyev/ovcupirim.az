using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Catalog;

public class CategorySchemaTests
{
    [Fact]
    public async Task Schema_includes_inherited_and_own_attributes()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var result = await h.Categories.GetSchemaAsync("child");
        var keys = result.Value!.Attributes.Select(a => a.Key).ToList();

        Assert.True(result.Succeeded);
        Assert.Contains("brand_like", keys);      // inherited
        Assert.Contains("length", keys);          // own
        Assert.DoesNotContain("parent_only", keys); // AppliesToDescendants = false
        Assert.DoesNotContain("retired_attr", keys); // inactive
    }

    [Fact]
    public async Task Inherited_attributes_are_flagged_as_inherited()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var schema = (await h.Categories.GetSchemaAsync("child")).Value!;

        Assert.True(schema.Attributes.Single(a => a.Key == "brand_like").Inherited);
        Assert.False(schema.Attributes.Single(a => a.Key == "length").Inherited);
    }

    [Fact]
    public async Task A_child_definition_overrides_the_inherited_one_with_the_same_key()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var schema = (await h.Categories.GetSchemaAsync("child")).Value!;
        var size = schema.Attributes.Single(a => a.Key == "size");

        // The child's footwear-style option set replaces the parent's letter sizes entirely.
        Assert.False(size.Inherited);
        Assert.True(size.IsRequired);
        Assert.Equal(3, size.Options!.Count);
        Assert.Equal(["40", "41", "42"], size.Options.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task The_parent_keeps_its_own_definition_when_a_child_overrides_it()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var schema = (await h.Categories.GetSchemaAsync("parent")).Value!;
        var size = schema.Attributes.Single(a => a.Key == "size");

        Assert.Equal(2, size.Options!.Count);
        Assert.False(size.IsRequired);
    }

    [Fact]
    public async Task Inactive_options_are_excluded_from_the_schema()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var schema = (await h.Categories.GetSchemaAsync("child")).Value!;
        var features = schema.Attributes.Single(a => a.Key == "features");

        Assert.Equal(2, features.Options!.Count);
        Assert.DoesNotContain("retired", features.Options.Select(o => o.Value));
    }

    [Fact]
    public async Task Schema_reports_the_ancestor_path_and_leaf_state()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var child = (await h.Categories.GetSchemaAsync("child")).Value!;
        var parent = (await h.Categories.GetSchemaAsync("parent")).Value!;

        Assert.Equal(["parent"], child.Category.Path.Select(p => p.Slug).ToArray());
        Assert.True(child.Category.IsLeaf);
        Assert.Empty(parent.Category.Path);
        Assert.False(parent.Category.IsLeaf);
    }

    [Fact]
    public async Task Only_Select_and_MultiSelect_carry_options()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var schema = (await h.Categories.GetSchemaAsync("child")).Value!;

        Assert.NotNull(schema.Attributes.Single(a => a.Key == "size").Options);
        Assert.NotNull(schema.Attributes.Single(a => a.Key == "features").Options);
        Assert.Null(schema.Attributes.Single(a => a.Key == "length").Options);
        Assert.Null(schema.Attributes.Single(a => a.Key == "waterproof").Options);
    }

    [Fact]
    public async Task An_unknown_or_inactive_category_is_not_found()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var unknown = await h.Categories.GetSchemaAsync("yoxdur");
        Assert.Equal(ResultError.NotFound, unknown.Error);

        var child = await h.Db.Categories.FirstAsync(c => c.Slug == "child");
        child.IsActive = false;
        await h.Db.SaveChangesAsync();

        var inactive = await h.Categories.GetSchemaAsync("child");
        Assert.Equal(ResultError.NotFound, inactive.Error);
    }
}

public class RestrictionInheritanceTests
{
    [Theory]
    [InlineData(RestrictionStatus.Unrestricted, RestrictionStatus.Unrestricted, "Unrestricted", false)]
    [InlineData(RestrictionStatus.Unrestricted, RestrictionStatus.Restricted, "Restricted", true)]
    [InlineData(RestrictionStatus.Restricted, RestrictionStatus.Unrestricted, "Restricted", true)]
    [InlineData(RestrictionStatus.Unclassified, RestrictionStatus.Unrestricted, "Unclassified", true)]
    [InlineData(RestrictionStatus.Unclassified, RestrictionStatus.Restricted, "Restricted", true)]
    [InlineData(RestrictionStatus.Restricted, RestrictionStatus.Unclassified, "Restricted", true)]
    public async Task Effective_status_is_the_most_severe_across_the_chain(
        RestrictionStatus parentStatus,
        RestrictionStatus childStatus,
        string expected,
        bool requiresAge)
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var parent = await h.Db.Categories.FirstAsync(c => c.Slug == "parent");
        var child = await h.Db.Categories.FirstAsync(c => c.Slug == "child");
        parent.RestrictionStatus = parentStatus;
        child.RestrictionStatus = childStatus;
        await h.Db.SaveChangesAsync();

        var schema = (await h.Categories.GetSchemaAsync("child")).Value!;

        Assert.Equal(expected, schema.Category.RestrictionStatus);
        Assert.Equal(requiresAge, schema.Category.RequiresAgeConfirmation);
    }

    [Fact]
    public async Task A_child_can_never_relax_a_restricted_parent()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var parent = await h.Db.Categories.FirstAsync(c => c.Slug == "parent");
        parent.RestrictionStatus = RestrictionStatus.Restricted;
        await h.Db.SaveChangesAsync();

        var tree = await h.Categories.GetTreeAsync();
        var node = tree.Single(c => c.Slug == "parent");

        Assert.All(node.Children, c => Assert.Equal("Restricted", c.RestrictionStatus));
    }

    [Fact]
    public async Task Unclassified_requires_age_confirmation_without_being_called_restricted()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var parent = await h.Db.Categories.FirstAsync(c => c.Slug == "parent");
        parent.RestrictionStatus = RestrictionStatus.Unclassified;
        var child = await h.Db.Categories.FirstAsync(c => c.Slug == "child");
        child.RestrictionStatus = RestrictionStatus.Unclassified;
        await h.Db.SaveChangesAsync();

        var detail = (await h.Categories.GetBySlugAsync("child")).Value!;

        // Handled conservatively, but reported distinctly from Restricted.
        Assert.Equal("Unclassified", detail.RestrictionStatus);
        Assert.True(detail.RequiresAgeConfirmation);
    }
}

public class CategoryTreeTests
{
    [Fact]
    public async Task Tree_nests_children_under_their_parent()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var tree = await h.Categories.GetTreeAsync();

        Assert.Single(tree);
        Assert.Equal("parent", tree[0].Slug);
        Assert.Equal(2, tree[0].Children.Count);
        Assert.Equal(["child", "sibling"], tree[0].Children.Select(c => c.Slug).ToArray());
    }

    [Fact]
    public async Task Inactive_categories_are_excluded_from_the_tree()
    {
        using var h = new TaxonomyTestHarness();
        await h.BuildFixtureAsync();

        var sibling = await h.Db.Categories.FirstAsync(c => c.Slug == "sibling");
        sibling.IsActive = false;
        await h.Db.SaveChangesAsync();

        var tree = await h.Categories.GetTreeAsync();

        Assert.Single(tree[0].Children);
        Assert.Equal("child", tree[0].Children[0].Slug);
    }
}
