namespace Warcraft.Api;

public static class WarcraftVersion
{
    public static string Current { get; } =
        typeof(WarcraftVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
