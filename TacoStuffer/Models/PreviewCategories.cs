namespace TacoStuffer.Models;

public static class PreviewCategories
{
    public static bool Visible(string path, ISet<string> hidden)
    {
        while (true)
        {
            if (hidden.Contains(path)) return false;
            int dot = path.LastIndexOf('.');
            if (dot < 0) break;
            path = path[..dot];
        }
        return true;
    }
}
