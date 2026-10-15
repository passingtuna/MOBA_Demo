using UnityEngine;
using TMPro;

public class MessagePopUpUI : UIBase
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public TMP_Text Text;
    public void SetText(string settingText)
    {
        Text.text = settingText;
    }
}
