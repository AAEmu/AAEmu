using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi;
internal class RouteMapper
{
    private static Dictionary<string, Func<HttpResponse>> _routes = new(StringComparer.CurrentCultureIgnoreCase);
    public static void RegisterRoute(string path, HttpMethod method, Func<HttpResponse> func)
    {
        _routes.Add(GetRouteKey(path, method), func);
    }

    public static Func<HttpResponse> GetRoute(string path, HttpMethod method)
    {
        if (_routes.TryGetValue(GetRouteKey(path, method), out var routeHandler))
        {
            return routeHandler;
        }

        return null;
    }

    private static string GetRouteKey(string path, HttpMethod method)
    {
        return string.Concat(method ?? HttpMethod.Get, ':', path);
    }

    // Find all classes that implement T and find the methods that have either WebApiGet or WebApiPost attributes
    // and register them as routes
    internal static void DiscoverRoutes<T>()
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(T).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

        foreach (var type in types)
        {
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                var getAttribute = method.GetCustomAttributes(typeof(WebApiGetAttribute), true);
                if (getAttribute.Length > 0)
                {
                    var get = getAttribute[0] as WebApiGetAttribute;
                    RegisterRoute(get.Path, HttpMethod.Get, () => (HttpResponse)method.Invoke(Activator.CreateInstance(type), null));
                }

                var postAttribute = method.GetCustomAttributes(typeof(WebApiPostAttribute), true);
                if (postAttribute.Length > 0)
                {
                    var post = postAttribute[0] as WebApiPostAttribute;
                    RegisterRoute(post.Path, HttpMethod.Post, () => (HttpResponse)method.Invoke(Activator.CreateInstance(type), null));
                }
            }
        }
    }
}
