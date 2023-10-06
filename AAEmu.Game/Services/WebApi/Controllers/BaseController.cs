using System.Net;
using System.Text.Json;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class BaseController : IController
{
    protected static HttpResponse OkHtml(string html)
    {
        return HtmlResponse(HttpStatusCode.OK, html);
    }
    protected static HttpResponse HtmlResponse(HttpStatusCode status, string html)
    {
        var response = new HttpResponse((int)status);
        response.SetContentType("text/html");
        response.SetBody(html);
        return response;
    }

    protected static HttpResponse OkJson(object responseModel = null)
    {
        return JsonResponse(HttpStatusCode.OK, responseModel);
    }
    protected static HttpResponse BadRequestJson(object responseModel = null)
    {
        return JsonResponse(HttpStatusCode.BadRequest, responseModel);
    }

    protected static HttpResponse JsonResponse(HttpStatusCode status, object responseModel = null)
    {
        var response = new HttpResponse((int)status);
        response.SetContentType("application/json");

        var jsonResult = JsonSerializer.Serialize(responseModel);
        response.SetBody(jsonResult);

        return response;
    }
}
