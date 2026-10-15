using MobaTypes;
using UnityEngine;
using UnityEngine.SceneManagement;

// DontDestroyOnLoad — 매칭 성공 ~ Main 진입까지 roomId/type 보관
public class ClientMatchState : MonoBehaviour
{
    public static ClientMatchState Instance { get; private set; }

    public bool HasActiveMatch { get; private set; }
    public int RoomId { get; private set; }
    public GameType MatchType { get; private set; }
    public PlayableCharacter SelectedCharacter { get; private set; } = PlayableCharacter.None;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Build Settings에서 Main만 단독 Play 하는 실수 방지
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == MobaScenes.Main && !HasActiveMatch)
            SceneManager.LoadScene(MobaScenes.Lobby);
        else if (scene.name == MobaScenes.CharacterSelect && !HasActiveMatch)
            SceneManager.LoadScene(MobaScenes.Lobby);
    }

    public void SetMatched(int roomId, GameType matchType)
    {
        HasActiveMatch = true;
        RoomId = roomId;
        MatchType = matchType;
    }

    public void SetSelectedCharacter(PlayableCharacter character)
    {
        SelectedCharacter = character;
    }

    public void Clear()
    {
        HasActiveMatch = false;
        RoomId = 0;
        MatchType = GameType.Practice;
        SelectedCharacter = PlayableCharacter.None;
        ClientPacketManager.ClearCharacterSelectCache();
        ClientPacketManager.ClearGameStartCache();
        ClientPacketManager.Instance?.SetGameLowLatency(false);
    }
}
