using System.Collections.Generic;
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

        var jsonResult = JsonSerializer.Serialize(responseModel);
        response.SetBody(jsonResult);

        return response;
    }
    
    public Dictionary<string, string> ParseQueryParameters(string url)
    {
        var queryParams = new Dictionary<string, string>();
        var queryStartIndex = url.IndexOf('?');

        if (queryStartIndex != -1)
        {
            var query = url.Substring(queryStartIndex + 1);
            foreach (var pair in query.Split('&'))
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    queryParams[keyValue[0]] = keyValue[1];
                }
            }
        }

        return queryParams;
    }
}
