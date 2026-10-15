using MobaTypes;
using System.Threading.Tasks;
using UnityEngine;

public class ServerBootsStrap : MonoBehaviour
{
    private Moba_Server _server;
    private bool _isShuttingDown;

    void Awake()
    {
        Application.runInBackground = true; // 에디터에서 ServerScene 포커스 잃어도 Tick 돌게
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Application.wantsToQuit += OnWantsToQuit;

        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            ServerLog.Info("[Server] Running without graphics device (headless).");
    }

    void Start()
    {
        Debug.Log($"[Server] version {MobaVersion.Server} (build check)");
        _server = new Moba_Server();
        _server.Initialize();
    }

    void Update()
    {
        _server?.Tick(Time.deltaTime);
    }

    private bool OnWantsToQuit()
    {
        if (_isShuttingDown)
            return true;

        _isShuttingDown = true;
        _ = ShutdownAsync();
        return false;
    }

    private async Task ShutdownAsync()
    {
        Debug.Log("서버 종료 시작");

        if (_server != null)
            await _server.ShutdownAsync();

        Debug.Log("서버 종료 완료");
        Application.Quit();
    }
}
