using MobaTypes;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static PacketManager;

// Unity ServerScene 한 프로세스: TCP accept + DB 워커 + Tick에서 매칭/패킷 flush
public class Moba_Server
{
    private TcpListener ServerListener;
    private bool IsRunning;
    private const int SAVE_INTERVAL = 30000; // ms — Dirty UserData 주기 저장

    private LoginService _LoginService;
    private GameService _GameService;
    private MatchService _MatchService;
    private long _lastSaveTime;

    public void Initialize()
    {
        ServerListener = new TcpListener(IPAddress.Parse(MobaNetwork.DefaultHost), MobaNetwork.DefaultPort);
        ServerListener.Start();
        IsRunning = true;

        _LoginService = new();
        _GameService = new();
        _MatchService = new(_GameService);
        new PacketManager(_LoginService, _GameService, _MatchService);
        new UserManager();
        new DBManager();

        _lastSaveTime = Environment.TickCount;

#pragma warning disable CS4014
        _ = AcceptClientLoop();
        _ = DBWorkerLoop();
#pragma warning restore CS4014

        ServerLog.Info("========================================");
        ServerLog.Info($" MOBA_Demo Server  version {MobaVersion.Server}");
        ServerLog.Info($" Listen {MobaNetwork.DefaultHost}:{MobaNetwork.DefaultPort}");
        ServerLog.Info("========================================");
    }

    public void Tick(float deltaTime)
    {
        if (!IsRunning)
            return;

        PacketManager.Instance.Update(); // 수신 큐 → 핸들러 (async)
        _MatchService.RunMatchingAlgorithm();
        _GameService.UpdateAllRooms(deltaTime);
        UpdateSchedulers();
        PacketManager.Instance.FlushSendQueue();
    }

    private void UpdateSchedulers()
    {
        long now = Environment.TickCount;

        if (now - _lastSaveTime > SAVE_INTERVAL)
        {
            UserManager.Instance.SaveAllDirtyUsers();
            _lastSaveTime = now;
        }
    }

    private async Task AcceptClientLoop()
    {
        while (IsRunning)
        {
            var client = await ServerListener.AcceptTcpClientAsync();
#pragma warning disable CS4014
            _ = HandleClientAsync(client);
#pragma warning restore CS4014
        }
    }

    private async Task DBWorkerLoop()
    {
        while (IsRunning)
        {
            if (DBManager.Instance.SendQueue.TryDequeue(out var data))
#pragma warning disable CS4014
                _ = ProcessSingleDBAsync(data);
#pragma warning restore CS4014
            else
                await Task.Delay(1);
        }
    }

    private async Task ProcessSingleDBAsync(DBManager.SQLData data)
    {
        try
        {
            var result = await DBManager.Instance.ExecuteStoredProcedureAsync(data);
            data.Tcs?.SetResult(result);
        }
        catch (Exception e)
        {
            data.Tcs?.SetException(e);
            ServerLog.Error($"DB Worker Error: {e}");
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        int clientId = AllocateClientId(client);

        var session = new NetworkSession()
        {
            ClientId = clientId,
            Socket = client.Client,
            IsConnected = true
        };
        PacketManager.Instance.NetworkSessions.TryAdd(clientId, session);
        ServerLog.Info($"Client {clientId} connected");

        byte[] lengthBuf = new byte[4];

        try
        {
            while (IsRunning)
            {
                await ReadExactAsync(stream, lengthBuf, 4);
                int length = BitConverter.ToInt32(lengthBuf, 0);

                if (length <= 0 || length > 65535)
                    throw new Exception("Invalid packet length");

                byte[] packetBuf = ArrayPool<byte>.Shared.Rent(length);
                Packet packet = PacketPool.Rent();

                try
                {
                    await ReadExactAsync(stream, packetBuf, length);
                    PacketType type = (PacketType)packetBuf[0];
                    packet.Type = type;
                    packet.SetData(packetBuf, length, fromPool: true);
                    packet.SetReadPos(1);

                    PacketManager.Instance.ReceivePacket(session, packet);
                }
                catch
                {
                    packet.Reset();
                    PacketPool.Return(packet);
                    throw;
                }
            }
        }
        catch
        {
            ServerLog.Info($"Client {clientId} disconnected");
        }
        finally
        {
            session.Disconnect();
            client.Close();
            ReleaseClientId(clientId);
        }
    }

    private int _nextClientId = 1;
    private ConcurrentDictionary<TcpClient, int> _clients = new();

    private int AllocateClientId(TcpClient client)
    {
        int id = Interlocked.Increment(ref _nextClientId);
        _clients[client] = id;
        return id;
    }

    private async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int size)
    {
        int read = 0;
        while (read < size)
        {
            int r = await stream.ReadAsync(buffer, read, size - read);
            if (r == 0)
                throw new SocketException();
            read += r;
        }
    }

    private void ReleaseClientId(int clientId)
    {
        foreach (var kv in _clients)
        {
            if (kv.Value == clientId)
            {
                _clients.TryRemove(kv.Key, out _);
                break;
            }
        }
    }

    public async Task ShutdownAsync()
    {
        ServerLog.Info("Server shutting down");
        IsRunning = false;
        UserManager.Instance.SaveAllDirtyUsers();
        await DBManager.Instance.FlushAllPending();
        ServerListener?.Stop();
    }
}
