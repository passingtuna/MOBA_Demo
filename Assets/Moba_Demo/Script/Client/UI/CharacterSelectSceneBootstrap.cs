using MobaTypes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

// 씬이 비어 있어도 캐릭터 선택 UI 런타임 생성 (나중에 CharacterSelect 경로 쓸 때)
public class CharacterSelectSceneBootstrap : MonoBehaviour
{
    private void Start()
    {
        if (ClientMatchState.Instance == null || !ClientMatchState.Instance.HasActiveMatch)
        {
            SceneManager.LoadScene(MobaScenes.Lobby);
            return;
        }

        if (FindObjectOfType<CharacterSelectUI>() != null)
            return;

        EnsureEventSystem();
        BuildUi();
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        esGo.AddComponent<InputSystemUIInputModule>();
#else
        esGo.AddComponent<StandaloneInputModule>();
#endif
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("CharacterSelectCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        var panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.14f, 0.95f);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        panel.AddComponent<CharacterSelectUI>();
    }
}
