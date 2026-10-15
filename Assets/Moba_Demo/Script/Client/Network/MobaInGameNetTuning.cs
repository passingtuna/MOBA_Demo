using UnityEngine;

[CreateAssetMenu(fileName = "InGameNetTuning", menuName = "Moba/InGame Net Tuning")]
public class MobaInGameNetTuning : ScriptableObject
{
    [Tooltip("위치 패킷 최소 전송 간격(초). 낮을수록 촘촘, 트래픽 증가.")]
    [Min(0.01f)] public float PositionSendInterval = 0.033f;

    [Tooltip("이 거리(m) 이상 움직였을 때만 전송.")]
    [Min(0.001f)] public float PositionSendThreshold = 0.02f;

    [Tooltip("원격 플레이어 위치 보간 속도.")]
    [Min(1f)] public float RemoteLerpSpeed = 16f;

    private static MobaInGameNetTuning _default;

    public static MobaInGameNetTuning Default
    {
        get
        {
            if (_default != null)
                return _default;

            _default = Resources.Load<MobaInGameNetTuning>("InGameNetTuning");
            if (_default == null)
                _default = CreateInstance<MobaInGameNetTuning>();

            return _default;
        }
    }
}
