using MobaTypes;
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using static MatchService;

public class GameService
{
    ConcurrentDictionary<int, GameRoom> RoomList = new ConcurrentDictionary<int, GameRoom>();

    private int _roomIdGenerator = 0;

    public int GenerateRoomId()
    {
        return Interlocked.Increment(ref _roomIdGenerator);
    }

    public void UpdateAllRooms(float deltaTime)
    {
        foreach (var room in RoomList.Values)
            room.Update(deltaTime);
    }

    public void CreateRoom(GameType matchType, MatchEntry[] group)
    {
        GameRoom gameRoom = new GameRoom(GenerateRoomId(), matchType);
        RoomList.TryAdd(gameRoom.RoomId, gameRoom);

        foreach (var temp in group)
        {
            gameRoom.AddPlayer(temp.UID);
            PacketManager.Instance.NotifyMatchSuccess(temp.UID, gameRoom);
        }

        // MVP: 캐릭터 선택 씬 스킵하고 바로 UserGameStart
        gameRoom.StartGameImmediately();

        Debug.Log($"[Match] Room {gameRoom.RoomId} created ({matchType}, players={group.Length})");
    }
    public void DestroyRoom(int Index)
    {
        RoomList.TryRemove(Index, out _);
    }

    public void RemovePlayerFromRoom(GameRoom room, int uid)
    {
        if (room == null)
            return;

        room.RemovePlayer(uid);
        if (room.PlayerCount == 0)
            DestroyRoom(room.RoomId);
    }
}
