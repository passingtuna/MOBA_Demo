using MobaTypes;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// 클라이언트당 NetworkSession(TCP) + UID당 UserSession(게임 상태) 분리
public class PacketManager
{
    public static PacketManager Instance;
    private readonly ConcurrentQueue<ReceiveJob> ReceiveQueue = new();
    private readonly ConcurrentQueue<SendJob> SendQueue = new();
    private readonly Dictionary<PacketType, Func<ReceiveJob, Task>> PacketHandlers = new();
    private NetworkStream Stream;
    private LoginService LoginService;
    private GameService GameService;
    private MatchService MatchService;

    public ConcurrentDictionary<int, NetworkSession> NetworkSessions = new();
    public ConcurrentDictionary<int, UserSession> UserSessions = new();

    class SendJob
    {
        public NetworkSession Session;
        public Packet Packet;

        public SendJob(NetworkSession session, Packet packet)
        {
            Session = session;
            Packet = packet;
        }
    }
    class ReceiveJob
    {
        public NetworkSession Session;
        public Packet Packet;
    }

    public class NetworkSession
    {
        public Socket Socket;
        public int ClientId; // TCP 연결 식별 (로그인 전에도 존재)
        public int UID;      // Login 성공 후 바인딩
        public bool IsConnected;

        public void SetGameLowLatency(bool enabled)
        {
            MobaTcpTraffic.SetNoDelay(Socket, enabled);
        }

        public void Disconnect()
        {
            if (!IsConnected)return;
            IsConnected = false;
            try
            {
                if (UID > 0 && Instance.UserSessions.TryGetValue(UID, out var userSession))
                {
                    userSession.OnSessionDisconnected(this);
                }

                Instance.NetworkSessions.TryRemove(ClientId, out _);
                try
                {
                    Socket.Shutdown(SocketShutdown.Both);
                }
                catch { }

                Socket.Close();
                Socket.Dispose();
            }
            catch (Exception e)
            {
                ServerLog.Error($"Disconnect Error: {e}");
            }
        }
    }

    public class UserSession
    {
        public int UID;
        public SessionState SessionState;
        public GameRoom ConnectedGameroom;
        public DateTime DisconnectTime;
        public NetworkSession Connection { get; private set; }
        public void BindConnection(NetworkSession session)
        {
            Connection = session;
            session.UID = UID;
            SessionState = SessionState.LoggedIn;
        }
        public UserSession(int uid)
        {
            UID = uid;
        }

        public void OnSessionDisconnected(NetworkSession session)
        {
            lock(this)
            {
                if (Connection != session) return;
                Connection = null;
                if (SessionState == SessionState.InGame)
                {
                    //ConnectedGameroom?.OnPlayerDisconnected(this);
                    SessionState = SessionState.LoggedIn;
                }
                else
                {
                    SessionState = SessionState.LoggedIn;
                }
                DisconnectTime = DateTime.UtcNow;
            }

        }
    }

    public PacketManager(LoginService loginService, GameService gameService, MatchService  matchService)
    {
        LoginService = loginService;
        GameService  = gameService;
        MatchService = matchService;
        Instance = this;
        RegisterHandlers();
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
    public void RemoveUserSession(int uid)
    {
        if (UserSessions.TryRemove(uid, out var userSession))
        {
            UserManager.Instance.LogOutUser(uid);
        }
    }
    public void Update()
    {
        ProcessPackets();
        UpdateDisconnectedSessions();
    }

    // 끊긴 UID 메모리 정리 (SessionState.Disconnected + Connection null 일 때)
    private void UpdateDisconnectedSessions()
    {
        var now = DateTime.UtcNow;

        foreach (var pair in UserSessions)
        {
            var userSession = pair.Value;

            if (userSession.Connection == null &&
                userSession.SessionState == SessionState.Disconnected &&
                now - userSession.DisconnectTime > TimeSpan.FromSeconds(30))
            {
                RemoveUserSession(userSession.UID);
            }
        }
    }

    private void RegisterHandlers()
    {
        PacketHandlers[PacketType.UserLoginReq] = HandleLoginReq;
        PacketHandlers[PacketType.UserRegistReq] = HandleRegistReq;
        PacketHandlers[PacketType.UserMatchingReq] = HandleMatchingReq;
        PacketHandlers[PacketType.UserCharacterSelectReq] = HandleCharacterSelectReq;
        PacketHandlers[PacketType.UserPositionReq] = HandlePositionReq;
        PacketHandlers[PacketType.UserLeaveRoomReq] = HandleLeaveRoomReq;
    }

    public void ReceivePacket(NetworkSession session , Packet packet)
    {
        ReceiveJob receivejob = new ReceiveJob();
        receivejob.Session = session; 
        receivejob.Packet = packet;

        ReceiveQueue.Enqueue(receivejob);
    }

    public void SendPacketToUser(int uid, Packet packet)
    {
        if (!UserSessions.TryGetValue(uid, out var userSession) ||
            userSession.Connection == null ||
            !userSession.Connection.IsConnected)
        {
            packet.Dispose();
            return;
        }

        SendToSession(userSession.Connection, packet);
    }

    public void SendMatchingStatus(int uid, int currentCount, int requiredCount)
    {
        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserMatchingStatus;
        packet.Write(currentCount);
        packet.Write(requiredCount);
        SendPacketToUser(uid, packet);
    }

    public void NotifyMatchSuccess(int uid, GameRoom room)
    {
        if (!UserSessions.TryGetValue(uid, out var userSession))
            return;

        userSession.ConnectedGameroom = room;

        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserMatchingResult;
        packet.Write(1);
        packet.Write(room.RoomId);
        packet.Write((int)room.MatchType);
        SendPacketToUser(uid, packet);
    }

    // 새 로그인 들어오기 전에 기존 소켓에 알림 → 클라 ClientSessionGuard가 Login 씬으로
    public void NotifyDuplicateLogin(NetworkSession session)
    {
        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserDuplicateLogin;
        packet.Write(1);
        SendToSession(session, packet);
    }

    public void SendToSession(NetworkSession session, Packet packet)
    {
        if (session == null || !session.IsConnected)
        {
            packet.Dispose();
            return;
        }

        SendQueue.Enqueue(new SendJob(session, packet));
    }

    public void FlushSendQueue()
    {
        while (SendQueue.TryDequeue(out var sendjob))
        {
            var session = sendjob.Session;
            var packet = sendjob.Packet;

            if (session == null || !session.IsConnected || !packet.TryGetPayload(out var payload, out int payloadLength))
            {
                packet.Reset();
                PacketPool.Return(packet);
                continue;
            }

            int bodyLength = 1 + payloadLength;
            int totalLength = 4 + bodyLength;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(totalLength);

            try
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), bodyLength);
                buffer[4] = (byte)packet.Type;
                if (payloadLength > 0)
                    Buffer.BlockCopy(payload, 0, buffer, 5, payloadLength);

                int sent = 0;
                while (sent < totalLength)
                    sent += session.Socket.Send(buffer, sent, totalLength - sent, SocketFlags.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"Send 실패 Client {session.ClientId}: {e}");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                packet.Reset();
                PacketPool.Return(packet);
            }
        }
    }

    public void ProcessPackets()
    {
        while (ReceiveQueue.TryDequeue(out var receiveJob))
        {
#pragma warning disable CS4014
            _ = DispatchReceiveJobAsync(receiveJob);
#pragma warning restore CS4014
        }
    }

    private async Task DispatchReceiveJobAsync(ReceiveJob receiveJob)
    {
        try
        {
            if (PacketHandlers.TryGetValue(receiveJob.Packet.Type, out var handler))
                await handler(receiveJob);
            else
                Debug.LogWarning($"[Packet] No handler for {receiveJob.Packet.Type}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Packet] Handler error: {ex}");
        }
        finally
        {
            PacketPool.Return(receiveJob.Packet);
        }
    }


    private async Task HandleLoginReq(ReceiveJob receive)
    {
        await LoginService.LoginProcessAsync(receive.Session,receive.Packet);
    }

    private async Task HandleRegistReq(ReceiveJob receive)
    {
        await LoginService.RegistProcessAsync(receive.Session, receive.Packet);
    }

    private Task HandleMatchingReq(ReceiveJob receive)
    {
        if (receive.Session.UID <= 0)
        {
            Debug.LogWarning("[Packet] Matching request before login");
            return Task.CompletedTask;
        }

        if (UserSessions.TryGetValue(receive.Session.UID, out var userSession) &&
            userSession.SessionState == SessionState.InGame)
        {
            Debug.LogWarning("[Packet] Matching request while already in game");
            return Task.CompletedTask;
        }

        if (UserSessions.TryGetValue(receive.Session.UID, out userSession) &&
            userSession.ConnectedGameroom != null)
        {
            Debug.LogWarning("[Packet] Matching request while in a room");
            return Task.CompletedTask;
        }

        MatchService.HandleMatchingRequest(receive.Session.UID, receive.Packet);
        return Task.CompletedTask;
    }

    private Task HandleCharacterSelectReq(ReceiveJob receive)
    {
        if (receive.Session.UID <= 0)
            return Task.CompletedTask;

        if (!UserSessions.TryGetValue(receive.Session.UID, out var userSession) ||
            userSession.ConnectedGameroom == null)
        {
            Debug.LogWarning("[Packet] Character select without room");
            return Task.CompletedTask;
        }

        int characterValue = receive.Packet.ReadInt();
        if (!System.Enum.IsDefined(typeof(PlayableCharacter), characterValue))
            return Task.CompletedTask;

        var character = (PlayableCharacter)characterValue;
        userSession.ConnectedGameroom.TryLockCharacter(receive.Session.UID, character);
        return Task.CompletedTask;
    }

    public void SendCharacterSelectSync(int uid, GameRoom room)
    {
        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserCharacterSelectSync;
        room.FillSyncPacket(packet);
        SendPacketToUser(uid, packet);
    }

    public void NotifyGameStart(int uid, GameRoom room)
    {
        // 인게임 들어갈 때부터 NoDelay (위치 패킷 지연 줄이려고)
        if (UserSessions.TryGetValue(uid, out var userSession) &&
            userSession.Connection != null)
        {
            userSession.Connection.SetGameLowLatency(true);
        }

        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserGameStart;
        room.FillGameStartPacket(packet);
        SendPacketToUser(uid, packet);
    }

    public void SendPositionSync(int uid, int subjectUid, Vector3 position)
    {
        Packet packet = PacketPool.Rent();
        packet.Type = PacketType.UserPositionSync;
        packet.Write(subjectUid);
        packet.Write(position.x);
        packet.Write(position.y);
        packet.Write(position.z);
        SendPacketToUser(uid, packet);
    }

    private Task HandleLeaveRoomReq(ReceiveJob receive)
    {
        if (receive.Session.UID <= 0)
            return Task.CompletedTask;

        if (!UserSessions.TryGetValue(receive.Session.UID, out var userSession))
            return Task.CompletedTask;

        var room = userSession.ConnectedGameroom;
        if (room != null)
            GameService.RemovePlayerFromRoom(room, receive.Session.UID);

        userSession.SessionState = SessionState.LoggedIn;
        userSession.ConnectedGameroom = null;
        receive.Session.SetGameLowLatency(false);
        return Task.CompletedTask;
    }

    private Task HandlePositionReq(ReceiveJob receive)
    {
        if (receive.Session.UID <= 0)
            return Task.CompletedTask;

        if (!UserSessions.TryGetValue(receive.Session.UID, out var userSession) ||
            userSession.ConnectedGameroom == null ||
            userSession.SessionState != SessionState.InGame)
        {
            return Task.CompletedTask;
        }

        float x = receive.Packet.ReadFloat();
        float y = receive.Packet.ReadFloat();
        float z = receive.Packet.ReadFloat();
        var position = new Vector3(x, y, z);

        receive.Session.SetGameLowLatency(true);

        var room = userSession.ConnectedGameroom;
        room.UpdatePlayerPosition(receive.Session.UID, position);
        room.BroadcastPosition(receive.Session.UID);
        return Task.CompletedTask;
    }

    public void OutUserGameRoom(int uid, GameRoom room)
    {
        if (UserSessions.TryGetValue(uid, out UserSession user))
        {
            user.SessionState = SessionState.LoggedIn;
            user.ConnectedGameroom = null;
            user.Connection?.SetGameLowLatency(false);
        }
    }
}