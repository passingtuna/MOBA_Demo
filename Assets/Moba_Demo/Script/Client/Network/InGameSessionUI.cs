using MobaTypes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Esc → UserLeaveRoomReq → Lobby (서버에서 방에서 빼고 NoDelay 끔)
public class InGameSessionUI : MonoBehaviour
{
    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame != true)
            return;

        ReturnToLobby();
    }

    public void ReturnToLobby()
    {
        if (ClientPacketManager.Instance != null && ClientPacketManager.Instance.IsConnected)
        {
            var packet = new Packet();
            packet.Type = PacketType.UserLeaveRoomReq;
            ClientPacketManager.Instance.SendPacket(packet);
        }

        ClientMatchState.Instance?.Clear();
        ClientPacketManager.Instance?.SetGameLowLatency(false);
        SceneManager.LoadScene(MobaScenes.Lobby);
    }
}
