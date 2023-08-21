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