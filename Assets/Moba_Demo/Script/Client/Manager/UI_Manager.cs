using System.Collections.Generic;
using UnityEngine;

public class UI_Manager : MonoBehaviour
{
    public static UI_Manager Instance { get; private set; }

    private Stack<UIBase> PopUpStack = new Stack<UIBase>(); // 매칭 팝업 등 LIFO
    [SerializeField] private List<UIBase> PopupPrefabs;
    private Dictionary<System.Type, UIBase> PopupDict;


    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        PopupDict = new Dictionary<System.Type, UIBase>();
        foreach (var prefab in PopupPrefabs)
        {
            PopupDict.Add(prefab.GetType(), prefab);
        }

    }
    public void PopUi()
    {
        if (PopUpStack.Count == 0)
            return;

        UIBase top = PopUpStack.Pop();
        top.OnClose();
        Destroy(top.gameObject);
        if (PopUpStack.Count > 0)
        {
            PopUpStack.Peek().OnOpen();
        }
    }
    public void PushUi(UIBase UI)
    {
        if (PopUpStack.Count > 0)
        {
            PopUpStack.Peek().OnClose();
        }

        PopUpStack.Push(UI);
        UI.OnOpen();
    }

    public void PopAll()
    {
        while (PopUpStack.Count > 0)
        {
            UIBase ui = PopUpStack.Pop();
            ui.OnClose();
        }
    }
    public T UIPopUp<T>(GameObject popupRoot) where T : UIBase
    {
        if (!PopupDict.TryGetValue(typeof(T), out UIBase basePrefab))
        {
            Debug.LogError($"Popup prefab not found: {typeof(T)}");
            return null;
        }

        T prefab = basePrefab as T;
        T instance = Instantiate(prefab, popupRoot.transform, false);
        PushUi(instance);
        return instance;
    }
}
