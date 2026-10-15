using MobaTypes;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using static PacketManager;
using static UserManager;

public class LoginService
{
    // SP LoginRequest — @Result가 UID(양수) 또는 MobaAccountLimits 음수 코드
    public async Task LoginProcessAsync(NetworkSession session, Packet packet)
    {
        string id = packet.ReadString();
        string password = packet.ReadString();

        if (!TryNormalizeLogin(id, password, out id, out password))
        {
            SendLoginResult(session, MobaAccountLimits.ResultInvalidInput, string.Empty, 0, 0);
            return;
        }

        var tcs = new TaskCompletionSource<Dictionary<string, object>>();
        DBManager.SQLData data = new DBManager.SQLData();
        data.sqlQuery = "LoginRequest";
        data.Tcs = tcs;
        data.AddInput("@UserId", SqlDbType.VarChar, id, MobaAccountLimits.MaxUserIdLength);
        data.AddInput("@Password", SqlDbType.VarChar, password, MobaAccountLimits.MaxPasswordLength);

        data.AddOutput("@Result", SqlDbType.Int);
        data.AddOutput("@NickName", SqlDbType.VarChar, MobaAccountLimits.MaxNickNameLength);
        data.AddOutput("@Money", SqlDbType.Int);
        data.AddOutput("@MMR", SqlDbType.Int);

        DBManager.Instance.SendDBSQL(data);
        var result = await tcs.Task;

        if (!session.IsConnected || session.UID != 0)
            return;

        if (result == null)
        {
            SendLoginResult(session, MobaAccountLimits.ResultServerError, string.Empty, 0, 0);
            return;
        }

        int uid = DBManager.GetValue<int>(result, "@Result");

        if (uid <= 0)
        {
            SendLoginResult(session, uid, string.Empty, 0, 0);
            return;
        }

        // 같은 UID로 이미 접속 중이면 예전 클라 끊음
        if (PacketManager.Instance.UserSessions.TryGetValue(uid, out UserSession userSession))
        {
            var oldSession = userSession.Connection;
            if (oldSession != null && oldSession != session)
            {
                PacketManager.Instance.NotifyDuplicateLogin(oldSession);
                PacketManager.Instance.FlushSendQueue();
                oldSession.Disconnect();
            }
        }
        else
        {
            userSession = new UserSession(uid);
            PacketManager.Instance.UserSessions.TryAdd(uid, userSession);
        }

        userSession.SessionState = SessionState.LoggedIn;
        userSession.BindConnection(session);

        int money = DBManager.GetValue<int>(result, "@Money");
        int mmr = DBManager.GetValue<int>(result, "@MMR");
        string nickName = DBManager.GetValue<string>(result, "@NickName") ?? string.Empty;

        UserData tempData;
        if (UserManager.Instance.dicUserData.TryGetValue(session.UID, out tempData))
        {
            tempData.SetUid(uid);
            tempData.SetNickName(nickName);
            tempData.SetMoney(money);
            tempData.SetMMR(mmr);
        }
        else
        {
            tempData = new UserData();
            tempData.SetUid(uid);
            tempData.SetNickName(nickName);
            tempData.SetMoney(money);
            tempData.SetMMR(mmr);
            UserManager.Instance.dicUserData.TryAdd(session.UID, tempData);
        }

        SendLoginResult(session, uid, nickName, money, mmr);
    }

    public async Task RegistProcessAsync(NetworkSession session, Packet packet)
    {
        string id = packet.ReadString();
        string password = packet.ReadString();
        string nickName = packet.ReadString();

        if (!TryNormalizeRegist(id, password, nickName, out id, out password, out nickName))
        {
            SendRegistResult(session, MobaAccountLimits.ResultInvalidInput);
            return;
        }

        var tcs = new TaskCompletionSource<Dictionary<string, object>>();
        DBManager.SQLData data = new DBManager.SQLData();
        data.sqlQuery = "ResistRequest"; // SP 이름 오타 그대로 (DB와 맞춤)
        data.Tcs = tcs;
        data.AddInput("@UserId", SqlDbType.VarChar, id, MobaAccountLimits.MaxUserIdLength);
        data.AddInput("@Password", SqlDbType.VarChar, password, MobaAccountLimits.MaxPasswordLength);
        data.AddInput("@NickName", SqlDbType.VarChar, nickName, MobaAccountLimits.MaxNickNameLength);
        data.AddOutput("@Result", SqlDbType.Int);

        DBManager.Instance.SendDBSQL(data);
        var result = await tcs.Task;
        int registResult = result == null
            ? MobaAccountLimits.ResultServerError
            : DBManager.GetValue<int>(result, "@Result");

        SendRegistResult(session, registResult);
    }

    // 실패여도 nick/money/mmr 필드는 항상 채워서 보냄 (클라 Read 순서 맞추기)
    private static void SendLoginResult(NetworkSession session, int uid, string nickName, int money, int mmr)
    {
        Packet sentPacket = new Packet();
        sentPacket.Type = PacketType.UserLoginResult;
        sentPacket.Write(uid);
        sentPacket.Write(nickName ?? string.Empty);
        sentPacket.Write(money);
        sentPacket.Write(mmr);
        PacketManager.Instance.SendToSession(session, sentPacket);
    }

    private static void SendRegistResult(NetworkSession session, int resultCode)
    {
        Packet sentPacket = new Packet();
        sentPacket.Type = PacketType.UserRegistResult;
        sentPacket.Write(resultCode);
        PacketManager.Instance.SendToSession(session, sentPacket);
    }

    private static bool TryNormalizeLogin(string id, string password, out string normalizedId, out string normalizedPassword)
    {
        normalizedId = id?.Trim() ?? string.Empty;
        normalizedPassword = password?.Trim() ?? string.Empty;

        if (normalizedId.Length == 0 || normalizedPassword.Length == 0)
            return false;

        if (normalizedId.Length > MobaAccountLimits.MaxUserIdLength)
            return false;

        if (normalizedPassword.Length > MobaAccountLimits.MaxPasswordLength)
            return false;

        return true;
    }

    private static bool TryNormalizeRegist(
        string id,
        string password,
        string nickName,
        out string normalizedId,
        out string normalizedPassword,
        out string normalizedNickName)
    {
        normalizedId = id?.Trim() ?? string.Empty;
        normalizedPassword = password?.Trim() ?? string.Empty;
        normalizedNickName = nickName?.Trim() ?? string.Empty;

        if (normalizedId.Length == 0 || normalizedPassword.Length == 0 || normalizedNickName.Length == 0)
            return false;

        if (normalizedId.Length > MobaAccountLimits.MaxUserIdLength)
            return false;

        if (normalizedPassword.Length > MobaAccountLimits.MaxPasswordLength)
            return false;

        if (normalizedNickName.Length > MobaAccountLimits.MaxNickNameLength)
            return false;

        return true;
    }
}
