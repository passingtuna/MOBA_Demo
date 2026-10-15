using MobaTypes;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// MainScene: UserGameStart 수신 → 플레이어 프리팹 스폰 + 위치 동기화
public class GameSessionBootstrap : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    private Transform _spawnRoot;

    private void Awake()
    {
        _spawnRoot = new GameObject("SpawnedPlayers").transform;
        HideScenePlaceholderPlayers(); // 씬에 박아둔 테스트용 Player는 끔

        if (GetComponent<InGameSessionUI>() == null)
            gameObject.AddComponent<InGameSessionUI>();
    }

    private void OnEnable()
    {
        ClientPacketManager.OnGameStart += HandleGameStart;
        ClientPacketManager.OnPositionSync += HandlePositionSync;
    }

    private void OnDisable()
    {
        ClientPacketManager.OnGameStart -= HandleGameStart;
        ClientPacketManager.OnPositionSync -= HandlePositionSync;
    }

    private void Start()
    {
        // 매칭 없이 Main 들어온 경우 (직접 씬 실행 등)
        if (!ClientMatchState.Instance?.HasActiveMatch ?? true)
        {
            SceneManager.LoadScene(MobaScenes.Lobby);
            return;
        }

        if (playerPrefab == null)
            playerPrefab = Resources.Load<GameObject>("MobaPlayer");

        // 패킷이 씬 로드보다 먼저 왔을 때
        if (ClientPacketManager.HasCachedGameStart)
            HandleGameStart(ClientPacketManager.GetCachedGameStart());
    }

    private void HandleGameStart(GameStartInfo info)
    {
        ClientPacketManager.Instance?.SetGameLowLatency(true);

        if (playerPrefab == null)
        {
            Debug.LogError("[Game] playerPrefab is not assigned.");
            return;
        }

        int myUid = UserManager.Instance?.GetUserData()?.GetUid() ?? 0;

        foreach (Transform child in _spawnRoot)
            Destroy(child.gameObject);

        CameraControl cameraControl = FindFirstObjectByType<CameraControl>();

        foreach (var spawn in info.Players)
        {
            bool isLocal = spawn.Uid == myUid;
            Vector3 spawnPos = ResolveSpawnPosition(spawn.Position);
            var instance = Instantiate(playerPrefab, spawnPos, Quaternion.identity, _spawnRoot);
            instance.name = isLocal ? $"Player_Local_{spawn.Uid}" : $"Player_Remote_{spawn.Uid}";

            var network = instance.GetComponent<NetworkPlayerController>();
            if (network == null)
                network = instance.AddComponent<NetworkPlayerController>();
            network.Initialize(spawn.Uid, isLocal);

            if (isLocal && cameraControl != null)
            {
                var localInput = instance.GetComponent<LocalPlayerInputController>();
                cameraControl.BindLocalPlayer(instance.transform, localInput);
            }
        }

        Debug.Log($"[Game] Spawned {info.Players.Count} players (room {info.RoomId}, myUid={myUid})");
    }

    private void HandlePositionSync(int uid, Vector3 position)
    {
        foreach (var controller in _spawnRoot.GetComponentsInChildren<NetworkPlayerController>())
        {
            if (controller.Uid != uid || controller.IsLocal)
                continue;
            controller.SetRemotePosition(position);
            return;
        }
    }

    private static Vector3 ResolveSpawnPosition(Vector3 requested)
    {
        if (NavMesh.SamplePosition(requested, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            return hit.position;

        return requested;
    }

    private static void HideScenePlaceholderPlayers()
    {
        foreach (var input in FindObjectsByType<LocalPlayerInputController>(FindObjectsSortMode.None))
            input.gameObject.SetActive(false);
    }
}
