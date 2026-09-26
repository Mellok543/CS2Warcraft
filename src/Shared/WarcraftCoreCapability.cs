using CounterStrikeSharp.API.Core.Capabilities;
using Warcraft.Api;

namespace Warcraft.Shared;

/// <summary>
/// Resolves the Core API for feature plugins. Compiled into each consumer
/// (linked source), so no module references another module's assembly.
/// </summary>
internal static class WarcraftCoreCapability
{
    private static readonly PluginCapability<IWarcraftApi> Capability =
        new(WarcraftCapabilityNames.CoreApi);

    /// <summary>
    /// Returns null when Warcraft.Core is not loaded. CounterStrikeSharp throws
    /// <see cref="KeyNotFoundException"/> for an unregistered capability.
    /// </summary>
    public static IWarcraftApi? TryGet()
    {
        try
        {
            return Capability.Get();
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public const string UnavailableMessage =
        "Warcraft.Core capability '{Capability}' is unavailable. Make sure Warcraft.Core loaded " +
        "successfully and Warcraft.Api.dll is deployed to addons/counterstrikesharp/shared/Warcraft.Api/.";
}
