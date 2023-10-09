using System.Net.Http;
using System.Threading.Tasks;
using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Services.WebApi.Controllers;
using NetCoreServer;
using Xunit;

#pragma warning disable CA1822 // Mark members as static

namespace AAEmu.UnitTests.Services.WebApi;

public class RouteMapperTests
{

    [Fact]
    public Task GetRoute_WhenSimpleRoute_ShouldFindAndMatch()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute("/world/logged-characters", HttpMethod.Get);

        // Assert
        Assert.NotNull(route);
        Assert.NotNull(matches);
        Assert.Single(matches);
        Assert.Equal("/world/logged-characters", route.Path);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/world/logged-characters")]
    [InlineData("/world/LOGGED-CHARACTERS")]
    [InlineData("/WORLD/LOGGED-CHARACTERS")]
    [InlineData("/WORLD/LOGGED-charactERS")]
    [InlineData("/WOrLD/LOggED-charactERS")]
    public Task GetRoute_WhenSimpleRoute_CaseInsensitiveShouldFindAndMatch(string path)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute(path, HttpMethod.Get);

        // Assert
        Assert.NotNull(route);
        Assert.NotNull(matches);
        Assert.Single(matches);
        Assert.Equal("/world/logged-characters", route.Path, true);

        return Task.CompletedTask;
    }

    [Fact]
    public Task GetRoute_WhenRegexRoutes_ShouldFindAndMatch()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        // Act
        var (route, matches) = routeMapper.GetRoute("/world/logged-characters", HttpMethod.Get);

        // Assert
        Assert.NotNull(route);
        Assert.NotNull(matches);
        Assert.Single(matches);
        Assert.Equal("logged-characters", matches[0].Groups[1].Value);
        Assert.Equal("/world/logged-characters", matches[0].Groups[0].Value);

        return Task.CompletedTask;
    }

    [Fact]
    public Task GetRoute_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyController));

        // Act
        var (route, matches) = routeMapper.GetRoute("not-found", HttpMethod.Get);

        // Assert
        Assert.Null(route);
        Assert.Null(matches);

        return Task.CompletedTask;
    }
    internal sealed class MyController : BaseController
    {
        [WebApiGet("/world/logged-characters")]
        public HttpResponse GetCharacter(HttpRequest request)
        {
            return OkJson(new { id = 1, name = "test" });
        }
    }

    internal sealed class MyRegexController : BaseController
    {
        [WebApiGet("/world/(.+)")]
        public HttpResponse GetCharacter(HttpRequest request)
        {
            return OkJson(new { id = 1, name = "test" });
        }
    }
}


