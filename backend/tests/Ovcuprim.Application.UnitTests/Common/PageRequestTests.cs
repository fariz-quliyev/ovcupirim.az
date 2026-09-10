using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.UnitTests.Common;

public class PageRequestTests
{
    [Fact]
    public void Defaults_to_the_first_page()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Clamps_a_non_positive_page_to_one(int page)
    {
        var request = new PageRequest { Page = page };

        Assert.Equal(1, request.Page);
    }

    [Fact]
    public void Caps_the_page_size_so_a_client_cannot_request_the_whole_table()
    {
        var request = new PageRequest { PageSize = 5000 };

        Assert.Equal(PageRequest.MaxPageSize, request.PageSize);
    }

    [Fact]
    public void Falls_back_to_the_default_page_size_when_given_a_non_positive_value()
    {
        var request = new PageRequest { PageSize = 0 };

        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
    }

    [Fact]
    public void Skip_reflects_the_requested_page()
    {
        var request = new PageRequest { Page = 3, PageSize = 20 };

        Assert.Equal(40, request.Skip);
    }
}
