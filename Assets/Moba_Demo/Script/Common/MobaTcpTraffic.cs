using System.Net.Sockets;

namespace MobaTypes
{
    // NoDelay는 소켓 하나에만 걸림 — 인게임 on/off는 ClientPacketManager에서 구간별로
    public static class MobaTcpTraffic
    {
        public static void SetNoDelay(Socket socket, bool noDelay)
        {
            if (socket == null)
                return;

            socket.NoDelay = noDelay;
        }
    }
}
