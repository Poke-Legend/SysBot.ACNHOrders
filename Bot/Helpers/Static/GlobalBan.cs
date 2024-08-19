using System;
using System.Collections.Generic;
using System.IO;

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

        public void IncrementPenaltyCount() => PenaltyCount++;

        public override string ToString() => ID;
    }

    public static class GlobalBan
    {
        private const string UserBanFilePath = "userban.txt";
        private const string ServerBanFilePath = "serverban.txt";

        private static int PenaltyCountBan;
        private static readonly List<Penalty> PenaltyList = new();
        private static readonly object UserMapAccessor = new();
        private static readonly object ServerMapAccessor = new();
        private static readonly List<string> BannedUsers = new();
        private static readonly List<string> BannedServers = new();

        public static void UpdateConfiguration(CrossBotConfig config)
        {
            PenaltyCountBan = config.OrderConfig.PenaltyBanCount;
            EnsureFileExists(UserBanFilePath);
            EnsureFileExists(ServerBanFilePath);
            LoadBannedUsers();
            LoadBannedServers();
        }

        public static bool Penalize(string id)
        {
            lock (UserMapAccessor)
            {
                var pen = PenaltyList.Find(x => x.ID == id) ?? new Penalty(id, 1);
                if (!PenaltyList.Contains(pen))
                {
                    PenaltyList.Add(pen);
                }
                else
                {
                    pen.IncrementPenaltyCount();
                }

                if (pen.PenaltyCount >= PenaltyCountBan)
                {
                    Ban(id);
                }

                SaveBannedUsers();
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
                if (BannedUsers.Remove(userId))
                {
                    SaveBannedUsers();
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
                if (BannedServers.Remove(serverId))
                {
                    SaveBannedServers();
                }
            }
        }

        private static void LoadBannedUsers()
        {
            lock (UserMapAccessor)
            {
                BannedUsers.Clear();
                BannedUsers.AddRange(File.ReadAllLines(UserBanFilePath));
            }
        }

        private static void LoadBannedServers()
        {
            lock (ServerMapAccessor)
            {
                BannedServers.Clear();
                BannedServers.AddRange(File.ReadAllLines(ServerBanFilePath));
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

        private static void EnsureFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                File.Create(filePath).Dispose();
            }
        }
    }
}
