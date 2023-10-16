using System;

namespace AAEmu.Game.Services.WebApi;

/// <summary>
/// Use this attribute in a method controller to flag it as a GET request handler
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal class WebApiGetAttribute : Attribute
{
    /// <summary>
    /// Current path for method
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebApiGetAttribute"/> class.
    /// </summary>
    /// <param name="path">Provide what is the expected path for method</param>
    public WebApiGetAttribute(string path)
    {
        Path = path;
    }
}
