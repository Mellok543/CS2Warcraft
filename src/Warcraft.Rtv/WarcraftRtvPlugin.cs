using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Warcraft.Api;
using Warcraft.Api.Menu;
using Warcraft.Api.Modules;
using Warcraft.Shared;

namespace Warcraft.Rtv;

[MinimumApiVersion(80)]
public sealed class WarcraftRtvPlugin : BasePlugin
{
    private const string ModuleId = "warcraft.rtv";
    private const string RootEntryId = "warcraft.rtv.open";
    private const string RootPageId = "warcraft.rtv.page";
    private const string VotePageId = "warcraft.rtv.vote";
    private const string NominatePageId = "warcraft.rtv.nominate";

    private const int VoteDurationSeconds = 15;
    private const int VoteMapCount = 5;
    private const double RtvRequiredFraction = 0.60;
    private const float EndVoteLeadSeconds = 300f;

    private readonly HashSet<ulong> _rtvVotes = [];
    private readonly Dictionary<string, int> _mapVotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> _mapVoters = [];
    private readonly Dictionary<ulong, string> _nominations = [];

    private IWarcraftApi? _api;
    private List<MapInfo> _currentVoteMaps = [];
    private bool _voteActive;
    private bool _endVoteStarted;
    private bool _changeImmediatelyAfterVote;
    private DateTimeOffset _voteEndsAt;
    private MapInfo? _scheduledNextMap;

    public override string ModuleName => "Warcraft.Rtv";
    public override string ModuleVersion => WarcraftVersion.Current;
    public override string ModuleAuthor => "Mellok543";
    public override string ModuleDescription => "Rock-the-vote, nominations and automatic next-map voting.";

    public override void Load(bool hotReload)
    {
        EnsureMapList();

        var loadedMaps = LoadMaps();
        Logger.LogInformation(
            "RTV: loaded {Count} maps from {Path}.",
            loadedMaps.Count,
            ResolveMapListPath());

        AddCommand("css_rtv", "Vote for an early map change", OnRtvCommand);
        AddCommand("css_nominate", "Nominate a map", OnNominateCommand);
        AddCommand("css_timeleft", "Show remaining map time", OnTimeleftCommand);
        AddCommand("css_reloadmaps", "Reload and validate Warcraft RTV map list", OnReloadMapsCommand);

        AddTimer(
            10.0f,
            CheckMapTime,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        RegisterListener<Listeners.OnMapStart>(_ => ResetForMap());
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        _api = WarcraftCoreCapability.TryGet();
        if (_api is null)
        {
            Logger.LogError(WarcraftCoreCapability.UnavailableMessage, WarcraftCapabilityNames.CoreApi);
            return;
        }

        _api.Menu.RegisterPage(new MenuPageRegistration(RootPageId, ModuleId, BuildRootPage));
        _api.Menu.RegisterPage(new MenuPageRegistration(VotePageId, ModuleId, BuildVotePage));
        _api.Menu.RegisterPage(new MenuPageRegistration(NominatePageId, ModuleId, BuildNominatePage));
        _api.Menu.Register(new MenuEntryRegistration(
            RootEntryId,
            ModuleId,
            "root",
            "Карты / RTV",
            50,
            steamId => _api.Menu.RequestOpenPage(RootPageId, steamId)));

        _api.Modules.Register(new ModuleRegistration(
            ModuleId,
            ModuleVersion,
            "RTV and map voting"));
    }

    public override void Unload(bool hotReload)
    {
        if (_api is not null)
        {
            _api.Menu.Unregister(RootEntryId, ModuleId);
            _api.Menu.UnregisterPage(RootPageId, ModuleId);
            _api.Menu.UnregisterPage(VotePageId, ModuleId);
            _api.Menu.UnregisterPage(NominatePageId, ModuleId);
            _api.Modules.Unregister(ModuleId);
        }

        _api = null;
        ResetForMap();
    }

    private void OnRtvCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (IsHuman(player))
            RegisterRtv(player!);
    }

    private void OnNominateCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        if (_voteActive)
        {
            Print(player!, "Во время голосования номинации недоступны.");
            return;
        }

        _api?.Menu.RequestOpenPage(NominatePageId, player!.SteamID);
    }

    private void OnTimeleftCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        Print(player!, TimeLeftText());
    }

    private void OnReloadMapsCommand(CCSPlayerController? player, CommandInfo command)
    {
        var path = ResolveMapListPath();
        var maps = LoadMaps();

        Logger.LogInformation(
            "RTV: map list reloaded: {Count} maps from {Path}.",
            maps.Count,
            path);

        var message = $"RTV: загружено карт: {maps.Count}. Файл: {path}";
        if (IsHuman(player))
            Print(player!, message);
        else
            command.ReplyToCommand(message);
    }

    private MenuPageDescriptor? BuildRootPage(ulong steamId)
    {
        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (player is not { IsValid: true })
            return null;

        var humans = HumanPlayers().ToArray();
        var required = RequiredRtvVotes(humans.Length);
        var nextMap = _scheduledNextMap is null ? "не выбрана" : _scheduledNextMap.Name;

        var items = new List<MenuPageItemDescriptor>
        {
            new(
                $"RTV — {_rtvVotes.Count}/{required}",
                id =>
                {
                    var voter = Utilities.GetPlayerFromSteamId(id);
                    if (IsHuman(voter))
                        RegisterRtv(voter!);
                },
                !_voteActive && _scheduledNextMap is null,
                _voteActive ? "Голосование за карту уже идёт"
                    : _scheduledNextMap is not null ? $"Следующая карта уже выбрана: {_scheduledNextMap.Name}"
                    : null),
            new(
                "Номинировать карту",
                id => _api?.Menu.RequestOpenPage(NominatePageId, id),
                !_voteActive,
                _voteActive ? "Во время голосования номинации недоступны" : null),
            new(
                $"До смены: {TimeLeftText(shortForm: true)}",
                _ => { },
                false,
                "Оставшееся время текущей карты"),
            new(
                $"Следующая карта: {nextMap}",
                _ => { },
                false,
                _scheduledNextMap is null ? "Будет определена голосованием" : "Победитель планового голосования")
        };

        return new MenuPageDescriptor(
            RootPageId,
            "КАРТЫ / RTV",
            Server.MapName.ToUpperInvariant(),
            items,
            "root");
    }

    private MenuPageDescriptor? BuildVotePage(ulong steamId)
    {
        if (!_voteActive)
            return new MenuPageDescriptor(
                VotePageId,
                "ГОЛОСОВАНИЕ",
                "ГОЛОСОВАНИЕ ЗАВЕРШЕНО",
                [new MenuPageItemDescriptor("← Назад", id => _api?.Menu.RequestOpenPage(RootPageId, id))],
                RootPageId);

        var alreadyVoted = _mapVoters.Contains(steamId);
        var secondsLeft = Math.Max(0, (int)Math.Ceiling((_voteEndsAt - DateTimeOffset.UtcNow).TotalSeconds));

        var items = _currentVoteMaps
            .Select(map =>
            {
                var votes = _mapVotes.GetValueOrDefault(map.Name);
                return new MenuPageItemDescriptor(
                    $"{map.Name} — {votes} голос(ов)",
                    id => RegisterMapVote(id, map),
                    !alreadyVoted,
                    alreadyVoted ? "Вы уже проголосовали" : null);
            })
            .ToArray();

        return new MenuPageDescriptor(
            VotePageId,
            "ВЫБОР КАРТЫ",
            $"{secondsLeft} СЕК. • ГОЛОСОВ: {_mapVoters.Count}",
            items,
            RootPageId);
    }

    private MenuPageDescriptor? BuildNominatePage(ulong steamId)
    {
        var maps = LoadMaps()
            .Where(map => !string.Equals(map.Name, Server.MapName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(map => map.Name)
            .ToArray();

        if (maps.Length == 0)
            return new MenuPageDescriptor(
                NominatePageId,
                "НОМИНАЦИЯ",
                "СПИСОК КАРТ ПУСТ",
                [new MenuPageItemDescriptor("Нет доступных карт", _ => { }, false, "Проверьте defaults/maplist.txt")],
                RootPageId);

        var selected = _nominations.GetValueOrDefault(steamId);
        var items = maps.Select(map =>
            new MenuPageItemDescriptor(
                string.Equals(selected, map.Name, StringComparison.OrdinalIgnoreCase)
                    ? $"{map.Name}  ✓"
                    : map.Name,
                id =>
                {
                    _nominations[id] = map.Name;
                    var player = Utilities.GetPlayerFromSteamId(id);
                    if (IsHuman(player))
                        Print(player!, $"Вы номинировали {map.Name}.");
                    _api?.Menu.RequestOpenPage(NominatePageId, id);
                }))
            .ToArray();

        return new MenuPageDescriptor(
            NominatePageId,
            "НОМИНАЦИЯ КАРТЫ",
            selected is null ? "ВЫБЕРИТЕ КАРТУ" : $"ВЫБРАНО: {selected.ToUpperInvariant()}",
            items,
            RootPageId);
    }

    private void RegisterRtv(CCSPlayerController player)
    {
        if (_voteActive)
        {
            Print(player, "Голосование за карту уже идёт.");
            _api?.Menu.RequestOpenPage(VotePageId, player.SteamID);
            return;
        }

        if (_scheduledNextMap is not null)
        {
            Print(player, $"Следующая карта уже выбрана: {_scheduledNextMap.Name}.");
            return;
        }

        var humans = HumanPlayers().ToArray();
        var required = RequiredRtvVotes(humans.Length);

        if (!_rtvVotes.Add(player.SteamID))
        {
            Print(player, "Вы уже проголосовали за RTV.");
            return;
        }

        PrintAll($"RTV: {_rtvVotes.Count}/{required}.");

        if (_rtvVotes.Count >= required)
            StartMapVote(changeImmediately: true);
    }

    private void StartMapVote(bool changeImmediately)
    {
        if (_voteActive)
            return;

        var maps = LoadMaps()
            .Where(map => !string.Equals(map.Name, Server.MapName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (maps.Count == 0)
        {
            Logger.LogWarning("RTV: maplist.txt is empty or has no maps other than the current map.");
            PrintAll("Не удалось запустить голосование: список карт пуст.");
            _endVoteStarted = false;
            return;
        }

        _rtvVotes.Clear();
        _mapVotes.Clear();
        _mapVoters.Clear();

        var nominations = _nominations.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nominatedMaps = maps
            .Where(map => nominations.Contains(map.Name, StringComparer.OrdinalIgnoreCase))
            .Take(VoteMapCount)
            .ToList();

        var randomMaps = maps
            .Where(map => nominatedMaps.All(candidate =>
                !candidate.Name.Equals(map.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Max(0, VoteMapCount - nominatedMaps.Count));

        _currentVoteMaps = nominatedMaps
            .Concat(randomMaps)
            .Take(VoteMapCount)
            .ToList();

        _nominations.Clear();

        foreach (var map in _currentVoteMaps)
            _mapVotes[map.Name] = 0;

        _changeImmediatelyAfterVote = changeImmediately;
        _voteActive = true;
        _voteEndsAt = DateTimeOffset.UtcNow.AddSeconds(VoteDurationSeconds);

        foreach (var player in HumanPlayers())
            _api?.Menu.RequestOpenPage(VotePageId, player.SteamID);

        _api?.Menu.RequestNotification(new MenuNotificationRequest(
            HumanPlayers().FirstOrDefault()?.SteamID ?? 0,
            "ГОЛОСОВАНИЕ",
            changeImmediately ? "RTV ПРИНЯТ" : "СЛЕДУЮЩАЯ КАРТА",
            $"Выберите карту. Время: {VoteDurationSeconds} сек.",
            MenuNotificationStyle.Rare,
            4f));

        PrintAll(
            changeImmediately
                ? $"RTV принят. Выберите новую карту за {VoteDurationSeconds} сек."
                : $"До конца карты 5 минут. Выберите следующую карту за {VoteDurationSeconds} сек.");

        AddTimer(
            VoteDurationSeconds,
            EndMapVote,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void RegisterMapVote(ulong steamId, MapInfo map)
    {
        if (!_voteActive || !_currentVoteMaps.Any(x => x.Name.Equals(map.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        if (!_mapVoters.Add(steamId))
        {
            var duplicate = Utilities.GetPlayerFromSteamId(steamId);
            if (IsHuman(duplicate))
                Print(duplicate!, "Вы уже проголосовали.");
            return;
        }

        _mapVotes[map.Name] = _mapVotes.GetValueOrDefault(map.Name) + 1;

        var player = Utilities.GetPlayerFromSteamId(steamId);
        if (IsHuman(player))
            Print(player!, $"Ваш голос: {map.Name}.");

        foreach (var human in HumanPlayers())
        {
            if (!_mapVoters.Contains(human.SteamID))
                _api?.Menu.RequestOpenPage(VotePageId, human.SteamID);
        }
    }

    private void EndMapVote()
    {
        if (!_voteActive)
            return;

        _voteActive = false;

        if (_currentVoteMaps.Count == 0)
            return;

        var highestVotes = _mapVotes.Values.DefaultIfEmpty(0).Max();
        var candidates = highestVotes > 0
            ? _currentVoteMaps.Where(map => _mapVotes.GetValueOrDefault(map.Name) == highestVotes).ToArray()
            : _currentVoteMaps.ToArray();

        var winner = candidates[Random.Shared.Next(candidates.Length)];
        PrintAll(
            highestVotes > 0
                ? $"Победила карта {winner.Name} — {highestVotes} голос(ов)."
                : $"Голосов нет. Случайно выбрана карта {winner.Name}.");

        if (_changeImmediatelyAfterVote)
        {
            AddTimer(
                5.0f,
                () => ChangeMap(winner),
                TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }

        _scheduledNextMap = winner;
        ScheduleEndOfMapChange(winner);
    }

    private void CheckMapTime()
    {
        if (_voteActive || _endVoteStarted || _scheduledNextMap is not null)
            return;

        if (!TryGetSecondsLeft(out var secondsLeft))
            return;

        if (secondsLeft > EndVoteLeadSeconds)
            return;

        _endVoteStarted = true;
        StartMapVote(changeImmediately: false);
    }

    private void ScheduleEndOfMapChange(MapInfo map)
    {
        if (!TryGetSecondsLeft(out var secondsLeft))
        {
            Logger.LogWarning("RTV: could not schedule next map because mp_timelimit is unavailable.");
            return;
        }

        AddTimer(
            Math.Max(1f, secondsLeft + 1f),
            () => ChangeMap(map),
            TimerFlags.STOP_ON_MAPCHANGE);

        PrintAll($"Следующая карта: {map.Name}.");
    }

    private void ChangeMap(MapInfo map)
    {
        Logger.LogInformation(
            "RTV: changing map to {Map} (WorkshopId={WorkshopId}).",
            map.Name,
            map.WorkshopId);

        if (map.IsWorkshop)
            Server.ExecuteCommand($"ds_workshop_changelevel {map.Name}");
        else
            Server.ExecuteCommand($"changelevel {map.Name}");
    }

    private bool TryGetSecondsLeft(out float secondsLeft)
    {
        secondsLeft = 0;

        var timeLimit = ConVar.Find("mp_timelimit");
        if (timeLimit is null)
            return false;

        var minutes = timeLimit.GetPrimitiveValue<float>();
        if (minutes <= 0)
            return false;

        secondsLeft = Math.Max(0f, minutes * 60f - Server.CurrentTime);
        return true;
    }

    private string TimeLeftText(bool shortForm = false)
    {
        if (!TryGetSecondsLeft(out var secondsLeft))
            return shortForm ? "без лимита" : "Лимит времени карты отключён.";

        var totalSeconds = (int)Math.Ceiling(secondsLeft);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        return shortForm
            ? $"{minutes}:{seconds:00}"
            : $"До смены карты: {minutes}:{seconds:00}.";
    }

    private List<MapInfo> LoadMaps()
    {
        var path = ResolveMapListPath();
        if (!File.Exists(path))
        {
            Logger.LogWarning("RTV: maplist.txt not found at {Path}", path);
            return [];
        }

        var result = new List<MapInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            var name = parts[0];

            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                continue;

            ulong? workshopId = null;
            if (parts.Length == 2)
            {
                if (!ulong.TryParse(parts[1], out var parsed) || parsed == 0)
                {
                    Logger.LogWarning("RTV: invalid Workshop id in line: {Line}", rawLine);
                    continue;
                }

                workshopId = parsed;
            }

            result.Add(new MapInfo(name, workshopId));
        }

        return result;
    }

    private void EnsureMapList()
    {
        var target = MapListPath();
        if (File.Exists(target))
            return;

        var source = PackagedMapListPath();
        if (!File.Exists(source))
        {
            Logger.LogWarning(
                "RTV: neither runtime nor packaged map list exists. Runtime path: {Target}; packaged path: {Source}",
                target,
                source);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target);
        Logger.LogInformation("RTV: created runtime map list at {Path}.", target);
    }

    private string ResolveMapListPath()
    {
        var runtime = MapListPath();
        var packaged = PackagedMapListPath();

        if (!File.Exists(runtime))
            return packaged;
        if (!File.Exists(packaged))
            return runtime;

        // A freshly deployed plugin may contain a newer packaged map list while an
        // old auto-created runtime file is still present. Prefer whichever file was
        // modified most recently; manual runtime edits therefore still win.
        return File.GetLastWriteTimeUtc(packaged) > File.GetLastWriteTimeUtc(runtime)
            ? packaged
            : runtime;
    }

    private string PackagedMapListPath()
        => Path.Combine(ModuleDirectory, "defaults", "maplist.txt");

    private static string MapListPath()
        => Path.Combine(Server.GameDirectory, "configs", "warcraft", "maplist.txt");

    private void OnClientDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is null || player.SteamID == 0)
            return;

        _rtvVotes.Remove(player.SteamID);
        _nominations.Remove(player.SteamID);
        _mapVoters.Remove(player.SteamID);
    }

    private void ResetForMap()
    {
        _rtvVotes.Clear();
        _mapVotes.Clear();
        _mapVoters.Clear();
        _nominations.Clear();
        _currentVoteMaps.Clear();
        _voteActive = false;
        _endVoteStarted = false;
        _changeImmediatelyAfterVote = false;
        _scheduledNextMap = null;
        _voteEndsAt = default;
    }

    private static int RequiredRtvVotes(int humanCount)
        => Math.Max(1, (int)Math.Ceiling(humanCount * RtvRequiredFraction));

    private static IEnumerable<CCSPlayerController> HumanPlayers()
        => Utilities.GetPlayers().Where(IsHuman)!;

    private static bool IsHuman(CCSPlayerController? player)
        => player is { IsValid: true, IsBot: false } && player.SteamID != 0;

    private static void Print(CCSPlayerController player, string message)
        => player.PrintToChat($" [Warcraft] {message}");

    private static void PrintAll(string message)
        => Server.PrintToChatAll($" [Warcraft] {message}");
}
