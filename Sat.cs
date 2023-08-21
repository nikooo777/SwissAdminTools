using BattleBitAPI;
using BattleBitAPI.Common;
using BattleBitAPI.Server;
using BBRAPIModules;
using CommunityServerAPI.Storage;
using SAT.Storage;
using SAT.SwissAdminTools;
using System;
using System.Threading.Tasks;

namespace SwissAdminTools;

public class SATConfiguration : ModuleConfiguration
{
    public string ConnectionString { get; set; } = "server=localhost;user=battlebit;password=battlebit;database=battlebit";
}

public class Sat : BattleBitModule
{
    public SATConfiguration Configuration { get; set; }

    public Sat()
    {
        Storage = new SwissAdminToolsMysql(this.Configuration.ConnectionString); // breaks: Configuration is available on modules loaded, not constructor
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