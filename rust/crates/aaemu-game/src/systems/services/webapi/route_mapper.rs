//! `Services/WebApi/RouteMapper`, `RouteDefinition`, `WebApiGet/PostAttribute`, `IController`.
//!
//! A route is a .NET-style regular expression matched case-insensitively and **unanchored**
//! against the request URL (path + query string), exactly as `Regex.IsMatch(url, path,
//! IgnoreCase)`. More than one matching route is an error (`NotSupportedException`).
//! Controllers register their routes explicitly ([`Controller::routes`]) instead of being
//! discovered through reflection.

use std::collections::HashMap;
use std::sync::Arc;

use futures::future::BoxFuture;
use regex::{Regex, RegexBuilder};

use super::http::{HttpRequest, HttpResponse};
use crate::server::GameServer;

/// One `Group` of a `Match` (`Name`, `Value`; an unmatched group has an empty value).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Group {
    pub name: String,
    pub value: String,
    pub success: bool,
}

/// One `Match` of a `MatchCollection`: `Groups[0]` is the whole match, then the numbered
/// groups, then the named ones (the .NET group order).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Match {
    pub groups: Vec<Group>,
}

impl Match {
    /// `match.Groups[i].Value` ("" when out of range, like an unmatched .NET group).
    pub fn group(&self, i: usize) -> &str {
        self.groups.get(i).map_or("", |g| g.value.as_str())
    }
}

/// `MatchCollection`.
pub type MatchCollection = Vec<Match>;

/// What a controller method receives (`HttpRequest`, `MatchCollection`) plus the server the
/// C# reaches through its singletons (`None` in unit tests).
pub struct ControllerCtx {
    pub server: Option<Arc<GameServer>>,
    pub request: HttpRequest,
    pub matches: MatchCollection,
}

impl ControllerCtx {
    /// The server, or the error the C# singletons would raise without one.
    pub fn server(&self) -> anyhow::Result<&Arc<GameServer>> {
        self.server
            .as_ref()
            .ok_or_else(|| anyhow::anyhow!("Object reference not set to an instance of an object."))
    }
}

/// A controller method (`MethodInfo` + `Activator.CreateInstance(DeclaringType)`). An `Err`
/// is an exception thrown by the method (500 page).
pub type Handler =
    Arc<dyn Fn(ControllerCtx) -> BoxFuture<'static, anyhow::Result<HttpResponse>> + Send + Sync>;

/// Wraps an async controller method.
pub fn handler<F, Fut>(f: F) -> Handler
where
    F: Fn(ControllerCtx) -> Fut + Send + Sync + 'static,
    Fut: std::future::Future<Output = anyhow::Result<HttpResponse>> + Send + 'static,
{
    Arc::new(move |ctx| Box::pin(f(ctx)))
}

/// `HttpMethod` (`Get` / `Post`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Method {
    Get,
    Post,
}

impl Method {
    pub fn as_str(self) -> &'static str {
        match self {
            Method::Get => "GET",
            Method::Post => "POST",
        }
    }

    /// `new HttpMethod(request.Method)` compared with `==` (ordinal, ignoring case): `None`
    /// for methods no route can have.
    pub fn parse(s: &str) -> Option<Self> {
        if s.eq_ignore_ascii_case("GET") {
            Some(Method::Get)
        } else if s.eq_ignore_ascii_case("POST") {
            Some(Method::Post)
        } else {
            None
        }
    }
}

/// `[WebApiGet(path)]` / `[WebApiPost(path)]` on a controller method (`WebApiGetAttribute`,
/// `WebApiPostAttribute`).
pub struct RouteSpec {
    pub method: Method,
    pub path: &'static str,
    /// The C# method name (diagnostics).
    pub name: &'static str,
    pub handler: Handler,
}

/// `IController`: a type whose methods are routes.
pub trait Controller {
    /// The `[WebApiGet]` / `[WebApiPost]` methods of the controller.
    fn routes() -> Vec<RouteSpec>;
}

/// `RouteDefinition(Path, Method, TargetMethod)`.
#[derive(Clone)]
pub struct RouteDefinition {
    pub path: String,
    pub method: Method,
    pub target_name: String,
    pub target: Handler,
    regex: Regex,
}

impl std::fmt::Debug for RouteDefinition {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("RouteDefinition")
            .field("path", &self.path)
            .field("method", &self.method)
            .field("target", &self.target_name)
            .finish()
    }
}

impl RouteDefinition {
    /// `Key` (`GetRouteKey(path, method)`).
    pub fn key(&self) -> String {
        Self::route_key(&self.path, self.method)
    }

    /// `GetRouteKey(path, method)`: `"{method}:{path}"`.
    pub fn route_key(path: &str, method: Method) -> String {
        format!("{}:{}", method.as_str(), path)
    }
}

/// `RouteMapper` failures.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum RouteError {
    /// `Dictionary.Add` of an existing key (`ArgumentException`).
    #[error("An item with the same key has already been added. Key: {0}")]
    Duplicate(String),
    /// An invalid route pattern (`RegexParseException`).
    #[error("Invalid pattern '{0}': {1}")]
    Pattern(String, String),
    /// `NotSupportedException`.
    #[error("Multiple routes found for {0} {1}")]
    Multiple(String, String),
}

/// `RouteMapper`.
#[derive(Default, Clone)]
pub struct RouteMapper {
    /// `_routes` (case-insensitive keys) in insertion order.
    routes: Vec<RouteDefinition>,
    keys: HashMap<String, usize>,
}

impl std::fmt::Debug for RouteMapper {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_list().entries(&self.routes).finish()
    }
}

impl RouteMapper {
    pub fn new() -> Self {
        Self::default()
    }

    /// `RegisterRoute(path, method, targetMethod)`.
    pub fn register_route(
        &mut self,
        path: &str,
        method: Method,
        target_name: &str,
        target: Handler,
    ) -> Result<(), RouteError> {
        let key = RouteDefinition::route_key(path, method).to_lowercase();
        if self.keys.contains_key(&key) {
            return Err(RouteError::Duplicate(RouteDefinition::route_key(
                path, method,
            )));
        }
        let regex = RegexBuilder::new(path)
            .case_insensitive(true)
            .build()
            .map_err(|e| RouteError::Pattern(path.to_string(), e.to_string()))?;
        self.keys.insert(key, self.routes.len());
        self.routes.push(RouteDefinition {
            path: path.to_string(),
            method,
            target_name: target_name.to_string(),
            target,
            regex,
        });
        Ok(())
    }

    /// `DiscoverRoutesFromType(controllerType)`.
    pub fn discover_routes_from_type<C: Controller>(&mut self) -> Result<(), RouteError> {
        for r in C::routes() {
            self.register_route(r.path, r.method, r.name, r.handler)?;
        }
        Ok(())
    }

    /// `DiscoverRoutes<IController>()`: every controller of the game server.
    pub fn discover_routes(&mut self) -> Result<(), RouteError> {
        use super::controllers::*;
        self.discover_routes_from_type::<AuctionController>()?;
        self.discover_routes_from_type::<CharacterController>()?;
        self.discover_routes_from_type::<CommandController>()?;
        self.discover_routes_from_type::<ExpeditionController>()?;
        self.discover_routes_from_type::<MailController>()?;
        self.discover_routes_from_type::<StatusController>()?;
        self.discover_routes_from_type::<WorldController>()?;
        Ok(())
    }

    /// `GetRoute(httpUrlPath, httpMethod)`: the single route whose pattern matches the URL, with
    /// all its matches; `Ok(None)` when nothing matches.
    pub fn get_route(
        &self,
        url: &str,
        method: &str,
    ) -> Result<Option<(&RouteDefinition, MatchCollection)>, RouteError> {
        let parsed = Method::parse(method);
        let mut found: Vec<(&RouteDefinition, MatchCollection)> = Vec::new();
        for route in &self.routes {
            if Some(route.method) == parsed && route.regex.is_match(url) {
                found.push((route, matches(&route.regex, url)));
            }
        }
        match found.len() {
            0 => Ok(None),
            1 => Ok(found.pop()),
            _ => Err(RouteError::Multiple(method.to_string(), url.to_string())),
        }
    }

    pub fn len(&self) -> usize {
        self.routes.len()
    }

    pub fn is_empty(&self) -> bool {
        self.routes.is_empty()
    }

    pub fn routes(&self) -> &[RouteDefinition] {
        &self.routes
    }
}

/// `Regex.Matches(input, pattern)` with the .NET group order (numbered groups first, named
/// groups after).
fn matches(regex: &Regex, input: &str) -> MatchCollection {
    let names: Vec<Option<&str>> = regex.capture_names().collect();
    // .NET numbering: unnamed groups 1..k in order, then the named groups.
    let mut order: Vec<(usize, String)> = Vec::new();
    let mut n = 0;
    for (i, name) in names.iter().enumerate().skip(1) {
        if name.is_none() {
            n += 1;
            order.push((i, n.to_string()));
        }
    }
    for (i, name) in names.iter().enumerate().skip(1) {
        if let Some(name) = name {
            order.push((i, (*name).to_string()));
        }
    }
    regex
        .captures_iter(input)
        .map(|caps| {
            let mut groups = vec![Group {
                name: "0".into(),
                value: caps.get(0).map_or("", |m| m.as_str()).to_string(),
                success: true,
            }];
            for (i, name) in &order {
                let m = caps.get(*i);
                groups.push(Group {
                    name: name.clone(),
                    value: m.map_or("", |m| m.as_str()).to_string(),
                    success: m.is_some(),
                });
            }
            Match { groups }
        })
        .collect()
}

#[cfg(test)]
mod tests {
    //! Port of `AAEmu.UnitTests/Services/WebApi/RouteMapperTests.cs`.
    use super::*;
    use crate::systems::services::webapi::http::ok_json;
    use crate::systems::services::webapi::json::JValue;

    struct MyController;

    impl Controller for MyController {
        fn routes() -> Vec<RouteSpec> {
            vec![RouteSpec {
                method: Method::Get,
                path: "/world/logged-characters",
                name: "GetCharacter",
                handler: handler(|_ctx| async {
                    Ok(ok_json(&JValue::object([
                        ("id", JValue::from(1)),
                        ("name", JValue::from("test")),
                    ])))
                }),
            }]
        }
    }

    struct MyRegexController;

    impl Controller for MyRegexController {
        fn routes() -> Vec<RouteSpec> {
            vec![RouteSpec {
                method: Method::Get,
                path: "/world/(.+)",
                name: "GetCharacter",
                handler: handler(|_ctx| async {
                    Ok(ok_json(&JValue::object([
                        ("id", JValue::from(1)),
                        ("name", JValue::from("test")),
                    ])))
                }),
            }]
        }
    }

    #[test]
    fn get_route_when_simple_route_should_find_and_match() {
        let mut mapper = RouteMapper::new();
        mapper.discover_routes_from_type::<MyController>().unwrap();
        let (route, matches) = mapper
            .get_route("/world/logged-characters", "GET")
            .unwrap()
            .unwrap();
        assert_eq!(matches.len(), 1);
        assert_eq!(route.path, "/world/logged-characters");
    }

    #[test]
    fn get_route_when_simple_route_case_insensitive_should_find_and_match() {
        for path in [
            "/world/logged-characters",
            "/world/LOGGED-CHARACTERS",
            "/WORLD/LOGGED-CHARACTERS",
            "/WORLD/LOGGED-charactERS",
            "/WOrLD/LOggED-charactERS",
        ] {
            let mut mapper = RouteMapper::new();
            mapper.discover_routes_from_type::<MyController>().unwrap();
            let (route, matches) = mapper.get_route(path, "GET").unwrap().unwrap();
            assert_eq!(matches.len(), 1);
            assert!(route.path.eq_ignore_ascii_case("/world/logged-characters"));
        }
    }

    #[test]
    fn get_route_when_regex_routes_should_find_and_match() {
        let mut mapper = RouteMapper::new();
        mapper
            .discover_routes_from_type::<MyRegexController>()
            .unwrap();
        let (_, matches) = mapper
            .get_route("/world/logged-characters", "GET")
            .unwrap()
            .unwrap();
        assert_eq!(matches.len(), 1);
        assert_eq!(matches[0].group(1), "logged-characters");
        assert_eq!(matches[0].group(0), "/world/logged-characters");
    }

    #[test]
    fn get_route_when_not_found_should_return_null() {
        let mut mapper = RouteMapper::new();
        mapper.discover_routes_from_type::<MyController>().unwrap();
        assert!(mapper.get_route("not-found", "GET").unwrap().is_none());
        // Same path, other method.
        assert!(mapper
            .get_route("/world/logged-characters", "POST")
            .unwrap()
            .is_none());
    }

    #[test]
    fn duplicate_and_ambiguous_routes() {
        let mut mapper = RouteMapper::new();
        mapper.discover_routes_from_type::<MyController>().unwrap();
        assert!(matches!(
            mapper.discover_routes_from_type::<MyController>(),
            Err(RouteError::Duplicate(_))
        ));
        mapper
            .discover_routes_from_type::<MyRegexController>()
            .unwrap();
        assert_eq!(
            mapper
                .get_route("/world/logged-characters", "get")
                .unwrap_err(),
            RouteError::Multiple("get".into(), "/world/logged-characters".into())
        );
    }

    #[test]
    fn game_server_routes_are_unique_and_unambiguous() {
        let mut mapper = RouteMapper::new();
        mapper.discover_routes().unwrap();
        assert_eq!(mapper.len(), 12);
        for (url, method) in [
            ("/status", "GET"),
            ("/api/world/logged-characters", "GET"),
            ("/api/expedition/list", "GET"),
            ("/api/character/list?Name=x", "GET"),
            ("/api/commands/help", "POST"),
            ("/api/mail/send", "POST"),
            ("/api/mail/list", "POST"),
            ("/api/mail/delete", "POST"),
            ("/api/auction/add", "POST"),
            ("/api/auction/list", "GET"),
            ("/api/auction/search?ItemId=1", "GET"),
            ("/api/auction/generate", "POST"),
        ] {
            assert!(
                mapper.get_route(url, method).unwrap().is_some(),
                "{method} {url}"
            );
        }
    }

    #[test]
    fn named_groups_follow_numbered_groups() {
        let re = RegexBuilder::new("(?P<x>a)(b)").build().unwrap();
        let m = matches(&re, "ab");
        let names: Vec<_> = m[0].groups.iter().map(|g| g.name.as_str()).collect();
        assert_eq!(names, ["0", "1", "x"]);
        assert_eq!(m[0].group(1), "b");
    }
}
