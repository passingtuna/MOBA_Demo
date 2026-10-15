using MobaTypes;
using System.Threading.Tasks;

public static class ClientConnectionHelper
{
    // TCP만 — HasServerSession은 Login 성공 후 따로 켬
    public static Task<bool> EnsureConnectedAsync()
    {
        if (ClientPacketManager.Instance == null)
            return Task.FromResult(false);

        return ClientPacketManager.Instance.EnsureConnectedAsync(
            MobaNetwork.DefaultHost,
            MobaNetwork.DefaultPort);
    }

    // 예전 로비 자동 재로그인용. 지금은 로비에서 안 씀 — Login 버튼 쪽 EnsureConnectedAsync만 사용
    public static async Task<bool> EnsureConnectedAndLoggedInAsync()
    {
        if (!await EnsureConnectedAsync())
            return false;

        if (ClientPacketManager.Instance.HasServerSession)
            return true;

        if (UserManager.Instance == null ||
            !UserManager.Instance.TryGetLoginCredentials(out string userId, out string password))
        {
            return false;
        }

        return await ClientPacketManager.Instance.RestoreServerSessionAsync(userId, password);
    }
}
