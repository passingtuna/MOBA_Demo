using MobaTypes;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// connect-then-proceed: 버튼 누를 때 TCP 확인 후 Login/Regist 패킷
public class LoginUIAction : MonoBehaviour
{
    public GameObject RegistWindow;

    public TMP_InputField RegistIdFiled;
    public TMP_InputField RegistPassWordFiled;
    public TMP_InputField RegistNickNameFiled;

    public TMP_InputField LoginIdFiled;
    public TMP_InputField LoginPassWordFiled;

    private bool _loginRequestPending;
    private bool _registRequestPending;
    private bool _connectInProgress;

    private async void Start()
    {
        if (LoginPassWordFiled != null)
            LoginPassWordFiled.contentType = TMP_InputField.ContentType.Password;
        if (RegistPassWordFiled != null)
            RegistPassWordFiled.contentType = TMP_InputField.ContentType.Password;

        if (ClientPacketManager.Instance == null)
        {
            ShowMessage("클라이언트 네트워크 매니저가 없습니다.");
            return;
        }

        // 씬 들어오자마자 한 번 붙여두면 첫 로그인 클릭이 덜 답답함 (실패해도 버튼에서 재시도)
        await ClientConnectionHelper.EnsureConnectedAsync();
    }

    private void OnDestroy()
    {
        ClientPacketManager.OnLoginResult -= HandleLoginResult;
        ClientPacketManager.OnRegistResult -= HandleRegistResult;
    }

    public void PressResistButton()
    {
        RegistWindow.SetActive(true);
    }

    public void PressResistXButton()
    {
        RegistWindow.SetActive(false);
    }

    public async void PressConfilmButton()
    {
        if (_registRequestPending || _connectInProgress)
            return;

        string id = RegistIdFiled.text;
        string password = RegistPassWordFiled.text;
        string nickName = RegistNickNameFiled.text;

        if (!TryValidateRegistInput(id, password, nickName, out string message))
        {
            ShowMessage(message);
            return;
        }

        if (!await TryEnsureServerConnectionAsync())
            return;

        id = id.Trim();
        password = password.Trim();
        nickName = nickName.Trim();

        var sentPacket = new Packet();
        sentPacket.Type = PacketType.UserRegistReq;
        sentPacket.Write(id);
        sentPacket.Write(password);
        sentPacket.Write(nickName);

        _registRequestPending = true;
        ClientPacketManager.OnRegistResult -= HandleRegistResult;
        ClientPacketManager.OnRegistResult += HandleRegistResult;
        ClientPacketManager.Instance.SendPacket(sentPacket);
    }

    public async void PressLoginButton()
    {
        if (_loginRequestPending || _connectInProgress)
            return;

        string id = LoginIdFiled.text;
        string password = LoginPassWordFiled.text;

        if (!TryValidateLoginInput(id, password, out string message))
        {
            ShowMessage(message);
            return;
        }

        if (!await TryEnsureServerConnectionAsync())
            return;

        id = id.Trim();
        password = password.Trim();

        var sentPacket = new Packet();
        sentPacket.Type = PacketType.UserLoginReq;
        sentPacket.Write(id);
        sentPacket.Write(password);

        _loginRequestPending = true;
        ClientPacketManager.OnLoginResult -= HandleLoginResult;
        ClientPacketManager.OnLoginResult += HandleLoginResult;
        ClientPacketManager.Instance.SendPacket(sentPacket);
    }

    private async Task<bool> TryEnsureServerConnectionAsync()
    {
        if (ClientPacketManager.Instance == null)
        {
            ShowMessage("클라이언트 네트워크 매니저가 없습니다.");
            return false;
        }

        if (ClientPacketManager.Instance.IsConnected)
            return true;

        _connectInProgress = true;
        bool connected = await ClientConnectionHelper.EnsureConnectedAsync();
        _connectInProgress = false;

        if (!connected)
            ShowMessage("서버에 연결할 수 없습니다. 서버 실행 후 다시 시도해주세요.");

        return connected;
    }

    private void HandleLoginResult(int result, string nickName, int money, int mmr)
    {
        _loginRequestPending = false;

        if (result <= 0)
        {
            ShowMessage(GetLoginFailureMessage(result));
            return;
        }

        var userData = new UserData();
        userData.SetUid(result);
        userData.SetMMR(mmr);
        userData.SetMoney(money);
        userData.SetNickName(nickName);
        UserManager.Instance.SetUserData(userData);
        UserManager.Instance.SetLoginCredentials(LoginIdFiled.text.Trim(), LoginPassWordFiled.text);
        ClientPacketManager.Instance.SetServerSessionActive(true); // HasServerSession — 로비 진입 조건
        SceneManager.LoadScene(MobaScenes.Lobby);
    }

    private void HandleRegistResult(int result)
    {
        _registRequestPending = false;

        switch (result)
        {
            case 1:
                ShowMessage("회원 가입 완료");
                RegistWindow.SetActive(false);
                RegistPassWordFiled.text = string.Empty;
                RegistNickNameFiled.text = string.Empty;
                break;
            case -1:
                ShowMessage("해당 아이디는 이미 사용중인 유저가 있습니다.");
                break;
            case -2:
                ShowMessage("해당 닉네임은 이미 사용중인 유저가 있습니다.");
                break;
            case MobaAccountLimits.ResultInvalidInput:
                ShowMessage("아이디·비밀번호·닉네임을 확인해주세요. (빈 값 또는 길이 초과)");
                break;
            case MobaAccountLimits.ResultServerError:
                ShowMessage("서버 오류로 회원가입에 실패했습니다. 잠시 후 다시 시도해주세요.");
                break;
            default:
                ShowMessage("회원가입에 실패했습니다. 입력 정보를 확인해주세요.");
                break;
        }
    }

    private static string GetLoginFailureMessage(int result)
    {
        return result switch
        {
            MobaAccountLimits.ResultLoginInvalidCredentials =>
                "아이디 또는 비밀번호가 올바르지 않습니다.",
            MobaAccountLimits.ResultLoginMissingUserData =>
                "계정 데이터가 불완전합니다. 관리자에게 문의하거나 다시 가입해주세요.",
            MobaAccountLimits.ResultInvalidInput =>
                "아이디와 비밀번호를 확인해주세요. (빈 값 또는 길이 초과)",
            MobaAccountLimits.ResultServerError =>
                "서버 오류로 로그인에 실패했습니다. 잠시 후 다시 시도해주세요.",
            _ => "로그인에 실패했습니다. 아이디와 비밀번호를 확인해주세요."
        };
    }

    private static bool TryValidateLoginInput(string id, string password, out string message)
    {
        id = id?.Trim() ?? string.Empty;
        password = password?.Trim() ?? string.Empty;

        if (id.Length == 0 || password.Length == 0)
        {
            message = "아이디와 비밀번호를 입력해주세요.";
            return false;
        }

        if (id.Length > MobaAccountLimits.MaxUserIdLength)
        {
            message = $"아이디는 {MobaAccountLimits.MaxUserIdLength}자 이하여야 합니다.";
            return false;
        }

        if (password.Length > MobaAccountLimits.MaxPasswordLength)
        {
            message = $"비밀번호는 {MobaAccountLimits.MaxPasswordLength}자 이하여야 합니다.";
            return false;
        }

        message = null;
        return true;
    }

    private static bool TryValidateRegistInput(string id, string password, string nickName, out string message)
    {
        id = id?.Trim() ?? string.Empty;
        password = password?.Trim() ?? string.Empty;
        nickName = nickName?.Trim() ?? string.Empty;

        if (id.Length == 0 || password.Length == 0 || nickName.Length == 0)
        {
            message = "아이디, 비밀번호, 닉네임을 모두 입력해주세요.";
            return false;
        }

        if (id.Length > MobaAccountLimits.MaxUserIdLength)
        {
            message = $"아이디는 {MobaAccountLimits.MaxUserIdLength}자 이하여야 합니다.";
            return false;
        }

        if (password.Length > MobaAccountLimits.MaxPasswordLength)
        {
            message = $"비밀번호는 {MobaAccountLimits.MaxPasswordLength}자 이하여야 합니다.";
            return false;
        }

        if (nickName.Length > MobaAccountLimits.MaxNickNameLength)
        {
            message = $"닉네임은 {MobaAccountLimits.MaxNickNameLength}자 이하여야 합니다.";
            return false;
        }

        message = null;
        return true;
    }

    private void ShowMessage(string text)
    {
        if (UI_Manager.Instance == null)
        {
            Debug.LogWarning($"[Login] {text}");
            return;
        }

        var popupRoot = GetComponentInParent<Canvas>()?.gameObject ?? gameObject;
        MessagePopUpUI messagePop = UI_Manager.Instance.UIPopUp<MessagePopUpUI>(popupRoot);
        messagePop.SetText(text);
    }
}
