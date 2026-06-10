using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
    private static readonly string _version = ReadVersion();

    private static string ReadVersion()
    {
        // Look for VERSION file relative to the app's content root
        var versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION");
        if (File.Exists(versionFile))
            return File.ReadAllText(versionFile).Trim();

        // Fallback: read from assembly informational version
        var asm = typeof(VersionController).Assembly;
        var attr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        if (attr.Length > 0)
            return ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion;

        return "unknown";
    }

    [HttpGet]
    public IActionResult Get() => Ok(new { version = _version });
}