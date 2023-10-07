using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Services.WebApi;
public class RouteMapper
{
    private Dictionary<string, RouteDefinition> _routes = new(StringComparer.CurrentCultureIgnoreCase);
    public void RegisterRoute(string path, HttpMethod httpMethod, MethodInfo targetMethod)
    {
        var route = new RouteDefinition(path, httpMethod, targetMethod);
        _routes.Add(route.Key, route);
    }

    public (RouteDefinition, MatchCollection matches) GetRoute(string httpUrlPath, HttpMethod httpMethod)
    {
        var matchedRoutes = new Dictionary<RouteDefinition, MatchCollection>();

        foreach (var route in _routes.Values)
        {
            if (route.Method == httpMethod && Regex.IsMatch(httpUrlPath, route.Path, RegexOptions.IgnoreCase))
            {
                matchedRoutes.Add(route, Regex.Matches(httpUrlPath, route.Path, RegexOptions.IgnoreCase));
            }
        }
        if (matchedRoutes.Count == 1)
        {
            var matchedRoute = matchedRoutes.First();
            return (matchedRoute.Key, matchedRoute.Value);
        }

        if (matchedRoutes.Count > 1)
        {
            throw new NotSupportedException($"Multiple routes found for {httpMethod} {httpUrlPath}");
        }

        return (null, null);
    }

    // Find all classes that implement T and find the methods that have either WebApiGet or WebApiPost attributes
    // and register them as routes
    internal void DiscoverRoutes<T>()
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(T).IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

        foreach (var type in types)
        {
            DiscoverRoutesFromType(type);
        }
    }

    internal void DiscoverRoutesFromType(Type controllerType)
    {
        var methods = controllerType.GetMethods();
        foreach (var method in methods)
        {
            var getAttribute = method.GetCustomAttributes(typeof(WebApiGetAttribute), true);
            if (getAttribute.Length > 0)
            {
                var get = getAttribute[0] as WebApiGetAttribute;
                RegisterRoute(get.Path, HttpMethod.Get, method);
            }

            var postAttribute = method.GetCustomAttributes(typeof(WebApiPostAttribute), true);
            if (postAttribute.Length > 0)
            {
                var post = postAttribute[0] as WebApiPostAttribute;
                RegisterRoute(post.Path, HttpMethod.Post, method);
            }
        }
    }
}
