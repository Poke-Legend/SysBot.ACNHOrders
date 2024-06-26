using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{

    public class Penalty
    {
        public string ID = string.Empty;
        public uint PenaltyCount = 0;

        public Penalty(string id, uint pcount)
        {
            ID = id;
            PenaltyCount = pcount;
        }

        public override string ToString() => ID;
    }
    public class GlobalBan
    {
        private const string UserBanFilePath = "userbanlist.txt";
        private const string ServerBanFilePath = "serverbanlist.txt";

        private static int PenaltyCountBan = 0;
        private static readonly List<Penalty> PenaltyList = new();

        private static readonly object UserMapAccessor = new object();
        private static readonly object ServerMapAccessor = new object();

        // User Bans
        private static readonly List<string> BannedUsers = new List<string>();

        // Server Bans
        private static readonly List<string> BannedServers = new List<string>();

        public static void UpdateConfiguration(CrossBotConfig config)
        {
            PenaltyCountBan = config.OrderConfig.PenaltyBanCount;

            lock (UserMapAccessor)
            {
                LoadBannedUsers();
                LoadBannedServers();
            }
        }

        public static bool Penalize(string id)
        {
            if (PenaltyCountBan < 1)
                return false;
            lock (UserMapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);

                if (pen != null)
                {
                    pen.PenaltyCount++;
                }
                else
                {
                    pen = new Penalty(id, 1);
                    PenaltyList.Add(pen);
                }
                SaveBannedServers();
                SaveBannedUsers();
                return pen.PenaltyCount >= PenaltyCountBan;
            }
        }


        // Load bans from files on startup
        public static void LoadBans()
        {
            LoadBannedUsers();
            LoadBannedServers();
        }

        // User Ban Methods
        public static void Ban(string userId)
        {
            lock (UserMapAccessor)
            {
                if (!BannedUsers.Contains(userId))
                {
                    BannedUsers.Add(userId);
                    SaveBannedUsers();
                }
            }
        }

        public static void UnBan(string userId)
        {
            lock (UserMapAccessor)
            {
                if (BannedUsers.Contains(userId))
                {
                    BannedUsers.Remove(userId);
                    SaveBannedUsers();
                }
            }
        }

        public static bool IsBanned(string userId)
        {
            lock (UserMapAccessor)
            {
                return BannedUsers.Contains(userId);
            }
        }

        private static void LoadBannedUsers()
        {
            if (File.Exists(UserBanFilePath))
            {
                BannedUsers.AddRange(File.ReadAllLines(UserBanFilePath));
            }
        }

        private static void SaveBannedUsers()
        {
            File.WriteAllLines(UserBanFilePath, BannedUsers);
        }

        // Server Ban Methods
        public static void BanServer(string serverId)
        {
            lock (ServerMapAccessor)
            {
                if (!BannedServers.Contains(serverId))
                {
                    BannedServers.Add(serverId);
                    SaveBannedServers();
                }
            }
        }

        public static void UnbanServer(string serverId)
        {
            lock (ServerMapAccessor)
            {
                if (BannedServers.Contains(serverId))
                {
                    BannedServers.Remove(serverId);
                    SaveBannedServers();
                }
            }
        }

        public static bool IsServerBanned(string serverId)
        {
            lock (ServerMapAccessor)
            {
                return BannedServers.Contains(serverId);
            }
        }

        private static void LoadBannedServers()
        {
            if (File.Exists(ServerBanFilePath))
            {
                BannedServers.AddRange(File.ReadAllLines(ServerBanFilePath));
            }
        }

        private static void SaveBannedServers()
        {
            File.WriteAllLines(ServerBanFilePath, BannedServers);
        }
    }
}
