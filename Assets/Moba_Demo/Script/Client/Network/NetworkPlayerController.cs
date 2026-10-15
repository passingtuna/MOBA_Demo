using MobaTypes;
using UnityEngine;
using UnityEngine.AI;

// 로컬: NavMesh 이동 + UserPositionReq / 원격: UserPositionSync 보간
public class NetworkPlayerController : MonoBehaviour
{
    public int Uid { get; private set; }
    public bool IsLocal { get; private set; }

    private NavMeshAgent _agent;
    private Vector3 _remoteTarget;
    private bool _hasRemoteTarget;
    private Vector3 _lastSentPosition;
    private float _sendCooldown;
    private float _sendInterval;
    private float _sendThresholdSqr;
    private float _remoteLerpSpeed;

    [SerializeField] private MobaInGameNetTuning tuningOverride;

    public void Initialize(int uid, bool isLocal)
    {
        var tuning = tuningOverride != null ? tuningOverride : MobaInGameNetTuning.Default;
        _sendInterval = tuning.PositionSendInterval;
        _sendThresholdSqr = tuning.PositionSendThreshold * tuning.PositionSendThreshold;
        _remoteLerpSpeed = tuning.RemoteLerpSpeed;

        Uid = uid;
        IsLocal = isLocal;
        _agent = GetComponent<NavMeshAgent>();
        _lastSentPosition = transform.position;
        _remoteTarget = transform.position;

        var localInput = GetComponent<LocalPlayerInputController>();
        if (localInput != null)
            localInput.enabled = isLocal;

        // 원격은 서버 좌표만 따라감 — NavMeshAgent 끄고 Lerp
        if (_agent != null && !isLocal)
        {
            _agent.isStopped = true;
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }
    }

    private void Update()
    {
        if (IsLocal)
            TrySendPosition();
        else if (_hasRemoteTarget)
            ApplyRemoteInterpolation();
    }

    private void TrySendPosition()
    {
        _sendCooldown -= Time.deltaTime;
        if (_sendCooldown > 0f)
            return;

        Vector3 pos = transform.position;
        if ((pos - _lastSentPosition).sqrMagnitude < _sendThresholdSqr)
            return;

        _lastSentPosition = pos;
        _sendCooldown = _sendInterval;

        var packet = new Packet();
        packet.Type = PacketType.UserPositionReq;
        packet.Write(pos.x);
        packet.Write(pos.y);
        packet.Write(pos.z);
        ClientPacketManager.Instance.SendPacket(packet);
    }

    public void SetRemotePosition(Vector3 worldPosition)
    {
        _remoteTarget = worldPosition;
        _hasRemoteTarget = true;
    }

    private void ApplyRemoteInterpolation()
    {
        transform.position = Vector3.Lerp(transform.position, _remoteTarget, Time.deltaTime * _remoteLerpSpeed);
        if ((transform.position - _remoteTarget).sqrMagnitude < 0.001f)
            transform.position = _remoteTarget;
    }
}
