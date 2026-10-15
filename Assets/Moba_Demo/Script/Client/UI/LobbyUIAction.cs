using MobaTypes;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUIAction : MonoBehaviour
{
    public TMP_Text NickNameText;
    public TMP_Text MoneyText;

    [SerializeField] private Button[] LobbyActionButtons;

    // LobbyScene에 LobbyUIAction이 두 개 (Canvas=닉/머니, 자식=버튼 OnClick). Login 전환은 Canvas 쪽만.
    private static bool s_loginRedirectInProgress;

    private bool _redirectingToLogin;

    private bool IsLobbyCoordinator => MoneyText != null;

    public void Start()
    {
        if (!IsLobbyCoordinator)
            return;

        s_loginRedirectInProgress = false;
        LobbyDisconnectPolicy.Reset();
        ClientMatchState.Instance?.Clear();

        var userData = UserManager.Instance?.GetUserData();
        if (userData == null)
        {
            // Login 거치지 않고 Lobby 온 경우 — 정책 카운트 없이 바로 돌려보냄
            ReturnToLogin(null);
            return;
        }

        NickNameText.text = userData.GetUserNickName();
        MoneyText.text = userData.GetMoney().ToString();

        EnsureLobbyActionButtonsCached();
        SetLobbyActionButtonsInteractable(true);

        if (!HasValidServerSession())
            LobbyDisconnectPolicy.NoteConnectionFailure();
    }

    private void OnEnable()
    {
        if (!IsLobbyCoordinator)
            return;

        ClientPacketManager.OnDisconnected += HandleServerDisconnected;
        LobbyDisconnectPolicy.OnBecameUnstable += HandleConnectionBecameUnstable;
        LobbyDisconnectPolicy.OnRedirectRequired += HandleRedirectRequired;
    }

    private void OnDisable()
    {
        if (!IsLobbyCoordinator)
            return;

        ClientPacketManager.OnDisconnected -= HandleServerDisconnected;
        LobbyDisconnectPolicy.OnBecameUnstable -= HandleConnectionBecameUnstable;
        LobbyDisconnectPolicy.OnRedirectRequired -= HandleRedirectRequired;
    }

    private void Update()
    {
        if (Keyboard.current?.f9Key.wasPressedThisFrame == true)
            PressSoloPracticeButton();

        if (!IsLobbyCoordinator || _redirectingToLogin || s_loginRedirectInProgress)
            return;

        LobbyDisconnectPolicy.Tick(); // MaxWait 체크용. 세션 폴링 아님

        if (LobbyDisconnectPolicy.ShouldBlockLobbyActions)
            SetLobbyActionButtonsInteractable(false);
    }

    private void HandleServerDisconnected()
    {
        LobbyDisconnectPolicy.NoteConnectionFailure();
    }

    private void HandleConnectionBecameUnstable()
    {
        SetLobbyActionButtonsInteractable(false);
        ShowMessage("서버 연결이 불안정합니다. 잠시 후 다시 로그인 화면으로 이동할 수 있습니다.");
    }

    private void HandleRedirectRequired()
    {
        ReturnToLogin("서버와의 연결이 끊어졌습니다. 다시 로그인해주세요.");
    }

    public void PressMatchingButton()
    {
        OpenMatchingPopup(GameType.OneVsOne);
    }

    public void PressSoloPracticeButton()
    {
        OpenMatchingPopup(GameType.Practice);
    }

    private void OpenMatchingPopup(GameType matchType)
    {
        if (_redirectingToLogin || LobbyDisconnectPolicy.ShouldBlockLobbyActions)
            return;

        if (UI_Manager.Instance == null)
        {
            Debug.LogError("[Lobby] UI_Manager missing.");
            return;
        }

        if (!HasValidServerSession())
        {
            LobbyDisconnectPolicy.NoteConnectionFailure();
            return;
        }

        var popupRoot = GetComponentInParent<Canvas>()?.gameObject ?? gameObject;
        MatchingQueuePopUpUI matchingPopup = UI_Manager.Instance.UIPopUp<MatchingQueuePopUpUI>(popupRoot);
        if (matchingPopup == null)
            return;

        matchingPopup.SetMatchType(matchType);
        matchingPopup.SetMatchingStart(true);
    }

    private static bool HasValidServerSession()
    {
        return ClientPacketManager.Instance != null &&
               ClientPacketManager.Instance.IsConnected &&
               ClientPacketManager.Instance.HasServerSession;
    }

    private void ReturnToLogin(string message)
    {
        if (_redirectingToLogin || s_loginRedirectInProgress)
            return;

        _redirectingToLogin = true;
        s_loginRedirectInProgress = true;

        LobbyDisconnectPolicy.Reset();
        ClientMatchState.Instance?.Clear();
        UserManager.Instance?.ClearLocalSession(); // UserData + 비번 캐시까지 싹
        ClientPacketManager.Instance?.SetGameLowLatency(false);
        ClientPacketManager.Instance?.Disconnect();

        if (!string.IsNullOrEmpty(message))
            ShowMessage(message);

        SceneManager.LoadScene(MobaScenes.Login);
    }

    private void ShowMessage(string text)
    {
        if (UI_Manager.Instance == null)
        {
            Debug.LogWarning($"[Lobby] {text}");
            return;
        }

        var popupRoot = GetComponentInParent<Canvas>()?.gameObject ?? gameObject;
        var popup = UI_Manager.Instance.UIPopUp<MessagePopUpUI>(popupRoot);
        popup?.SetText(text);
    }

    private void EnsureLobbyActionButtonsCached()
    {
        if (LobbyActionButtons != null && LobbyActionButtons.Length > 0)
            return;

        // Inspector에 안 묶어도 동작하게 — 씬 이름/OnClick 메서드 이름으로 찾음
        var buttons = new System.Collections.Generic.List<Button>();
        var matching = GameObject.Find("MatchingButton");
        if (matching != null && matching.TryGetComponent(out Button matchBtn))
            buttons.Add(matchBtn);

        var allButtons = FindObjectsByType<Button>(FindObjectsSortMode.None);
        foreach (var button in allButtons)
        {
            if (button == null || buttons.Contains(button))
                continue;

            int listenerCount = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < listenerCount; i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == nameof(PressSoloPracticeButton))
                {
                    buttons.Add(button);
                    break;
                }
            }
        }

        LobbyActionButtons = buttons.ToArray();
    }

    private void SetLobbyActionButtonsInteractable(bool interactable)
    {
        EnsureLobbyActionButtonsCached();
        if (LobbyActionButtons == null)
            return;

        foreach (var button in LobbyActionButtons)
        {
            if (button != null)
                button.interactable = interactable;
        }
    }
}
