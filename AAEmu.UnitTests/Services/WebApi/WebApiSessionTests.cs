using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Services.WebApi.Controllers;
using NetCoreServer;
using Xunit;

#pragma warning disable CA1822 // Mark members as static

namespace AAEmu.UnitTests.Services.WebApi;
public class WebApiSessionTests
{

    [Fact]
    public Task OnReceivedRequest_WhenRouteNotFound_ShouldReturn404()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));
        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest("GET", "/not-found", "HTTP/1.1"));

        // Assert
        Assert.Equal(404, sut.ResultResponse.Status);
        Assert.Equal("Not Found", sut.ResultResponse.StatusPhrase);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("GET", "/world/1", "world")]
    [InlineData("GET", "/world/fdsf", "world")]
    [InlineData("GET", "/world/fdsf/any/193", "world")]
    [InlineData("POST", "/world/1", "world-post")]
    [InlineData("POST", "/world/e1", "world-post")]
    [InlineData("POST", "/world/rr/1", "world-post")]
    [InlineData("GET", "/test/1", "test")]
    [InlineData("POST", "/test/1", "test-post")]
    public Task OnReceivedRequest_WhenRouteFound_ShouldReturnHtml(string method, string path, string expectedHtmlContent)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest(method, path, "HTTP/1.1"));

        // Assert
        Assert.Equal(200, sut.ResultResponse.Status);
        Assert.Equal("OK", sut.ResultResponse.StatusPhrase);
        AssertContentType(sut.ResultResponse, "text/html");
        Assert.Equal(expectedHtmlContent, sut.ResultResponse.Body);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("POST", "/multipleMatches/resource/subresource", new[] { "resource", "subresource" })]
    [InlineData("POST", "/multipleMatches/players/search", new[] { "players", "search" })]
    public Task OnReceivedRequest_WhenRouteFoundWithRegex_ShouldReturnHtmlWithMatches(string method, string path, string[] expectedMatches)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest(method, path, "HTTP/1.1"));

        // Assert
        Assert.Equal(200, sut.ResultResponse.Status);
        Assert.Equal("OK", sut.ResultResponse.StatusPhrase);
        AssertContentType(sut.ResultResponse, "text/html");

        var expectedHtmlContent = "test-post";
        var groupIndex = 1;
        expectedHtmlContent += $"\ngroup: name(0) value({path})";
        foreach (var expectedMatch in expectedMatches)
        {
            expectedHtmlContent += $"\ngroup: name({groupIndex}) value({expectedMatch})";
            groupIndex++;
        }

        Assert.Equal(expectedHtmlContent, sut.ResultResponse.Body);

        return Task.CompletedTask;
    }


    private static void AssertContentType(HttpResponse response, string expectedContentType)
    {
        for (var i = 0; i < response.Headers; i++)
        {
            if (response.Header(i).Item1 == "Content-Type")
            {
                Assert.Equal(expectedContentType, response.Header(i).Item2);
                break;
            }
        }
    }

    public class WebApiServerFake : WebApiServer
    {
        public WebApiServerFake(IPAddress address, int port) : base(address, port)
        {
        }

        public void RegisterRouteTest(string path, HttpMethod httpMethod, MethodInfo targetMethod)
        {
            base.RouteMapper.RegisterRoute(path, httpMethod, targetMethod);
        }
    }
    public class WebApiSessionFake : WebApiSession
    {
        public HttpResponse ResultResponse { get; private set; }
        public WebApiSessionFake(WebApiServer server) : base(server)
        {
        }

        public void OnReceivedRequestTest(HttpRequest request)
        {
            base.OnReceivedRequest(request);
        }

        protected override void InternalSendResponseAsync(HttpResponse response)
        {
            ResultResponse = response;
        }
    }
    internal class MyRegexController : BaseController
    {
        [WebApiGet("/world/(.+)")]
        public HttpResponse GetCharacter(HttpRequest request)
        {
            return OkHtml("world");
        }

        [WebApiPost("/world/(.+)")]
        public HttpResponse GetCharacterPost(HttpRequest request)
        {
            return OkHtml("world-post");
        }

        [WebApiGet("/test/(.+)")]
        public HttpResponse GetCharacter(HttpRequest request, MatchCollection matches)
        {
            return OkHtml("test");
        }

        [WebApiPost("/test/(.+)")]
        public HttpResponse GetCharacterPost(HttpRequest request, MatchCollection matches)
        {
            return OkHtml("test-post");
        }

        [WebApiPost("/multipleMatches/([^/]+)/([^/]+)")]
        public HttpResponse GetComplexRegexPost(HttpRequest request, MatchCollection matches)
        {
            var html = "test-post";
            foreach (Match match in matches)
            {
                foreach (Group group in match.Groups)
                {
                    html += "\ngroup: name(" + group.Name + ") value(" + group.Value + ")";
                }
            }
            return OkHtml(html);
        }
    }
}

