using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class Penalty
    {
        public string ID { get; private set; }
        public uint PenaltyCount { get; private set; }

        public Penalty(string id, uint pcount)
        {
            ID = id;
            PenaltyCount = pcount;
        }

        public void IncrementPenaltyCount()
        {
            PenaltyCount++;
        }

        public override string ToString() => ID;
    }

    public class GlobalBan
    {
        private const string UserBanFilePath = "userbanlist.txt";
        private const string ServerBanFilePath = "serverbanlist.txt";

        private static int PenaltyCountBan = 0;
        private static readonly List<Penalty> PenaltyList = new List<Penalty>();

        private static readonly object UserMapAccessor = new object();
        private static readonly object ServerMapAccessor = new object();

        // User Bans
        private static readonly List<string> BannedUsers = new List<string>();

        // Server Bans
        private static readonly List<string> BannedServers = new List<string>();

        public static void UpdateConfiguration(CrossBotConfig config)
        {
            PenaltyCountBan = config.OrderConfig.PenaltyBanCount;
            LoadBannedUsers();
            LoadBannedServers();
        }

        public static bool Penalize(string id)
        {
            lock (UserMapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id);
                if (pen != null)
                {
                    pen.IncrementPenaltyCount();
                }
                else
                {
                    pen = new Penalty(id, 1);
                    PenaltyList.Add(pen);
                }

                if (pen.PenaltyCount >= PenaltyCountBan)
                {
                    Ban(id);
                }

                SaveBannedUsers();
                SaveBannedServers();

                return pen.PenaltyCount >= PenaltyCountBan;
            }
        }

        public static bool IsServerBanned(string serverId)
        {
            lock (ServerMapAccessor)
            {
                return BannedServers.Contains(serverId);
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
            lock (UserMapAccessor)
            {
                BannedUsers.Clear();
                if (File.Exists(UserBanFilePath))
                {
                    BannedUsers.AddRange(File.ReadAllLines(UserBanFilePath));
                }
            }
        }

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

        private static void LoadBannedServers()
        {
            lock (ServerMapAccessor)
            {
                BannedServers.Clear();
                if (File.Exists(ServerBanFilePath))
                {
                    BannedServers.AddRange(File.ReadAllLines(ServerBanFilePath));
                }
            }
        }

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

        private static void SaveBannedUsers()
        {
            lock (UserMapAccessor)
            {
                File.WriteAllLines(UserBanFilePath, BannedUsers);
            }
        }

        private static void SaveBannedServers()
        {
            lock (ServerMapAccessor)
            {
                File.WriteAllLines(ServerBanFilePath, BannedServers);
            }
        }
    }
}
