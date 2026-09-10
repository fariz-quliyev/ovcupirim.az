using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Success_carries_the_value()
    {
        var result = Result<string>.Success("elan");

        Assert.True(result.Succeeded);
        Assert.Equal("elan", result.Value);
        Assert.Equal(ResultError.None, result.Error);
    }

    [Fact]
    public void NotFound_reports_the_matching_error()
    {
        var result = Result<string>.NotFound("Elan tapılmadı.");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(ResultError.NotFound, result.Error);
        Assert.Equal("Elan tapılmadı.", result.Message);
    }

    [Fact]
    public void Conflict_reports_the_matching_error()
    {
        var result = Result.Conflict("Bu nömrə artıq qeydiyyatdadır.");

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Conflict, result.Error);
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 24, 0)]
    [InlineData(24, 24, 1)]
    [InlineData(25, 24, 2)]
    [InlineData(100, 24, 5)]
    public void TotalPages_rounds_up(int total, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], 1, pageSize, total);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void Empty_has_no_items()
    {
        var result = PagedResult<string>.Empty(2, 24);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(2, result.Page);
    }
}
