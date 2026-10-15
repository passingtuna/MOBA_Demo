using MobaTypes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchingQueuePopUpUI : UIBase
{
    [SerializeField] private GameType matchType = GameType.Practice; // Lobby에서 SetMatchType

    public TMP_Text TimeText;
    public TMP_Text PeopleText;

    private float matchStartTime = -1f;
    private int lastSecond = -1;
    private int requiredCount = 1;
    private int displayCurrent;

    private void Awake()
    {
        CacheTextReferences();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        CacheTextReferences();
        ApplyCachedMatchingStatus();
        RefreshTimerText();
    }

    private void OnEnable()
    {
        requiredCount = MobaMatchRules.GetRequiredPlayerCount(matchType);
        displayCurrent = 0;
        UpdatePeopleText(displayCurrent, requiredCount);

        ClientPacketManager.OnMatchingStatus += HandleMatchingStatus;
        ClientPacketManager.OnMatchingResult += HandleMatchingSuccess;
        ClientPacketManager.OnDisconnected += HandleServerDisconnected;
        ApplyCachedMatchingStatus();
        RefreshTimerText();
    }

    private void OnDisable()
    {
        ClientPacketManager.OnMatchingStatus -= HandleMatchingStatus;
        ClientPacketManager.OnMatchingResult -= HandleMatchingSuccess;
        ClientPacketManager.OnDisconnected -= HandleServerDisconnected;
    }

    private void Update()
    {
        if (matchStartTime < 0)
            return;

        RefreshTimerText();
    }

    public void SetMatchType(GameType type)
    {
        matchType = type;
        requiredCount = MobaMatchRules.GetRequiredPlayerCount(matchType);
        displayCurrent = 0;
        UpdatePeopleText(displayCurrent, requiredCount);
    }

    public void SetMatchingStart(bool start)
    {
        CacheTextReferences();

        if (start)
        {
            if (ClientPacketManager.Instance == null ||
                !ClientPacketManager.Instance.IsConnected ||
                !ClientPacketManager.Instance.HasServerSession)
            {
                Debug.LogWarning("[Match] Not connected to server.");
                LobbyDisconnectPolicy.NoteConnectionFailure(); // 로비 쪽에서 같이 카운트
                return;
            }

            ClientMatchState.Instance?.Clear();
            ClientPacketManager.ClearMatchingStatusCache();

            matchStartTime = Time.time;
            lastSecond = -1;
            requiredCount = MobaMatchRules.GetRequiredPlayerCount(matchType);
            displayCurrent = 0;
            UpdatePeopleText(displayCurrent, requiredCount);
            RefreshTimerText();

            Packet sentPacket = new Packet();
            sentPacket.Type = PacketType.UserMatchingReq;
            sentPacket.Write(true);
            sentPacket.Write((int)matchType);
            ClientPacketManager.Instance.SendPacket(sentPacket);
        }
        else
        {
            matchStartTime = -1;
            SendCancelRequest();
            ClientPacketManager.ClearMatchingStatusCache();
            UI_Manager.Instance.PopUi();
        }
    }

    private void SendCancelRequest()
    {
        if (!ClientPacketManager.Instance.IsConnected)
            return;

        Packet sentPacket = new Packet();
        sentPacket.Type = PacketType.UserMatchingReq;
        sentPacket.Write(false);
        sentPacket.Write((int)matchType);
        ClientPacketManager.Instance.SendPacket(sentPacket);
    }

    private void HandleMatchingStatus(int current, int required)
    {
        requiredCount = required;
        displayCurrent = current;
        UpdatePeopleText(displayCurrent, requiredCount);
    }

    private void ApplyCachedMatchingStatus()
    {
        if (!ClientPacketManager.HasCachedMatchingStatus)
            return;

        HandleMatchingStatus(
            ClientPacketManager.CachedMatchingCurrent,
            ClientPacketManager.CachedMatchingRequired);
    }

    private void UpdatePeopleText(int current, int required)
    {
        if (PeopleText == null)
            return;

        PeopleText.text = $"{current}/{required}";
    }

    private void RefreshTimerText()
    {
        if (TimeText == null || matchStartTime < 0)
            return;

        float elapsed = Time.time - matchStartTime;
        int totalSeconds = Mathf.Max(0, (int)elapsed);

        if (totalSeconds == lastSecond)
            return;

        lastSecond = totalSeconds;
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        TimeText.text = $"{minutes:00}:{seconds:00}";
    }

    private void CacheTextReferences()
    {
        if (TimeText == null)
        {
            var timeTransform = transform.Find("MatchingTime");
            if (timeTransform != null)
                TimeText = timeTransform.GetComponent<TMP_Text>();
        }

        if (PeopleText == null)
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (text == TimeText)
                    continue;
                PeopleText = text;
                break;
            }
        }
    }

    private void HandleMatchingSuccess(int result, int roomId, GameType matchedType)
    {
        if (result != 1)
            return;

        matchStartTime = -1;
        ClientPacketManager.ClearMatchingStatusCache();
        ClientPacketManager.OnMatchingResult -= HandleMatchingSuccess;
        ClientPacketManager.OnMatchingStatus -= HandleMatchingStatus;

        UI_Manager.Instance?.PopUi();
        SceneManager.LoadScene(MobaScenes.Main); // 서버는 이미 UserGameStart 보낸 상태
    }

    public void PressCancelButton()
    {
        SendCancelRequest();
        matchStartTime = -1;
        ClientPacketManager.ClearMatchingStatusCache();
        OnClose();
        UI_Manager.Instance?.PopUi();
    }

    private void HandleServerDisconnected()
    {
        if (matchStartTime < 0)
            return;

        // 끊김 카운트는 LobbyUIAction이 처리. 여기선 팝업만 닫음
        matchStartTime = -1;
        ClientPacketManager.ClearMatchingStatusCache();
        UI_Manager.Instance?.PopUi();
    }

}
