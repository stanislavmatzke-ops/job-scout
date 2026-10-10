namespace JobScout.Api.Auth;

public static class ReturnUrl
{
    /// <summary>Only paths on this site – "https://evil…", "//evil…" or "/\evil…" would make us an open redirect.</summary>
    public static string Sanitize(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
            ? url
            : "/";
}
