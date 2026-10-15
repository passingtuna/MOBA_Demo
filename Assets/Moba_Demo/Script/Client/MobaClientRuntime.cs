using UnityEngine;

// 에디터에서 Game 창 포커스 빼도 ReceiveLoop/Update 돌게 (2클라 테스트용)
public static class MobaClientRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
    }
}
