//! The request / response model of the WebApi (`NetCoreServer.HttpRequest` / `HttpResponse`)
//! and the `BaseController` response helpers.

use super::json::JValue;

/// `NetCoreServer.HttpRequest` (the members the controllers read).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct HttpRequest {
    /// `Method` as sent (`GET`, `POST`, ...).
    pub method: String,
    /// `Url`: the request target (path and query string, not decoded).
    pub url: String,
    pub protocol: String,
    /// `Body` (UTF-8).
    pub body: String,
}

impl HttpRequest {
    /// `new HttpRequest(method, url, protocol)`.
    pub fn new(method: &str, url: &str, protocol: &str) -> Self {
        Self {
            method: method.to_string(),
            url: url.to_string(),
            protocol: protocol.to_string(),
            body: String::new(),
        }
    }

    pub fn with_body(mut self, body: impl Into<String>) -> Self {
        self.body = body.into();
        self
    }
}

/// `NetCoreServer.HttpResponse`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HttpResponse {
    pub status: u16,
    /// `StatusPhrase`.
    pub status_phrase: String,
    /// Headers in insertion order (`SetHeader`; `Content-Length` is added by the transport).
    pub headers: Vec<(String, String)>,
    pub body: String,
}

impl HttpResponse {
    /// `new HttpResponse(status)`.
    pub fn new(status: u16) -> Self {
        Self {
            status,
            status_phrase: status_phrase(status).to_string(),
            headers: Vec::new(),
            body: String::new(),
        }
    }

    /// `SetHeader(key, value)`.
    pub fn set_header(&mut self, key: &str, value: &str) {
        self.headers.push((key.to_string(), value.to_string()));
    }

    /// `SetContentType(type)`.
    pub fn set_content_type(&mut self, content_type: &str) {
        self.set_header("Content-Type", content_type);
    }

    /// `SetBody(body)`.
    pub fn set_body(&mut self, body: impl Into<String>) {
        self.body = body.into();
    }

    /// The `Content-Type` header, if set.
    pub fn content_type(&self) -> Option<&str> {
        self.headers
            .iter()
            .find(|(k, _)| k == "Content-Type")
            .map(|(_, v)| v.as_str())
    }
}

/// The status phrases NetCoreServer writes.
pub fn status_phrase(status: u16) -> &'static str {
    match status {
        100 => "Continue",
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        301 => "Moved Permanently",
        302 => "Found",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        503 => "Service Unavailable",
        _ => "Unknown",
    }
}

/// `BaseController.OkHtml(html)`.
pub fn ok_html(html: &str) -> HttpResponse {
    html_response(200, html)
}

/// `BaseController.HtmlResponse(status, html)`.
pub fn html_response(status: u16, html: &str) -> HttpResponse {
    let mut r = HttpResponse::new(status);
    r.set_header("Content-Type", "text/html");
    r.set_body(html);
    r
}

/// `BaseController.OkJson(model)`.
pub fn ok_json(model: &JValue) -> HttpResponse {
    json_response(200, model)
}

/// `BaseController.BadRequestJson(model)`.
pub fn bad_request_json(model: &JValue) -> HttpResponse {
    json_response(400, model)
}

/// `BaseController.JsonResponse(status, model)` (`JsonConvert.SerializeObject(model)`).
pub fn json_response(status: u16, model: &JValue) -> HttpResponse {
    let mut r = HttpResponse::new(status);
    r.set_header("Content-Type", "application/json");
    r.set_body(model.to_json());
    r
}

/// `new ErrorModel(message)` (`StackTrace` = null).
pub fn error_model(message: impl Into<String>) -> JValue {
    JValue::object([
        ("Message", JValue::Str(message.into())),
        ("StackTrace", JValue::Null),
    ])
}

/// `HttpUtility.ParseQueryString` result (`NameValueCollection`: case-insensitive keys, values
/// of repeated keys joined with `,`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct QueryString {
    /// (key, value) in order; `None` keys come from segments without `=`.
    pairs: Vec<(Option<String>, String)>,
}

impl QueryString {
    /// `ParseQueryString(url after '?')` of `BaseController.ParseQueryString(url)`.
    pub fn from_url(url: &str) -> Self {
        let query = url.split_once('?').map_or("", |(_, q)| q);
        Self::parse(query)
    }

    /// `HttpUtility.ParseQueryString(query)`.
    pub fn parse(query: &str) -> Self {
        let mut pairs = Vec::new();
        if query.is_empty() {
            return Self { pairs };
        }
        for seg in query.split('&') {
            match seg.split_once('=') {
                Some((k, v)) => pairs.push((Some(url_decode(k)), url_decode(v))),
                None => pairs.push((None, url_decode(seg))),
            }
        }
        Self { pairs }
    }

    /// `NameValueCollection.Get(name)` / `[name]`.
    pub fn get(&self, name: &str) -> Option<String> {
        let values: Vec<&str> = self
            .pairs
            .iter()
            .filter(|(k, _)| k.as_deref().is_some_and(|k| k.eq_ignore_ascii_case(name)))
            .map(|(_, v)| v.as_str())
            .collect();
        (!values.is_empty()).then(|| values.join(","))
    }
}

/// `HttpUtility.UrlDecode` (UTF-8, `+` = space, invalid escapes kept).
pub fn url_decode(s: &str) -> String {
    let bytes = s.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        match bytes[i] {
            b'+' => out.push(b' '),
            b'%' if i + 2 < bytes.len() => {
                let hex = std::str::from_utf8(&bytes[i + 1..i + 3]).ok();
                match hex.and_then(|h| u8::from_str_radix(h, 16).ok()) {
                    Some(b) => {
                        out.push(b);
                        i += 2;
                    }
                    None => out.push(b'%'),
                }
            }
            b => out.push(b),
        }
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn query_strings() {
        let q = QueryString::from_url("/api/character/list?AccountId=5&name=Bob%20B&x&name=Al+C");
        assert_eq!(q.get("accountid").as_deref(), Some("5"));
        assert_eq!(q.get("Name").as_deref(), Some("Bob B,Al C"));
        assert_eq!(q.get("x"), None);
        assert_eq!(QueryString::from_url("/status").get("a"), None);
        assert_eq!(url_decode("%41%zz%"), "A%zz%");
    }

    #[test]
    fn helpers() {
        let r = bad_request_json(&error_model("Command name is required"));
        assert_eq!(r.status, 400);
        assert_eq!(r.status_phrase, "Bad Request");
        assert_eq!(r.content_type(), Some("application/json"));
        assert_eq!(
            r.body,
            r#"{"Message":"Command name is required","StackTrace":null}"#
        );
        let r = ok_html("x");
        assert_eq!((r.status, r.content_type()), (200, Some("text/html")));
    }
}
