using System.Net;
using System.Text.Json;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class BaseController : IController
{
    protected HttpResponse OkHtml(string html)
    {
        return HtmlResponse(HttpStatusCode.OK, html);
    }
    protected HttpResponse HtmlResponse(HttpStatusCode status, string html)
    {
        var response = new HttpResponse((int)status);
        response.SetContentType("text/html");
        response.SetBody(html);
        return response;
    }

    protected HttpResponse OkJson(object responseModel = null)
    {
        return JsonResponse(HttpStatusCode.OK, responseModel);
    }
    protected HttpResponse BadRequestJson(object responseModel = null)
    {
        return JsonResponse(HttpStatusCode.BadRequest, responseModel);
    }

    protected HttpResponse JsonResponse(HttpStatusCode status, object responseModel = null)
    {
        var response = new HttpResponse((int)status);
        response.SetContentType("application/json");

        var jsonResult = JsonSerializer.Serialize(responseModel);
        response.SetBody(jsonResult);

        return response;
    }
}
