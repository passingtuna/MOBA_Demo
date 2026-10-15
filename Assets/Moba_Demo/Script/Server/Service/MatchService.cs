using MobaTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static PacketManager;
public class MatchService
{
    private readonly object _lock = new();

    private ConcurrentDictionary<int, MatchEntry> playersMatchEntry = new();
    private ConcurrentDictionary<GameType, SortedSet<MatchEntry>> matchPools = new();

    // MMR 차이 허용치 — 대기할수록 넓어짐
    private int baseRange = 50;
    private int expandPerSecond = 10;
    private readonly GameService GameService;

    public class MatchEntry
    {
        public int UID;
        public int MMR;
        public float JoinTime;
        public GameType MatchType;
    }

    public MatchService(GameService gameService)
    {
        GameService = gameService;
        matchPools.TryAdd(GameType.Practice, new SortedSet<MatchEntry>(new MatchEntryComparer()));
        matchPools.TryAdd(GameType.OneVsOne, new SortedSet<MatchEntry>(new MatchEntryComparer()));
        matchPools.TryAdd(GameType.FiveVsFive, new SortedSet<MatchEntry>(new MatchEntryComparer()));
        matchPools.TryAdd(GameType.FreeForAll , new SortedSet<MatchEntry>(new MatchEntryComparer()));
    }

    public void RunMatchingAlgorithm()
    {
        foreach (GameType type in Enum.GetValues(typeof(GameType)))
            TryMatchPool(type);
    }

    private void TryMatchPool(GameType type)
    {
        MatchEntry[] matchedGroup = null;

        lock (_lock)
        {
            if (!matchPools.TryGetValue(type, out var pool))
                return;

            int requiredCount = MobaMatchRules.GetRequiredPlayerCount(type);
            if (requiredCount < 0 || pool.Count < requiredCount)
                return;

            var list = pool.ToArray();

            for (int i = 0; i <= list.Length - requiredCount; i++)
            {
                int minMMR = int.MaxValue;
                int maxMMR = int.MinValue;
                int dynamicRange = 0;

                for (int j = 0; j < requiredCount; j++)
                {
                    var entry = list[i + j];
                    minMMR = Math.Min(minMMR, entry.MMR);
                    maxMMR = Math.Max(maxMMR, entry.MMR);
                    dynamicRange = Math.Max(dynamicRange, GetAllowedRange(entry));
                }

                if ((maxMMR - minMMR) <= dynamicRange)
                {
                    matchedGroup = new MatchEntry[requiredCount];
                    Array.Copy(list, i, matchedGroup, 0, requiredCount);

                    foreach (var entry in matchedGroup)
                    {
                        pool.Remove(entry);
                        playersMatchEntry.TryRemove(entry.UID, out _);
                    }
                    break;
                }
            }
        }

        if (matchedGroup == null)
            return;

        GameService.CreateRoom(type, matchedGroup);
        BroadcastPoolStatus(type);
    }

    private int GetAllowedRange(MatchEntry entry)
    {
        float waitTime = Time.realtimeSinceStartup - entry.JoinTime;
        return baseRange + (int)(waitTime * expandPerSecond);
    }

    public void HandleMatchingRequest(int uid, Packet packet)
    {
        bool join = packet.ReadBool();
        int type = packet.ReadInt();
        GameType matchType = (GameType)type;

        if (join)
            MatchingEnqueue(matchType, uid);
        else
            MatchingDequeue(uid);
    }

    public void MatchingEnqueue(GameType matchType, int uid)
    {
        var userData = UserManager.Instance.GetUserData(uid);
        if (userData == null)
        {
            Debug.LogWarning($"[Match] No user data for uid {uid}");
            return;
        }

        lock (_lock)
        {
            if (playersMatchEntry.TryGetValue(uid, out var existing))
            {
                matchPools[existing.MatchType].Remove(existing);
                playersMatchEntry.TryRemove(uid, out _);
            }

            var entry = new MatchEntry
            {
                UID = uid,
                MMR = userData.GetMMR(),
                JoinTime = Time.realtimeSinceStartup,
                MatchType = matchType
            };

            playersMatchEntry[uid] = entry;
            matchPools[matchType].Add(entry);
        }

        BroadcastPoolStatus(matchType);

        // 솔로 연습은 1명이면 바로 방 생성
        if (matchType == GameType.Practice)
            TryMatchPool(GameType.Practice);
    }

    public void MatchingDequeue(int uid)
    {
        GameType matchType;

        lock (_lock)
        {
            if (!playersMatchEntry.TryGetValue(uid, out var entry))
                return;

            matchType = entry.MatchType;
            matchPools[entry.MatchType].Remove(entry);
            playersMatchEntry.TryRemove(uid, out _);
        }

        BroadcastPoolStatus(matchType);
    }

    public void BroadcastPoolStatus(GameType matchType)
    {
        int required;
        MatchEntry[] snapshot;

        lock (_lock)
        {
            if (!matchPools.TryGetValue(matchType, out var pool))
                return;

            required = MobaMatchRules.GetRequiredPlayerCount(matchType);
            snapshot = pool.ToArray();
        }

        int count = snapshot.Length;
        foreach (var entry in snapshot)
            PacketManager.Instance.SendMatchingStatus(entry.UID, count, required);
    }

    class MatchEntryComparer : IComparer<MatchEntry>
    {
        public int Compare(MatchEntry x, MatchEntry y)
        {
            int cmp = x.MMR.CompareTo(y.MMR);
            return cmp == 0 ? x.UID.CompareTo(y.UID) : cmp;
        }
    }
}