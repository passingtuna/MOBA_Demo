using MobaTypes;
using System.Collections.Generic;
using UnityEngine;
using static PacketManager;

// 같은 프로세스 안에서 돌리는 게임 방 (별도 게임 서버 exe 없음)
public class GameRoom
{
    public int RoomId;
    public GameType MatchType { get; }
    public GameState State { get; private set; } = GameState.CharacterSelect;
    private readonly List<RoomPlayer> Players = new();

    private static readonly Vector3[] DefaultSpawnPoints =
    {
        new(-6f, 0f, 0f),
        new(6f, 0f, 0f),
        new(-6f, 0f, 6f),
        new(6f, 0f, 6f),
        new(0f, 0f, -6f),
        new(0f, 0f, 6f),
        new(-6f, 0f, -6f),
        new(6f, 0f, -6f),
        new(0f, 0f, 0f),
        new(3f, 0f, 3f),
    };

    public class RoomPlayer
    {
        public int UID;
        public PlayableCharacter SelectChar = PlayableCharacter.Gunner;
        public bool LockedIn;
        public int TeamNum;
        public Vector3 Position;
    }

    public GameRoom(int roomId, GameType matchType)
    {
        RoomId = roomId;
        MatchType = matchType;
    }

    public int PlayerCount => Players.Count;

    public void AddPlayer(int uid)
    {
        var player = new RoomPlayer { UID = uid };
        Players.Add(player);
    }

    public bool RemovePlayer(int uid)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].UID != uid)
                continue;
            Players.RemoveAt(i);
            return true;
        }

        return false;
    }

    // 캐릭터 선택 없이 매칭 직후 게임 시작 (MVP)
    public void StartGameImmediately()
    {
        if (State != GameState.CharacterSelect)
            return;

        for (int i = 0; i < Players.Count; i++)
        {
            Players[i].SelectChar = PlayableCharacter.Gunner;
            Players[i].LockedIn = true;
            Players[i].Position = DefaultSpawnPoints[i % DefaultSpawnPoints.Length];
        }

        BeginGame();
    }

    public bool TryLockCharacter(int uid, PlayableCharacter character)
    {
        if (State != GameState.CharacterSelect)
            return false;

        if (character == PlayableCharacter.None)
            return false;

        RoomPlayer target = null;
        foreach (var player in Players)
        {
            if (player.UID != uid)
                continue;
            target = player;
            break;
        }

        if (target == null || target.LockedIn)
            return false;

        target.SelectChar = character;
        target.LockedIn = true;
        BroadcastCharacterSync();

        if (AllPlayersLockedIn())
            BeginGame();

        return true;
    }

    private bool AllPlayersLockedIn()
    {
        if (Players.Count == 0)
            return false;

        foreach (var player in Players)
        {
            if (!player.LockedIn || player.SelectChar == PlayableCharacter.None)
                return false;
        }

        return true;
    }

    private void BeginGame()
    {
        State = GameState.Opening;

        foreach (var player in Players)
        {
            if (Instance.UserSessions.TryGetValue(player.UID, out var session))
                session.SessionState = SessionState.InGame;
        }

        foreach (var player in Players)
            Instance.NotifyGameStart(player.UID, this);
    }

    public void UpdatePlayerPosition(int uid, Vector3 position)
    {
        foreach (var player in Players)
        {
            if (player.UID != uid)
                continue;
            player.Position = position;
            return;
        }
    }

    // 보낸 사람 빼고 방 인원 전원에게 UserPositionSync
    public void BroadcastPosition(int senderUid)
    {
        Vector3 position = default;
        bool found = false;
        foreach (var player in Players)
        {
            if (player.UID != senderUid)
                continue;
            position = player.Position;
            found = true;
            break;
        }

        if (!found)
            return;

        foreach (var player in Players)
        {
            if (player.UID == senderUid)
                continue;
            Instance.SendPositionSync(player.UID, senderUid, position);
        }
    }

    public void BroadcastCharacterSync()
    {
        foreach (var player in Players)
            Instance.SendCharacterSelectSync(player.UID, this);
    }

    public void FillSyncPacket(Packet packet)
    {
        packet.Write(Players.Count);
        foreach (var player in Players)
        {
            packet.Write(player.UID);
            packet.Write((int)player.SelectChar);
            packet.Write(player.LockedIn);
        }
    }

    public void FillGameStartPacket(Packet packet)
    {
        packet.Write(1);
        packet.Write(RoomId);
        packet.Write(Players.Count);
        foreach (var player in Players)
        {
            packet.Write(player.UID);
            packet.Write(player.Position.x);
            packet.Write(player.Position.y);
            packet.Write(player.Position.z);
        }
    }

    public void Update(float deltaTime)
    {
    }

    public void HandlePacket(NetworkSession session, Packet packet)
    {
    }
}
