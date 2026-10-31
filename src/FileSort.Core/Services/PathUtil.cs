using FileSort.Core.Models;

namespace FileSort.Core.Services;

public static class PathUtil
{
    /// <summary>Virtual location that lists the drives ("This PC").</summary>
    public const string ThisPc = "::ThisPC";

    /// <summary>Returns the parent path, or null when the path is a root.</summary>
    public static string? GetParent(string path)
    {
        var p = path.TrimEnd('\\', '/');
        if (p.Length == 0) return null;                    // "/" root
        if (p.Length == 2 && p[1] == ':') return null;     // "C:" drive root
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        if (i < 0) return null;
        var parent = p[..i];
        if (parent.Length == 0) return "/";
        if (parent.Length == 2 && parent[1] == ':') parent += "\\";
        return parent;
    }

    public static IReadOnlyList<Breadcrumb> GetBreadcrumbs(string path)
    {
        var result = new List<Breadcrumb>();
        if (string.IsNullOrEmpty(path) || path == ThisPc) return result;

        if (path.Length >= 2 && path[1] == ':')
        {
            var current = path[..2] + "\\";
            result.Add(new Breadcrumb(path[..2], current));
            foreach (var seg in path[2..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = current.EndsWith('\\') ? current + seg : current + "\\" + seg;
                result.Add(new Breadcrumb(seg, current));
            }
        }
        else if (path.StartsWith('/'))
        {
            var current = "/";
            result.Add(new Breadcrumb("/", current));
            foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = current.EndsWith('/') ? current + seg : current + "/" + seg;
                result.Add(new Breadcrumb(seg, current));
            }
        }
        else
        {
            result.Add(new Breadcrumb(path, path)); // UNC or unknown form: single segment
        }
        return result;
    }
}
