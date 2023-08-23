using BattleBitAPI;
using BattleBitAPI.Common;
using BattleBitAPI.Server;
using BBRAPIModules;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SwissAdminTools;

#region Configuration
public class SATConfiguration : ModuleConfiguration
{
    public string ConnectionString { get; set; } = "server=localhost;user=battlebit;password=battlebit;database=battlebit";
}
#endregion

#region SAT Module
public class Sat : BattleBitModule
{
    public SATConfiguration Configuration { get; set; }

    public override void OnModulesLoaded()
    {
        Storage = new SwissAdminToolsMysql(this.Configuration.ConnectionString);
    }

    public SwissAdminToolsMysql Storage { get; set; }

    public override Task OnConnected()
    {
        Console.WriteLine($"Gameserver connected! {Server.GameIP}:{Server.GamePort} {Server.ServerName}");
        Server.ForceStartGame();
        Server.RoundSettings.SecondsLeft = 3600;
        Server.RoundSettings.TeamATickets = 100;
        Server.RoundSettings.TeamBTickets = 100;
        Server.ServerSettings.PlayerCollision = true;
        return Task.CompletedTask;
    }

    public override Task OnPlayerConnected(RunnerPlayer player)
    {
        var blockDetails = AdminTools.IsBlocked(player.SteamID, AdminTools.BlockType.Ban);
        if (blockDetails.isBlocked)
        {
            player.Kick(blockDetails.reason);
            return Task.CompletedTask;
        }

        player.Modifications.CanDeploy = true;
        Console.WriteLine($"Player {player.Name} - {player.SteamID} connected with IP {player.IP}");
        return Task.CompletedTask;
    }

    public override Task OnPlayerJoiningToServer(ulong steamId, PlayerJoiningArguments args)
    {
        try
        {
            Storage.StorePlayer(steamId, args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while storing player: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public override Task<bool> OnPlayerTypedMessage(RunnerPlayer player, ChatChannel channel, string msg)
    {
        Storage.StoreChatLog(player.SteamID, msg);
        var res = AdminTools.ProcessChat(msg, player, Server);
        if (!res) return Task.FromResult(false);

        var blockResult = AdminTools.IsBlocked(player.SteamID, AdminTools.BlockType.Gag);
        if (!blockResult.isBlocked) return Task.FromResult(true);
        player.WarnPlayer($"You are currently gagged: {blockResult.reason}");
        return Task.FromResult(false);
    }

    public override Task<OnPlayerSpawnArguments?> OnPlayerSpawning(RunnerPlayer player, OnPlayerSpawnArguments request)
    {
        if (AdminTools.IsWeaponRestricted(request.Loadout.PrimaryWeapon.Tool))
        {
            player.WarnPlayer($"You are not allowed to use {request.Loadout.PrimaryWeapon.Tool.Name}!");
            return Task.FromResult<OnPlayerSpawnArguments?>(null);
        }

        if (AdminTools.IsWeaponRestricted(request.Loadout.SecondaryWeapon.Tool))
        {
            player.WarnPlayer($"You are not allowed to use {request.Loadout.SecondaryWeapon.Tool.Name}!");
            return Task.FromResult<OnPlayerSpawnArguments?>(null);
        }

        return Task.FromResult<OnPlayerSpawnArguments?>(request);
    }
}
#endregion

#region Admin Tools
public static class AdminTools
{
    public enum BlockType
    {
        Ban,
        Gag,
        Mute
    }

    private static readonly Dictionary<string, Func<Arguments, RunnerPlayer, RunnerServer, bool>> Commands = new()
    {
        { "say", SayCmd },
        { "clear", ClearCmd },
        { "kick", KickCmd },
        { "slay", SlayCmd },
        { "ban", BanCmd },
        { "gag", GagCmd },
        { "saveloc", SaveLocCmd },
        { "tele", TeleportCmd },
        { "restrict", RestrictCmd },
        { "rcon", RconCmd }
        // { "gravity", GravityCmd },
        // { "speed", SpeedCmd },
        // { "", Cmd }
    };

    private static readonly Dictionary<ulong, (long timestamp, string reason)> BannedPlayers = new();
    private static readonly Dictionary<ulong, (long timestamp, string reason)> GaggedPlayers = new();
    private static readonly Dictionary<ulong, (long timestamp, string reason)> MutedPlayers = new();
    private static readonly List<Weapon> BlockedWeapons = new();

    private static readonly Dictionary<ulong, Vector3> TeleportCoords = new();


    public static (bool isBlocked, string reason) IsBlocked(ulong steamId, BlockType blockType)
    {
        var blockDict = blockType switch
        {
            BlockType.Ban => BannedPlayers,
            BlockType.Gag => GaggedPlayers,
            BlockType.Mute => MutedPlayers,
            _ => throw new ArgumentOutOfRangeException(nameof(blockType), blockType, null)
        };

        if (!blockDict.TryGetValue(steamId, out var block))
            return (false, "");

        var unixTime = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        var formattedLength = LengthFromSeconds(block.timestamp - unixTime);

        if (unixTime <= block.timestamp)
            return (true, $"{block.reason} (length: {formattedLength})");

        blockDict.Remove(steamId);
        return (false, "");
    }

    public static bool IsWeaponRestricted(Weapon weapon)
    {
        return BlockedWeapons.Contains(weapon);
    }


    public static bool ProcessChat(string message, RunnerPlayer sender, RunnerServer server)
    {
        if (message.StartsWith("@")) return SayCmd(new Arguments(message.TrimStart('@')), sender, server);
        if (!message.StartsWith("!")) return true;

        message = message.TrimStart('!');
        var split = message.Split(new[] { ' ' }, 2);

        if (split.Length == 0) return true;

        var command = split[0];
        if (!Commands.ContainsKey(command)) return true;

        if (split.Length == 1)
            try
            {
                return Commands[command](new Arguments(""), sender, server);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }

        var args = split[1];
        try
        {
            return Commands[command](new Arguments(args), sender, server);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }

    private static bool RestrictCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        if (args.Count() != 2)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for restrict command (<weapon> <true/false>)");
            return false;
        }

        var weapon = args.GetString();
        var restrict = args.GetBool();

        if (weapon == null || restrict == null)
        {
            server.MessageToPlayer(sender, "Invalid arguments for restrict command (<weapon> <true/false>)");
            return false;
        }

        Weapons.TryFind(weapon, out var wep);

        if (wep == null)
        {
            server.MessageToPlayer(sender, "Invalid weapon name");
            return false;
        }

        if (restrict.Value)
            BlockedWeapons.Add(wep);
        else
            BlockedWeapons.Remove(wep);

        return false;
    }

    private static bool SaveLocCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        var loc = sender.Position;
        TeleportCoords[sender.SteamID] = loc;
        return false;
    }

    private static bool TeleportCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        TeleportCoords.TryGetValue(sender.SteamID, out var loc);
        try
        {
            var possibleTarget = args.GetString();
            if (possibleTarget == null)
            {
                Console.Error.WriteLine("could not parse teleport target");
                return false;
            }
            var targets = FindTarget(possibleTarget, sender, server).ToList();
            targets.ForEach(t =>
            {
                server.UILogOnServer($"{t.Name} was teleported", 3f);
                t.Teleport(loc);
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return false;
    }

    private static bool GagCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        //!gag <target> <length> <optional reason>
        if (args.Count() < 2)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for gag command (<target> <length> <reason>)");
            return false;
        }
        var possibleTarget = args.GetString();
        if (possibleTarget == null)
        {
            Console.Error.WriteLine("could not parse gag target");
            return false;
        }
        var targets = FindTarget(possibleTarget, sender, server);

        var mins = args.GetInt();
        if (mins == null)
        {
            server.MessageToPlayer(sender, "Invalid gag length (pass a number of minutes)");
            return false;
        }

        var lengthMinutes = mins.Value;

        //convert minutes to human readable string (if minutes are hours or days, that is used instead)
        var lengthMessage = LengthFromSeconds(lengthMinutes * 60);
        var reason = args.Count() > 2 ? args.GetRemainingString() : "Gagged by admin";
        if (reason == null)
        {
            Console.Error.WriteLine("could not parse gag reason");
            return false;
        }
        try
        {
            targets.ToList().ForEach(t =>
            {
                var gagExpiry = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds() + lengthMinutes * 60;
                if (lengthMinutes <= 0) gagExpiry = DateTime.MaxValue.Ticks;

                GaggedPlayers.Add(t.SteamID, new ValueTuple<long, string>(gagExpiry, reason));
                server.UILogOnServer($"{t.Name} was gagged: {reason} ({lengthMessage})", 4f);
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return false;
    }

    private static bool SayCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        if (args.Count() < 1)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for say command (<message>)");
            return false;
        }
        var msg = args.GetRemainingString();
        if (msg == null)
        {
            Console.Error.WriteLine("could not parse say message");
            return false;
        }
        server.SayToAllChat($"{RichText.Red}[{RichText.Bold("ADMIN")}]: {RichText.Magenta}{RichText.Italic(msg)}");
        return false;
    }

    private static bool ClearCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        server.SayToAllChat($"\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n{RichText.Size(".", 0)}");
        return false;
    }

    private static bool KickCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        if (args.Count() < 1)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for kick command (<target> <reason>)");
            return false;
        }

        var possibleTarget = args.GetString();
        if (possibleTarget == null)
        {
            Console.Error.WriteLine("could not parse kick target");
            return false;
        }
        var targets = FindTarget(possibleTarget, sender, server);
        var reason = args.Count() > 1 ? args.GetRemainingString() : "Kicked by admin";
        try
        {
            targets.ToList().ForEach(t =>
            {
                server.Kick(t, reason);
                server.UILogOnServer($"{t.Name} was kicked from the server: {reason}", 3f);
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return false;
    }

    private static bool SlayCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        try
        {
            var possibleTarget = args.GetString();
            if (possibleTarget == null)
            {
                Console.Error.WriteLine("could not parse slay target");
                return false;
            }
            var targets = FindTarget(possibleTarget, sender, server).ToList();
            targets.ForEach(t =>
            {
                server.UILogOnServer($"{t.Name} was slayed", 3f);
                server.Kill(t);
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return false;
    }

    private static bool RconCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        if (sender.SteamID != 76561197997290818) return false;

        if (args.Count() < 1)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for rcon command (<command>)");
            return false;
        }

        server.ExecuteCommand(args.GetRemainingString());
        return true;
    }

    private static bool BanCmd(Arguments args, RunnerPlayer sender, RunnerServer server)
    {
        //!ban <target> <length> <optional reason>
        if (args.Count() < 2)
        {
            server.MessageToPlayer(sender, "Invalid number of arguments for ban command (<target> <length> <reason>)");
            return false;
        }

        var possibleTarget = args.GetString();
        if (possibleTarget == null)
        {
            Console.Error.WriteLine("could not parse ban target");
            return false;
        }
        var targets = FindTarget(possibleTarget, sender, server);
        var mins = args.GetInt();
        if (mins == null)
        {
            server.MessageToPlayer(sender, "Invalid ban length (pass a number of minutes)");
            return false;
        }

        var lengthMinutes = mins.Value;

        var reason = args.Count() > 2 ? args.GetRemainingString() : "Banned by admin";
        if (reason == null)
        {
            Console.Error.WriteLine("could not parse ban reason");
            return false;
        }

        //convert minutes to human readable string (if minutes are hours or days, that is used instead)
        var lengthMessage = LengthFromSeconds(lengthMinutes * 60);

        try
        {
            targets.ToList().ForEach(t =>
            {
                var banExpiry = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds() + lengthMinutes * 60;
                if (lengthMinutes <= 0) banExpiry = DateTime.MaxValue.Ticks;

                BannedPlayers.Add(t.SteamID, new ValueTuple<long, string>(banExpiry, reason));
                server.Kick(t, reason + $" {lengthMessage}");
                server.UILogOnServer($"{t.Name} was banned from the server: {reason} ({lengthMessage})", 4f);
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return false;
    }

    private static string LengthFromSeconds(long lengthSeconds)
    {
        var lengthMinutes = lengthSeconds / 60f;
        return lengthMinutes switch
        {
            <= 0 => "permanently",
            < 1 => $"{lengthSeconds:0} seconds",
            < 60 => $"{lengthMinutes:0.0} minutes",
            < 1440 => $"{lengthMinutes / 60f:0.0} hours",
            _ => $"{lengthMinutes / 1440f:0.0} days"
        };
    }

    /// FindTarget returns a list of steamIds based on the target string
    /// if the target filter is a partial name or steamId then it will only allow returning one player
    /// if special filters are used then it may return multiple players
    private static IEnumerable<RunnerPlayer> FindTarget(string target, RunnerPlayer sender, RunnerServer server)
    {
        var players = server.AllPlayers.ToList();
        var nameMatchCount = 0;
        var idMatchCount = 0;
        var matches = new List<RunnerPlayer>();
        switch (target.ToLower())
        {
            // if string contains @all then return everyone
            case "@all":
                return players.Select(p => p).ToArray();
            // if string contains @!me then return everyone except the sender
            case "@!me":
                return players.Where(p => p.SteamID != sender.SteamID).Select(p => p).ToArray();
            // if string contains @me then return the sender
            case "@me":
                return new[] { sender };
            // if string contains @usa then return all those on team A
            case "@usa":
                return players.Where(p => p.Team == Team.TeamA).Select(p => p).ToArray();
            // if string contains @rus then return all those on team B
            case "@rus":
                return players.Where(p => p.Team == Team.TeamB).Select(p => p).ToArray();
            // if string contains @dead then return all those currently dead using p.IsAlive
            case "@dead":
                return players.Where(p => !p.IsAlive).Select(p => p).ToArray();
            // if string contains @alive then return all those currently alive using p.IsAlive
            case "@alive":
                return players.Where(p => p.IsAlive).Select(p => p).ToArray();
            // target Assault, Medic , Support, Engineer, Recon, Leader
            case "@assault":
                return players.Where(p => p.Role == GameRole.Assault).Select(p => p).ToArray();
            case "@medic":
                return players.Where(p => p.Role == GameRole.Medic).Select(p => p).ToArray();
            case "@support":
                return players.Where(p => p.Role == GameRole.Support).Select(p => p).ToArray();
            case "@engineer":
                return players.Where(p => p.Role == GameRole.Engineer).Select(p => p).ToArray();
            case "@recon":
                return players.Where(p => p.Role == GameRole.Recon).Select(p => p).ToArray();
            case "@leader":
                return players.Where(p => p.Role == GameRole.Leader).Select(p => p).ToArray();
        }

        //if string starts in # then return the player with a partially matching steamID instead of name
        if (target.StartsWith("#"))
        {
            var steamId = target.TrimStart('#');
            players.ForEach(p =>
            {
                if (!p.SteamID.ToString().Contains(steamId)) return;
                idMatchCount++;
                matches.Add(p);
            });
        }

        if (idMatchCount > 0)
        {
            if (idMatchCount > 1) throw new Exception("multiple players match that partial steamID");
            return matches;
        }

        foreach (var player in players.Where(player => player.Name.ToLower().Contains(target.ToLower())))
        {
            nameMatchCount++;
            matches.Add(player);
        }

        if (nameMatchCount > 1) throw new Exception("multiple players match that name");
        return matches;
    }

    private class Arguments
    {
        private readonly string[] mArgs;
        private int mIndex;

        public Arguments(string input)
        {
            // Match non-whitespace or a sequence between double quotes
            // thank you Copilot
            var matches = Regex.Matches(input, @"[^\s""']+|""([^""]*)""|'([^']*)'");

            mArgs = new string[matches.Count];
            for (var i = 0; i < matches.Count; i++) mArgs[i] = matches[i].Value.Trim('"'); // Removing the quotes around the arguments
        }

        public int Count()
        {
            return mArgs.Length;
        }

        public string? GetString()
        {
            if (mIndex >= mArgs.Length) return null;
            return mArgs[mIndex++];
        }

        public string? GetRemainingString()
        {
            if (mIndex >= mArgs.Length) return null;
            var result = string.Join(" ", mArgs[mIndex..]);
            mIndex = mArgs.Length;
            return result;
        }

        public int? GetInt()
        {
            if (mIndex >= mArgs.Length) return null;
            //try parse
            if (!int.TryParse(mArgs[mIndex++], out var result)) return null;
            return result;
        }

        public float GetFloat()
        {
            if (mIndex >= mArgs.Length) return 0;
            return float.Parse(mArgs[mIndex++]);
        }

        public bool? GetBool()
        {
            if (mIndex >= mArgs.Length) return null;
            //try parse
            if (!bool.TryParse(mArgs[mIndex++], out var result)) return null;
            return result;
        }
    }
}
#endregion

#region Storage
internal class SourceBans
{
    public bool IsBanned(ulong steamId)
    {
        throw new NotImplementedException();
    }

    public bool IsAdmin(ulong steamId)
    {
        throw new NotImplementedException();
    }

    public Admin GetAdmin(ulong steamId)
    {
        throw new NotImplementedException();
    }

    public void AddBan(ulong steamId, string reason, int length, Admin issuerAdmin, string targetIp = null, string adminIp = null)
    {
        throw new NotImplementedException();
    }

    public struct Admin
    {
        private ulong SteamId { get; }
        private string Name { get; }
        private int Immunity { get; }
        private string Flags { get; }
    }
}

public class SwissAdminToolsMysql : SwissAdminToolsStore
{
    private const int MaxMessages = 50;
    private const int TimerInterval = 10000; // 10 seconds
    private static readonly ConcurrentQueue<(string Message, int PlayerId)> MessageQueue = new();
    private static Timer _timer;
    private readonly MySqlConnection mConnection;

    public SwissAdminToolsMysql(string connectionString)
    {
        mConnection = new MySqlConnection(connectionString);
        mConnection.Open();
        _timer = new Timer(FlushToDatabase, null, TimerInterval, TimerInterval);
    }

    public void StoreProgression(ulong steamId, PlayerStats stats)
    {
        throw new NotImplementedException();
    }

    public void StoreChatLog(ulong steamId, string message)
    {
        var playerId = GetPlayer(steamId);
        if (!playerId.HasValue) return;
        MessageQueue.Enqueue((message, playerId.Value));
        if (MessageQueue.Count >= MaxMessages)
            Task.Run(() => { FlushToDatabase(null); });
    }

    public List<ChatLog> GetChatLogs(ulong steamId, DateTime startDate, DateTime endDate)
    {
        throw new NotImplementedException();
    }

    public void AddAdmin(AdminData admin)
    {
        throw new NotImplementedException();
    }

    public void RemoveAdmin(int adminId)
    {
        throw new NotImplementedException();
    }

    public AdminData GetAdmin(int adminId)
    {
        throw new NotImplementedException();
    }

    public List<AdminData> GetAllAdmins()
    {
        throw new NotImplementedException();
    }

    public void AddBlock(BlockData block)
    {
        throw new NotImplementedException();
    }

    public void RemoveBlock(int blockId)
    {
        throw new NotImplementedException();
    }

    public BlockData GetBlock(int blockId)
    {
        throw new NotImplementedException();
    }

    public List<BlockData> GetAllBlocksForPlayer(ulong steamId)
    {
        throw new NotImplementedException();
    }

    public void ReportPlayer(ReportData report)
    {
        throw new NotImplementedException();
    }

    public void UpdateReportStatus(int reportId, ReportStatus status, string adminNotes)
    {
        throw new NotImplementedException();
    }

    public List<ReportData> GetReportsForPlayer(ulong steamId)
    {
        throw new NotImplementedException();
    }

    public List<ReportData> GetAllPendingReports()
    {
        throw new NotImplementedException();
    }

    public void StorePlayer(ulong steamId, PlayerJoiningArguments args)
    {
        var playerQuery = new MySqlCommand(
            "INSERT INTO player (steam_id, is_banned, roles, achievements, selections, tool_progress, created_at, updated_at) " +
            "VALUES (@steamId, @isBanned, @roles, @achievements, @selections, @toolProgress, NOW(), NOW()) " +
            "ON DUPLICATE KEY UPDATE " +
            "is_banned = @isBannedUpdate, " +
            "roles = @rolesUpdate, " +
            "achievements = @achievementsUpdate, " +
            "selections = @selectionsUpdate, " +
            "tool_progress = @toolProgressUpdate, " +
            "updated_at = NOW();",
            mConnection);

        playerQuery.Parameters.AddWithValue("@steamId", steamId);
        playerQuery.Parameters.AddWithValue("@isBanned", args.Stats.IsBanned);
        playerQuery.Parameters.AddWithValue("@roles", args.Stats.Roles);
        playerQuery.Parameters.AddWithValue("@achievements", args.Stats.Achievements);
        playerQuery.Parameters.AddWithValue("@selections", args.Stats.Selections);
        playerQuery.Parameters.AddWithValue("@toolProgress", args.Stats.ToolProgress);

        // Duplicate parameters for the UPDATE section
        playerQuery.Parameters.AddWithValue("@isBannedUpdate", args.Stats.IsBanned);
        playerQuery.Parameters.AddWithValue("@rolesUpdate", args.Stats.Roles);
        playerQuery.Parameters.AddWithValue("@achievementsUpdate", args.Stats.Achievements);
        playerQuery.Parameters.AddWithValue("@selectionsUpdate", args.Stats.Selections);
        playerQuery.Parameters.AddWithValue("@toolProgressUpdate", args.Stats.ToolProgress);

        playerQuery.ExecuteNonQuery();

        var progressQuery = new MySqlCommand(
            "INSERT INTO player_progress (player_id, kill_count, leader_kills, assault_kills, medic_kills, engineer_kills, " +
            "support_kills, recon_kills, death_count, win_count, lose_count, friendly_shots, friendly_kills, revived, " +
            "revived_team_mates, assists, prestige, current_rank, exp, shots_fired, shots_hit, headshots, completed_objectives, " +
            "healed_hps, road_kills, suicides, vehicles_destroyed, vehicle_hp_repaired, longest_kill, play_time_seconds, " +
            "leader_play_time, assault_play_time, medic_play_time, engineer_play_time, support_play_time, recon_play_time, " +
            "leader_score, assault_score, medic_score, engineer_score, support_score, recon_score, total_score, created_at, updated_at) " +
            "VALUES ((SELECT id FROM player WHERE steam_id = @steamId), @killCount, @leaderKills, @assaultKills, @medicKills, @engineerKills, " +
            "@supportKills, @reconKills, @deathCount, @winCount, @loseCount, @friendlyShots, @friendlyKills, @revived, " +
            "@revivedTeamMates, @assists, @prestige, @rank, @exp, @shotsFired, @shotsHit, @headshots, @objectivesCompleted, " +
            "@healedHPs, @roadKills, @suicides, @vehiclesDestroyed, @vehicleHPRepaired, @longestKill, @playTimeSeconds, " +
            "@leaderPlayTime, @assaultPlayTime, @medicPlayTime, @engineerPlayTime, @supportPlayTime, @reconPlayTime, " +
            "@leaderScore, @assaultScore, @medicScore, @engineerScore, @supportScore, @reconScore, @totalScore, NOW(), NOW()) " +
            "ON DUPLICATE KEY UPDATE " +
            "kill_count = @killCount, leader_kills = @leaderKills, assault_kills = @assaultKills, medic_kills = @medicKills, engineer_kills = @engineerKills, " +
            "support_kills = @supportKills, recon_kills = @reconKills, death_count = @deathCount, win_count = @winCount, lose_count = @loseCount, " +
            "friendly_shots = @friendlyShots, friendly_kills = @friendlyKills, revived = @revived, revived_team_mates = @revivedTeamMates, assists = @assists, " +
            "prestige = @prestige, current_rank = @rank, exp = @exp, shots_fired = @shotsFired, shots_hit = @shotsHit, headshots = @headshots, " +
            "completed_objectives = @objectivesCompleted, healed_hps = @healedHPs, road_kills = @roadKills, suicides = @suicides, vehicles_destroyed = @vehiclesDestroyed, " +
            "vehicle_hp_repaired = @vehicleHPRepaired, longest_kill = @longestKill, play_time_seconds = @playTimeSeconds, leader_play_time = @leaderPlayTime, " +
            "assault_play_time = @assaultPlayTime, medic_play_time = @medicPlayTime, engineer_play_time = @engineerPlayTime, support_play_time = @supportPlayTime, " +
            "recon_play_time = @reconPlayTime, leader_score = @leaderScore, assault_score = @assaultScore, medic_score = @medicScore, engineer_score = @engineerScore, " +
            "support_score = @supportScore, recon_score = @reconScore, total_score = @totalScore, updated_at = NOW();",
            mConnection);

        // Add all the required parameters for the INSERT
        progressQuery.Parameters.AddWithValue("@steamId", steamId);
        progressQuery.Parameters.AddWithValue("@killCount", args.Stats.Progress.KillCount);
        progressQuery.Parameters.AddWithValue("@leaderKills", args.Stats.Progress.LeaderKills);
        progressQuery.Parameters.AddWithValue("@assaultKills", args.Stats.Progress.AssaultKills);
        progressQuery.Parameters.AddWithValue("@medicKills", args.Stats.Progress.MedicKills);
        progressQuery.Parameters.AddWithValue("@engineerKills", args.Stats.Progress.EngineerKills);
        progressQuery.Parameters.AddWithValue("@supportKills", args.Stats.Progress.SupportKills);
        progressQuery.Parameters.AddWithValue("@reconKills", args.Stats.Progress.ReconKills);
        progressQuery.Parameters.AddWithValue("@deathCount", args.Stats.Progress.DeathCount);
        progressQuery.Parameters.AddWithValue("@winCount", args.Stats.Progress.WinCount);
        progressQuery.Parameters.AddWithValue("@loseCount", args.Stats.Progress.LoseCount);
        progressQuery.Parameters.AddWithValue("@friendlyShots", args.Stats.Progress.FriendlyShots);
        progressQuery.Parameters.AddWithValue("@friendlyKills", args.Stats.Progress.FriendlyKills);
        progressQuery.Parameters.AddWithValue("@revived", args.Stats.Progress.Revived);
        progressQuery.Parameters.AddWithValue("@revivedTeamMates", args.Stats.Progress.RevivedTeamMates);
        progressQuery.Parameters.AddWithValue("@assists", args.Stats.Progress.Assists);
        progressQuery.Parameters.AddWithValue("@prestige", args.Stats.Progress.Prestige);
        progressQuery.Parameters.AddWithValue("@rank", args.Stats.Progress.Rank);
        progressQuery.Parameters.AddWithValue("@exp", args.Stats.Progress.EXP);
        progressQuery.Parameters.AddWithValue("@shotsFired", args.Stats.Progress.ShotsFired);
        progressQuery.Parameters.AddWithValue("@shotsHit", args.Stats.Progress.ShotsHit);
        progressQuery.Parameters.AddWithValue("@headshots", args.Stats.Progress.Headshots);
        progressQuery.Parameters.AddWithValue("@objectivesCompleted", args.Stats.Progress.ObjectivesComplated);
        progressQuery.Parameters.AddWithValue("@healedHPs", args.Stats.Progress.HealedHPs);
        progressQuery.Parameters.AddWithValue("@roadKills", args.Stats.Progress.RoadKills);
        progressQuery.Parameters.AddWithValue("@suicides", args.Stats.Progress.Suicides);
        progressQuery.Parameters.AddWithValue("@vehiclesDestroyed", args.Stats.Progress.VehiclesDestroyed);
        progressQuery.Parameters.AddWithValue("@vehicleHPRepaired", args.Stats.Progress.VehicleHPRepaired);
        progressQuery.Parameters.AddWithValue("@longestKill", args.Stats.Progress.LongestKill);
        progressQuery.Parameters.AddWithValue("@playTimeSeconds", args.Stats.Progress.PlayTimeSeconds);
        progressQuery.Parameters.AddWithValue("@leaderPlayTime", args.Stats.Progress.LeaderPlayTime);
        progressQuery.Parameters.AddWithValue("@assaultPlayTime", args.Stats.Progress.AssaultPlayTime);
        progressQuery.Parameters.AddWithValue("@medicPlayTime", args.Stats.Progress.MedicPlayTime);
        progressQuery.Parameters.AddWithValue("@engineerPlayTime", args.Stats.Progress.EngineerPlayTime);
        progressQuery.Parameters.AddWithValue("@supportPlayTime", args.Stats.Progress.SupportPlayTime);
        progressQuery.Parameters.AddWithValue("@reconPlayTime", args.Stats.Progress.ReconPlayTime);
        progressQuery.Parameters.AddWithValue("@leaderScore", args.Stats.Progress.LeaderScore);
        progressQuery.Parameters.AddWithValue("@assaultScore", args.Stats.Progress.AssaultScore);
        progressQuery.Parameters.AddWithValue("@medicScore", args.Stats.Progress.MedicScore);
        progressQuery.Parameters.AddWithValue("@engineerScore", args.Stats.Progress.EngineerScore);
        progressQuery.Parameters.AddWithValue("@supportScore", args.Stats.Progress.SupportScore);
        progressQuery.Parameters.AddWithValue("@reconScore", args.Stats.Progress.ReconScore);
        progressQuery.Parameters.AddWithValue("@totalScore", args.Stats.Progress.TotalScore);

        progressQuery.ExecuteNonQuery();
    }


    public int? GetPlayer(ulong steamId)
    {
        var command = new MySqlCommand("SELECT * FROM player WHERE steam_id = @steamId", mConnection);
        command.Parameters.AddWithValue("@steamId", steamId);

        var result = command.ExecuteScalar();
        if (result != null)
            return Convert.ToInt32(result);
        return null;
    }

    private void FlushToDatabase(object state)
    {
        if (!MessageQueue.Any())
            return;

        using var transaction = mConnection.BeginTransaction();
        try
        {
            var command =
                new MySqlCommand(
                    "INSERT INTO chat_logs (message, player_id, timestamp) VALUES (@message, @playerId, @timestamp)",
                    mConnection, transaction);
            command.Parameters.Add("@message", MySqlDbType.Text);
            command.Parameters.Add("@playerId", MySqlDbType.Int32);
            command.Parameters.Add("@timestamp", MySqlDbType.DateTime);

            while (MessageQueue.TryDequeue(out var log))
            {
                command.Parameters["@message"].Value = log.Message;
                command.Parameters["@playerId"].Value = log.PlayerId;
                command.Parameters["@timestamp"].Value = DateTime.Now;

                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Close()
    {
        _timer.Dispose();
        mConnection.Close();
    }
}

public class ChatLog
{
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AdminData
{
    public string Name { get; set; }
    public int Immunity { get; set; }
    public string Flags { get; set; }
}

public class BlockData
{
    public ulong SteamId { get; set; }
    public BlockType Type { get; set; }
    public string Reason { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int IssuerAdminId { get; set; }
    public string TargetIp { get; set; }
    public string AdminIp { get; set; }
}

public class ReportData
{
    public int ReporterId { get; set; }
    public int ReportedPlayerId { get; set; }
    public string Reason { get; set; }
    public DateTime Timestamp { get; set; }
    public ReportStatus Status { get; set; }
    public string AdminNotes { get; set; }
}

public enum BlockType
{
    Ban,
    Gag,
    Mute
}

public enum ReportStatus
{
    Pending,
    Reviewed,
    Resolved,
    Dismissed
}

public interface SwissAdminToolsStore
{
    // Player Progress
    void StoreProgression(ulong steamId, PlayerStats stats);
    int? GetPlayer(ulong steamId);

    // Chat logs
    void StoreChatLog(ulong steamId, string message);
    List<ChatLog> GetChatLogs(ulong steamId, DateTime startDate, DateTime endDate);

    // Admin data
    void AddAdmin(AdminData admin);
    void RemoveAdmin(int adminId);
    AdminData GetAdmin(int adminId);
    List<AdminData> GetAllAdmins();

    // Blocks
    void AddBlock(BlockData block);
    void RemoveBlock(int blockId);
    BlockData GetBlock(int blockId);
    List<BlockData> GetAllBlocksForPlayer(ulong steamId);

    // Player reports
    void ReportPlayer(ReportData report);
    void UpdateReportStatus(int reportId, ReportStatus status, string adminNotes);
    List<ReportData> GetReportsForPlayer(ulong steamId);
    List<ReportData> GetAllPendingReports();
    void StorePlayer(ulong steamId, PlayerJoiningArguments player);
}
#endregion

#region Utils
public static class RichText
{
    public const string LineBreak = "<br>";

    public const string EndColor = "</color>";
    // https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/StyledText.html#supported-colors
    // http://digitalnativestudios.com/textmeshpro/docs/rich-text/

    #region Colors

    public const string Aqua = "<color=#00ffff>";
    public const string Black = "<color=#000000>";
    public const string Blue = "<color=#0000ff>";
    public const string Brown = "<color=#a52a2a>";
    public const string Cyan = "<color=#00ffff>";
    public const string DarkBlue = "<color=#0000a0>";
    public const string Fuchsia = "<color=#ff00ff>";
    public const string Green = "<color=#008000>";
    public const string Grey = "<color=#808080>";
    public const string LightBlue = "<color=#add8e6>";
    public const string Lime = "<color=#00ff00>";
    public const string Magenta = "<color=#ff00ff>";
    public const string Maroon = "<color=#800000>";
    public const string Navy = "<color=#000080>";
    public const string Olive = "<color=#808000>";
    public const string Orange = "<color=#ffa500>";
    public const string Purple = "<color=#800080>";
    public const string Red = "<color=#ff0000>";
    public const string Silver = "<color=#c0c0c0>";
    public const string Teal = "<color=#008080>";
    public const string White = "<color=#ffffff>";
    public const string Yellow = "<color=#ffff00>";

    #endregion

    #region Sprites

    //icons
    public const string Moderator = "<sprite index=0>";
    public const string Patreon = "<sprite index=1>";
    public const string Creator = "<sprite index=2>";
    public const string DiscordBooster = "<sprite index=3>";
    public const string Special = "<sprite index=4>";
    public const string PatreonFirebacker = "<sprite index=5>";
    public const string Vip = "<sprite index=6>";
    public const string Supporter = "<sprite index=7>";
    public const string Developer = "<sprite index=8>";
    public const string Veteran = "<sprite index=9>";
    public const string Misc1 = "<sprite index=10>";
    public const string Misc2 = "<sprite index=11>";
    public const string Misc3 = "<sprite index=12>";
    public const string Misc4 = "<sprite index=13>";
    public const string Misc5 = "<sprite index=14>";
    public const string Misc6 = "<sprite index=15>";

    //emojis
    public const string Blush = "<sprite=\"EmojiOne\" index=0>";
    public const string Yum = "<sprite=\"EmojiOne\" index=1>";
    public const string HeartEyes = "<sprite=\"EmojiOne\" index=2>";
    public const string Sunglasses = "<sprite=\"EmojiOne\" index=3>";
    public const string Grinning = "<sprite=\"EmojiOne\" index=4>";
    public const string Smile = "<sprite=\"EmojiOne\" index=5>";
    public const string Joy = "<sprite=\"EmojiOne\" index=6>";
    public const string Smiley = "<sprite=\"EmojiOne\" index=7>";
    public const string Grin = "<sprite=\"EmojiOne\" index=8>";
    public const string SweatSmile = "<sprite=\"EmojiOne\" index=9>";
    public const string Tired = "<sprite=\"EmojiOne\" index=10>";
    public const string TongueOutWink = "<sprite=\"EmojiOne\" index=11>";
    public const string Kiss = "<sprite=\"EmojiOne\" index=12>";
    public const string Rofl = "<sprite=\"EmojiOne\" index=13>";
    public const string SlightSmile = "<sprite=\"EmojiOne\" index=14>";
    public const string SlightFrown = "<sprite=\"EmojiOne\" index=15>";

    #endregion

    #region Text Formatting

    public static string Bold(string text)
    {
        return $"<b>{text}</b>";
    }

    public static string Italic(string text)
    {
        return $"<i>{text}</i>";
    }

    public static string Underline(string text)
    {
        return $"<u>{text}</u>";
    }

    public static string Strike(string text)
    {
        return $"<s>{text}</s>";
    }

    public static string SuperScript(string text)
    {
        return $"<sup>{text}</sup>";
    }

    public static string SubScript(string text)
    {
        return $"<sub>{text}</sub>";
    }

    #endregion

    #region Styles

    public static string StyleH1(string text)
    {
        return $"<style=\"H1\">{text}</style>";
    }

    public static string StyleH2(string text)
    {
        return $"<style=\"H2\">{text}</style>";
    }

    public static string StyleH3(string text)
    {
        return $"<style=\"H3\">{text}</style>";
    }

    public static string StyleC1(string text)
    {
        return $"<style=\"C1\">{text}</style>";
    }

    public static string StyleC2(string text)
    {
        return $"<style=\"C2\">{text}</style>";
    }

    public static string StyleC3(string text)
    {
        return $"<style=\"C3\">{text}</style>";
    }

    public static string StyleNormal(string text)
    {
        return $"<style=\"Normal\">{text}</style>";
    }

    public static string StyleTitle(string text)
    {
        return $"<style=\"Title\">{text}</style>";
    }

    public static string StyleQuote(string text)
    {
        return $"<style=\"Quote\">{text}</style>";
    }

    public static string StyleLink(string text)
    {
        return $"<style=\"Link\">{text}</style>";
    }

    public static string Highlight(string text, string color)
    {
        return $"<mark={color}>{text}</mark>";
    }

    public static string VerticalOffset(string text, float amount)
    {
        return $"<voffset={amount}em>{text}</voffset>";
    }

    public static string Size(string text, int sizeValue)
    {
        return $"<size={sizeValue}>{text}</size>";
    }

    #endregion
}
#endregion