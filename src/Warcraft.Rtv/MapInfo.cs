namespace Warcraft.Rtv;

internal sealed record MapInfo(string Name, ulong? WorkshopId)
{
    public bool IsWorkshop => WorkshopId is > 0;
}
