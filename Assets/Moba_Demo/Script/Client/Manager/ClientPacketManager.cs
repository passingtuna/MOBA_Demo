using MobaTypes;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// 클라 TCP: 수신은 백그라운드 Task → ReceiveQueue, 송신은 SendQueue → Update에서 Flush
public class ClientPacketManager : MonoBehaviour
{
    public static ClientPacketManager Instance { get; private set; }

    private TcpClient Client;
    private NetworkStream Stream;

    private readonly ConcurrentQueue<Packet> ReceiveQueue = new();
    private readonly ConcurrentQueue<Packet> SendQueue = new();

    private readonly Dictionary<PacketType, Action<Packet>> PacketHandlers = new();
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;

    public bool IsConnected => Client != null && Client.Connected;
    // Login 성공 후 true. TCP만 붙어 있고 로그인 안 한 소켓은 false
    public bool HasServerSession { get; private set; }
    public bool GameLowLatencyEnabled { get; private set; }

    // Login/Regist 동시에 ConnectAsync 두 번 안 타게
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    void Awake()
    {

        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);

        if (GetComponent<ClientMatchState>() == null)
            gameObject.AddComponent<ClientMatchState>();

        if (GetComponent<ClientSessionGuard>() == null)
            gameObject.AddComponent<ClientSessionGuard>();

        RegisterHandlers();
    }

    void Update()
    {
        ProcessPackets();
        FlushSendQueue();
    }

    public static class PacketPool
    {
        private static readonly ConcurrentQueue<Packet> _pool = new();

        public static Packet Rent()
        {
            if (_pool.TryDequeue(out var packet))
            {
                packet.PrepareForReuse();
                return packet;
            }

            return new Packet();
        }

        public static void Return(Packet packet)
        {
            packet.Reset();
            _pool.Enqueue(packet);
        }
    }


    public Task<bool> ConnectAsync(string ip, int port) => EnsureConnectedAsync(ip, port);

    // 끊겨 있으면 접속. 이미 Connected면 재접속 안 함 (로그인 패킷은 따로)
    public async Task<bool> EnsureConnectedAsync(
        string ip = null,
        int port = 0)
    {
        if (IsConnected)
            return true;

        string host = string.IsNullOrEmpty(ip) ? MobaNetwork.DefaultHost : ip;
        int targetPort = port <= 0 ? MobaNetwork.DefaultPort : port;

        await _connectGate.WaitAsync();
        try
        {
            if (IsConnected)
                return true;

            return await ConnectInternalAsync(host, targetPort);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task<bool> ConnectInternalAsync(string ip, int port)
    {
        CleanupSocket();

        try
        {
            Client = new TcpClient();
            await Client.ConnectAsync(ip, port);
            Stream = Client.GetStream();

            Debug.Log("[Client] Connected to server");
            SetGameLowLatency(false);

#pragma warning disable CS4014
            _ = ReceiveLoopAsync();
#pragma warning restore CS4014
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Client] Connect failed: {ex.Message}");
            CleanupSocket();
            return false;
        }
    }

    public void Disconnect()
    {
        SetGameLowLatency(false);
        MarkDisconnected();
    }

    public void SetServerSessionActive(bool active) => HasServerSession = active;

    // 로비 자동 재로그인용 (현재는 Login UI 경로만 쓰는 게 기본)
    public async Task<bool> RestoreServerSessionAsync(string userId, string password)
    {
        var tcs = new TaskCompletionSource<bool>();

        void Handler(int result, string nickName, int money, int mmr)
        {
            OnLoginResult -= Handler;

            if (result <= 0)
            {
                tcs.TrySetResult(false);
                return;
            }

            if (UserManager.Instance != null)
            {
                var userData = UserManager.Instance.GetUserData() ?? new UserData();
                userData.SetUid(result);
                userData.SetNickName(nickName);
                userData.SetMoney(money);
                userData.SetMMR(mmr);
                UserManager.Instance.SetUserData(userData);
            }

            HasServerSession = true;
            tcs.TrySetResult(true);
        }

        OnLoginResult += Handler;

        var packet = new Packet();
        packet.Type = PacketType.UserLoginReq;
        packet.Write(userId);
        packet.Write(password);
        SendPacket(packet);

        return await tcs.Task;
    }

    // TCP 끊길 때 (로비 정책, 매칭 팝업 등에서 구독)
    public static event Action OnDisconnected;

    private void MarkDisconnected()
    {
        bool hadConnection = Client != null;
        HasServerSession = false;
        CleanupSocket();

        if (hadConnection)
            OnDisconnected?.Invoke();
    }

    private void CleanupSocket()
    {
        try
        {
            Stream?.Close();
        }
        catch
        {
            // ignored
        }

        try
        {
            Client?.Close();
        }
        catch
        {
            // ignored
        }

        Stream = null;
        Client = null;
    }

    // 인게임만 NoDelay — 소켓 전체 설정이라 로비/로그인에선 꺼 둠
    public void SetGameLowLatency(bool enabled)
    {
        GameLowLatencyEnabled = enabled;
        if (Client?.Client != null)
            MobaTcpTraffic.SetNoDelay(Client.Client, enabled);
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] lengthBuf = BufferPool.Rent(4);

        try
        {
            while (IsConnected)
            {
                await ReadExactAsync(lengthBuf, 4);
                int length = BitConverter.ToInt32(lengthBuf, 0);

                // 프레이밍: [4byte len][1byte type][payload...] — len은 type+payload 길이
                if (length <= 0 || length > 65535)
                    throw new Exception("Invalid packet length");
                byte[] packetBuf = ArrayPool<byte>.Shared.Rent(length);
                Packet packet = PacketPool.Rent();

                try
                {
                    await ReadExactAsync(packetBuf, length);
                    PacketType type = (PacketType)packetBuf[0];
                    packet.Type = type;
                    packet.SetData(packetBuf, length, fromPool: true);
                    packet.SetReadPos(1);
                    ReceiveQueue.Enqueue(packet);
                }
                catch
                {
                    // 예외 발생 시 Packet 반환
                    packet.Reset();
                    PacketPool.Return(packet);
                    throw;
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log($"[Client] Disconnected: {e.Message}");
            MarkDisconnected();
        }
        finally
        {
            BufferPool.Return(lengthBuf);
        }
    }

    private async Task ReadExactAsync(byte[] buffer, int size)
    {
        int read = 0;

        while (read < size)
        {
            int r = await Stream.ReadAsync(buffer, read, size - read);
            if (r == 0)
                throw new SocketException();

            read += r;
        }
    }
    public void SendPacket(Packet packet)
    {
        SendQueue.Enqueue(packet);
    }

    private void FlushSendQueue()
    {
        if (!IsConnected) return;

        while (SendQueue.TryDequeue(out var packet))
        {
            if (!packet.TryGetPayload(out var payload, out int payloadLength))
            {
                PacketPool.Return(packet);
                continue;
            }

            int bodySize = 1 + payloadLength;

            byte[] body = BufferPool.Rent(bodySize);
            byte[] lengthBuf = BufferPool.Rent(4);

            try
            {
                body[0] = (byte)packet.Type;

                if (payloadLength > 0)
                    Buffer.BlockCopy(payload, 0, body, 1, payloadLength);

                BitConverter.TryWriteBytes(new Span<byte>(lengthBuf, 0, 4),bodySize);
                Stream.Write(lengthBuf, 0, 4);
                Stream.Write(body, 0, bodySize);
            }
            finally
            {
                BufferPool.Return(body);
                BufferPool.Return(lengthBuf);
                PacketPool.Return(packet);
            }
        }
    }

    private void ProcessPackets()
    {
        while (ReceiveQueue.TryDequeue(out var packet))
        {
            try
            {
                if (PacketHandlers.TryGetValue(packet.Type, out var handler))
                {
                    handler(packet);
                }
                else
                {
                    Debug.LogWarning($"[Client] No handler for {packet.Type}");
                }
            }
            finally
            {
                PacketPool.Return(packet);
            }
        }
    }

    private void RegisterHandlers()
    {
        PacketHandlers[PacketType.UserLoginResult] = HandleLoginResult;
        PacketHandlers[PacketType.UserDuplicateLogin] = HandleDuplicateLogin;
        PacketHandlers[PacketType.UserRegistResult] = HandleRegistResult;
        PacketHandlers[PacketType.UserMatchingStatus] = HandleMatchingStatus;
        PacketHandlers[PacketType.UserMatchingResult] = HandleMatchingResult;
        PacketHandlers[PacketType.UserCharacterSelectSync] = HandleCharacterSelectSync;
        PacketHandlers[PacketType.UserGameStart] = HandleGameStart;
        PacketHandlers[PacketType.UserPositionSync] = HandlePositionSync;
    }
    public static event Action<int, string ,int, int> OnLoginResult;
    public static event Action OnDuplicateLogin;

    public void HandleDuplicateLogin(Packet packet)
    {
        int reason = packet.ReadInt();
        if (reason != 1)
            return;

        Debug.Log("[Client] Duplicate login — session replaced.");
        OnDuplicateLogin?.Invoke();
    }

    public void HandleLoginResult(Packet packet)
    {
        int result = packet.ReadInt();
        string nickName = packet.ReadString();
        int Money = packet.ReadInt();
        int MMR = packet.ReadInt();
        Debug.Log($"[Client] Login result = {result}, nickName = {nickName} Money = {Money} , MMR = {MMR}");
        OnLoginResult?.Invoke(result, nickName, Money,MMR);
    }

    public static event Action<int> OnRegistResult;
    public void HandleRegistResult(Packet packet)
    {
        int result = packet.ReadInt();
        Debug.Log($"[Client] Regist result = {result}");
        OnRegistResult?.Invoke(result);
    }

    // 씬 전환 직후 UI가 한 박자 늦게 붙을 때 매칭 인원 표시용
    public static int CachedMatchingCurrent { get; private set; }
    public static int CachedMatchingRequired { get; private set; }
    public static bool HasCachedMatchingStatus { get; private set; }

    public static event Action<int, int> OnMatchingStatus;
    public void HandleMatchingStatus(Packet packet)
    {
        int current = packet.ReadInt();
        int required = packet.ReadInt();
        CachedMatchingCurrent = current;
        CachedMatchingRequired = required;
        HasCachedMatchingStatus = true;
        OnMatchingStatus?.Invoke(current, required);
    }

    public static void ClearMatchingStatusCache()
    {
        HasCachedMatchingStatus = false;
        CachedMatchingCurrent = 0;
        CachedMatchingRequired = 0;
    }

    public static event Action<int, int, GameType> OnMatchingResult;
    public void HandleMatchingResult(Packet packet)
    {
        int result = packet.ReadInt();
        if (result != 1)
            return;

        int roomId = packet.ReadInt();
        var matchType = (GameType)packet.ReadInt();

        ClientMatchState.Instance?.SetMatched(roomId, matchType);
        OnMatchingResult?.Invoke(result, roomId, matchType);
    }

    private static readonly List<CharacterSelectEntry> CachedCharacterSync = new();
    public static bool HasCachedCharacterSync { get; private set; }

    public static IReadOnlyList<CharacterSelectEntry> GetCachedCharacterSelectEntries() => CachedCharacterSync;

    public static void ClearCharacterSelectCache()
    {
        CachedCharacterSync.Clear();
        HasCachedCharacterSync = false;
    }

    public static event Action<IReadOnlyList<CharacterSelectEntry>> OnCharacterSelectSync;

    public void HandleCharacterSelectSync(Packet packet)
    {
        CachedCharacterSync.Clear();
        int count = packet.ReadInt();
        for (int i = 0; i < count; i++)
        {
            int uid = packet.ReadInt();
            var character = (PlayableCharacter)packet.ReadInt();
            packet.ReadBool();
            CachedCharacterSync.Add(new CharacterSelectEntry(uid, character));
        }

        HasCachedCharacterSync = true;
        OnCharacterSelectSync?.Invoke(CachedCharacterSync);
    }

    private static GameStartInfo CachedGameStart = new();
    public static bool HasCachedGameStart { get; private set; }

    public static GameStartInfo GetCachedGameStart() => CachedGameStart;

    public static void ClearGameStartCache()
    {
        HasCachedGameStart = false;
        CachedGameStart = new GameStartInfo();
    }

    public static event Action<GameStartInfo> OnGameStart;
    public static event Action<int, Vector3> OnPositionSync;

    public void HandleGameStart(Packet packet)
    {
        int result = packet.ReadInt();
        if (result != 1)
            return;

        var info = new GameStartInfo
        {
            RoomId = packet.ReadInt(),
            PlayerCount = packet.ReadInt()
        };

        for (int i = 0; i < info.PlayerCount; i++)
        {
            int uid = packet.ReadInt();
            float x = packet.ReadFloat();
            float y = packet.ReadFloat();
            float z = packet.ReadFloat();
            info.Players.Add(new GameStartInfo.GameStartPlayerSpawn
            {
                Uid = uid,
                Position = new Vector3(x, y, z)
            });
        }

        CachedGameStart = info;
        HasCachedGameStart = true;
        OnGameStart?.Invoke(info);
    }

    public void HandlePositionSync(Packet packet)
    {
        int uid = packet.ReadInt();
        float x = packet.ReadFloat();
        float y = packet.ReadFloat();
        float z = packet.ReadFloat();
        OnPositionSync?.Invoke(uid, new Vector3(x, y, z));
    }
}