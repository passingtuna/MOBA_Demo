using UnityEngine;

public class UIBase : MonoBehaviour
{
    // UI_Manager PopUpStack — OnOpen/OnClose로 아래 팝업 가리기
    public virtual void OnOpen()
    {   gameObject.SetActive(true);
    }
    public void OnClose()
    {
        gameObject.SetActive(false);
    }
}
