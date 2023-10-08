using System.Net.Http;
using System.Reflection;

namespace AAEmu.Game.Services.WebApi;

public record RouteDefinition
{
    public string Key { get; }
    public string Path { get; }
    public HttpMethod Method { get; }
    public MethodInfo TargetMethod { get; }

    public RouteDefinition(string path, HttpMethod httpMethod, MethodInfo targetMethod)
    {
        Path = path;
        Method = httpMethod;
        TargetMethod = targetMethod;
        Key = GetRouteKey(path, httpMethod);
    }

    public static string GetRouteKey(string path, HttpMethod method)
    {
        return string.Concat(method ?? HttpMethod.Get, ':', path);
    }
}
