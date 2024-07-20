using System;

namespace AAEmu.Game.Services.WebApi;

/// <summary>
/// Use this attribute in a method controller to flag it as a POST request handler
/// </summary>

[AttributeUsage(AttributeTargets.Method)]
internal class WebApiPostAttribute : Attribute
{
    /// <summary>
    /// Current path for method
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebApiPostAttribute"/> class.
    /// </summary>
    /// <param name="path">Provide what is the expected path for method</param>
    public WebApiPostAttribute(string path)
    {
        Path = path;
    }
}
