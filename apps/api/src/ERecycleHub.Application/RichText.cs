using Ganss.Xss;

namespace ERecycleHub.Application;

/// <summary>Whitelist HTML sanitiser for rich text written by publishers and admins. Applied on every write.</summary>
public static class RichText
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.UnionWith(["http", "https", "mailto", "tel"]);
        s.AllowedAttributes.Remove("style");
        s.AllowedTags.Remove("img"); // images belong in the gallery, not inline (no tracking pixels / mixed content)
        s.AllowedAttributes.Add("dir");
        return s;
    }

    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var clean = Sanitizer.Sanitize(html).Trim();
        return clean.Length == 0 ? null : clean;
    }
}
