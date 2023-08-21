using System.Collections.Concurrent;
using BattleBitAPI.Common;
using CommunityServerAPI.Storage;
using MySql.Data.MySqlClient;

namespace SAT.Storage;

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