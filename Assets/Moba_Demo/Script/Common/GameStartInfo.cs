using System.Collections.Generic;
using UnityEngine;

// UserGameStart 패킷 파싱 결과 (Main 스폰용)
public class GameStartInfo
{
    public int RoomId;
    public int PlayerCount;
    public readonly List<GameStartPlayerSpawn> Players = new();

    public struct GameStartPlayerSpawn
    {
        public int Uid;
        public Vector3 Position;
    }
}
