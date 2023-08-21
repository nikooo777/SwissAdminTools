using BattleBitAPI.Common;

namespace CommunityServerAPI.Storage;

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