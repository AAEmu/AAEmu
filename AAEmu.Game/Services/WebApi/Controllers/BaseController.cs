using System.Collections.Specialized;
using System.Net;
using System.Web;
using NetCoreServer;
using Newtonsoft.Json;

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
        response.SetHeader("Content-Type", "text/html");
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
        response.SetHeader("Content-Type", "application/json");

        var jsonResult = JsonConvert.SerializeObject(responseModel);
        response.SetBody(jsonResult);

        return response;
    }

    public NameValueCollection ParseQueryString(string url)
    {
        var queryStartIndex = url.IndexOf('?');
        var queryString = queryStartIndex >= 0 ? url.Substring(queryStartIndex + 1) : string.Empty;

        return HttpUtility.ParseQueryString(queryString);
    }
}
