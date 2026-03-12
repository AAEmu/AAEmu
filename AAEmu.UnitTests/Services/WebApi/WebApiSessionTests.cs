using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Services.WebApi.Controllers;
using NetCoreServer;

namespace AAEmu.UnitTests.Services.WebApi;
public class WebApiSessionTests
{

    [Test]
    public async Task OnReceivedRequest_WhenRouteNotFound_ShouldReturn404()
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));
        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest("GET", "/not-found", "HTTP/1.1"));

        // Assert
        await Assert.That(sut.ResultResponse.Status).IsEqualTo(404);
        await Assert.That(sut.ResultResponse.StatusPhrase).IsEqualTo("Not Found");
    }

    [Test]
    [Arguments("GET", "/world/1", "world")]
    [Arguments("GET", "/world/fdsf", "world")]
    [Arguments("GET", "/world/fdsf/any/193", "world")]
    [Arguments("POST", "/world/1", "world-post")]
    [Arguments("POST", "/world/e1", "world-post")]
    [Arguments("POST", "/world/rr/1", "world-post")]
    [Arguments("GET", "/test/1", "test")]
    [Arguments("POST", "/test/1", "test-post")]
    public async Task OnReceivedRequest_WhenRouteFound_ShouldReturnHtml(string method, string path, string expectedHtmlContent)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest(method, path, "HTTP/1.1"));

        // Assert
        await Assert.That(sut.ResultResponse.Status).IsEqualTo(200);
        await Assert.That(sut.ResultResponse.StatusPhrase).IsEqualTo("OK");
        await AssertContentType(sut.ResultResponse, "text/html");
        await Assert.That(sut.ResultResponse.Body).IsEqualTo(expectedHtmlContent);
    }

    [Test]
    [Arguments("POST", "/multipleMatches/resource/subresource", new[] { "resource", "subresource" })]
    [Arguments("POST", "/multipleMatches/players/search", new[] { "players", "search" })]
    public async Task OnReceivedRequest_WhenRouteFoundWithRegex_ShouldReturnHtmlWithMatches(string method, string path, string[] expectedMatches)
    {
        // Arrange
        var routeMapper = new RouteMapper();
        routeMapper.DiscoverRoutesFromType(typeof(MyRegexController));

        using var server = new WebApiServer(IPAddress.Any, 10000, routeMapper);
        using var sut = new WebApiSessionFake(server);

        // Act
        sut.OnReceivedRequestTest(new HttpRequest(method, path, "HTTP/1.1"));

        // Assert
        await Assert.That(sut.ResultResponse.Status).IsEqualTo(200);
        await Assert.That(sut.ResultResponse.StatusPhrase).IsEqualTo("OK");
        await AssertContentType(sut.ResultResponse, "text/html");

        var expectedHtmlContent = "test-post";
        var groupIndex = 1;
        expectedHtmlContent += $"\ngroup: name(0) value({path})";
        foreach (var expectedMatch in expectedMatches)
        {
            expectedHtmlContent += $"\ngroup: name({groupIndex}) value({expectedMatch})";
            groupIndex++;
        }

        await Assert.That(sut.ResultResponse.Body).IsEqualTo(expectedHtmlContent);
    }

    private static async Task AssertContentType(HttpResponse response, string expectedContentType)
    {
        for (var i = 0; i < response.Headers; i++)
        {
            if (response.Header(i).Item1 == "Content-Type")
            {
                await Assert.That(response.Header(i).Item2).IsEqualTo(expectedContentType);
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
    internal sealed class MyRegexController : BaseController
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
