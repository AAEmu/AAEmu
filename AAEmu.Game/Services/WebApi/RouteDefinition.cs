using System.Reflection;

namespace AAEmu.Game.Services.WebApi;

public record RouteDefinition(string Path, HttpMethod Method, MethodInfo TargetMethod)
{
    public string Key { get; } = GetRouteKey(Path, Method);

    public static string GetRouteKey(string path, HttpMethod method)
    {
        return string.Concat(method ?? HttpMethod.Get, ':', path);
    }
}
