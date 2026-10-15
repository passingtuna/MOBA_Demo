using MobaTypes;
using UnityEngine;
using UnityEngine.SceneManagement;

// 중복 로그인 같은 건 카운트 기다릴 필요 없이 즉시 Login
public class ClientSessionGuard : MonoBehaviour
{
    private void Awake()
    {
        if (GetComponent<ClientPacketManager>() == null)
            return;

        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        ClientPacketManager.OnDuplicateLogin += HandleDuplicateLogin;
    }

    private void OnDisable()
    {
        ClientPacketManager.OnDuplicateLogin -= HandleDuplicateLogin;
    }

    private void HandleDuplicateLogin()
    {
        // LobbyUIAction.ReturnToLogin이랑 정리 내용 맞춰둠
        ClientMatchState.Instance?.Clear();
        UserManager.Instance?.ClearLocalSession();
        LobbyDisconnectPolicy.Reset();
        ClientPacketManager.Instance?.SetGameLowLatency(false);
        ClientPacketManager.Instance?.Disconnect();

        if (UI_Manager.Instance != null)
        {
            var canvas = FindFirstObjectByType<Canvas>();
            var root = canvas != null ? canvas.gameObject : gameObject;
            var popup = UI_Manager.Instance.UIPopUp<MessagePopUpUI>(root);
            popup?.SetText("다른 곳에서 동일 계정으로 로그인되어 연결을 종료합니다.");
        }

        SceneManager.LoadScene(MobaScenes.Login);
    }
}
