using System.Collections.Generic;
using System.Text;
using MobaTypes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// CharacterSelectScene용 — MVP에선 매칭 후 Main 직행이라 당장은 거의 안 탐
public class CharacterSelectUI : MonoBehaviour
{
    private PlayableCharacter _pending = PlayableCharacter.None;
    private PlayableCharacter _confirmed = PlayableCharacter.None;
    private readonly Dictionary<int, PlayableCharacter> _roomPicks = new();

    private TMP_Text _titleText;
    private TMP_Text _statusText;
    private TMP_Text _hintText;
    private Button _gunnerButton;
    private Button _assassinButton;
    private Button _confirmButton;

    private void OnEnable()
    {
        ClientPacketManager.OnCharacterSelectSync += HandleSync;
        ClientPacketManager.OnGameStart += HandleGameStart;
        if (ClientPacketManager.HasCachedGameStart)
            HandleGameStart(ClientPacketManager.GetCachedGameStart());
    }

    private void OnDisable()
    {
        ClientPacketManager.OnCharacterSelectSync -= HandleSync;
        ClientPacketManager.OnGameStart -= HandleGameStart;
    }

    private void Start()
    {
        BuildControls();
        ApplyCachedSync();
        RefreshUi();
    }

    private void ApplyCachedSync()
    {
        if (!ClientPacketManager.HasCachedCharacterSync)
            return;

        HandleSync(ClientPacketManager.GetCachedCharacterSelectEntries());
    }

    private void BuildControls()
    {
        _titleText = CreateText("Title", new Vector2(0.5f, 0.82f), 36, "캐릭터 선택");
        _statusText = CreateText("Status", new Vector2(0.5f, 0.68f), 22, "캐릭터를 고른 뒤 확정하세요.");
        _hintText = CreateText("Hint", new Vector2(0.5f, 0.22f), 18, "");

        _gunnerButton = CreateButton("Gunner", new Vector2(0.35f, 0.45f), "Gunner", () => SelectLocal(PlayableCharacter.Gunner));
        _assassinButton = CreateButton("Assassin", new Vector2(0.65f, 0.45f), "Assassin", () => SelectLocal(PlayableCharacter.Assassin));
        _confirmButton = CreateButton("Confirm", new Vector2(0.5f, 0.08f), "확정", ConfirmSelection);
        _confirmButton.interactable = false;
    }

    private void SelectLocal(PlayableCharacter character)
    {
        _pending = character;
        RefreshUi();
    }

    private void ConfirmSelection()
    {
        if (_pending == PlayableCharacter.None || _confirmed != PlayableCharacter.None)
            return;

        if (!ClientPacketManager.Instance.IsConnected)
        {
            _statusText.text = "서버 연결이 끊어졌습니다.";
            return;
        }

        var packet = new Packet();
        packet.Type = PacketType.UserCharacterSelectReq;
        packet.Write((int)_pending);
        ClientPacketManager.Instance.SendPacket(packet);

        _confirmed = _pending;
        _confirmButton.interactable = false;
        _gunnerButton.interactable = false;
        _assassinButton.interactable = false;
        _statusText.text = $"{CharacterLabel(_confirmed)} 확정 — 다른 플레이어를 기다리는 중…";
    }

    private void HandleSync(IReadOnlyList<CharacterSelectEntry> entries)
    {
        _roomPicks.Clear();
        foreach (var entry in entries)
            _roomPicks[entry.Uid] = entry.Character;

        RefreshUi();
    }

    private void HandleGameStart(GameStartInfo info)
    {
        if (_confirmed != PlayableCharacter.None)
            ClientMatchState.Instance?.SetSelectedCharacter(_confirmed);

        SceneManager.LoadScene(MobaScenes.Main);
    }

    private void RefreshUi()
    {
        if (_titleText == null)
            return;

        HighlightButton(_gunnerButton, _pending == PlayableCharacter.Gunner);
        HighlightButton(_assassinButton, _pending == PlayableCharacter.Assassin);

        if (_confirmed == PlayableCharacter.None)
        {
            _confirmButton.interactable = _pending != PlayableCharacter.None;
            if (_pending != PlayableCharacter.None)
                _statusText.text = $"선택: {CharacterLabel(_pending)} — 확정 버튼을 누르세요.";
        }

        _hintText.text = BuildRoomStatusText();
    }

    private string BuildRoomStatusText()
    {
        if (_roomPicks.Count == 0)
            return "방 플레이어 선택 대기 중…";

        var sb = new StringBuilder();
        foreach (var kvp in _roomPicks)
        {
            string pick = kvp.Value == PlayableCharacter.None ? "선택 중" : CharacterLabel(kvp.Value);
            sb.AppendLine($"Player {kvp.Key}: {pick}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CharacterLabel(PlayableCharacter character)
    {
        return character switch
        {
            PlayableCharacter.Gunner => "Gunner",
            PlayableCharacter.Assassin => "Assassin",
            _ => "-"
        };
    }

    private static void HighlightButton(Button button, bool selected)
    {
        if (button == null)
            return;

        var colors = button.colors;
        colors.normalColor = selected ? new Color(0.35f, 0.55f, 0.85f) : Color.white;
        button.colors = colors;
    }

    private TMP_Text CreateText(string name, Vector2 anchor, int fontSize, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(720f, 80f);
        rect.anchoredPosition = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = fontSize;
        tmp.text = text;
        return tmp;
    }

    private Button CreateButton(string name, Vector2 anchor, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 56f);
        rect.anchoredPosition = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.22f, 0.28f);

        var button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 22;
        tmp.text = label;

        return button;
    }
}
