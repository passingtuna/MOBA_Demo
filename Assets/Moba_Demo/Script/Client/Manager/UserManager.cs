using UnityEngine;
using MobaTypes;

// 클라 로컬 유저 (DontDestroyOnLoad) — 서버 UserManager와는 별개
public class UserManager : MonoBehaviour
{
    public static UserManager Instance { get; private set; }

    private UserData _userData;
    // 로비 자동 재로그인 안 쓰지만, RestoreServerSession 등 레거시/디버그용으로 남겨둠
    private string _cachedUserId;
    private string _cachedPassword;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetUserData(UserData data) => _userData = data;
    public UserData GetUserData() => _userData;

    public void SetLoginCredentials(string userId, string password)
    {
        _cachedUserId = userId;
        _cachedPassword = password;
    }

    public void ClearLoginCredentials()
    {
        _cachedUserId = null;
        _cachedPassword = null;
    }

    // Login 씬 보낼 때 호출. 다음 로그인은 처음부터.
    public void ClearLocalSession()
    {
        _userData = null;
        ClearLoginCredentials();
    }

    public bool TryGetLoginCredentials(out string userId, out string password)
    {
        userId = _cachedUserId;
        password = _cachedPassword;
        return !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(password);
    }
}
