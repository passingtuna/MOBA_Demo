using MobaTypes;
using UnityEngine;

// Opening/MiddleGame 등 페이즈 훅 — 아직 로그만
public class GamePhaseController : MonoBehaviour
{
    [SerializeField] private GameState initialPhase = GameState.Opening;

    private void Start()
    {
        if (ClientMatchState.Instance != null && ClientMatchState.Instance.HasActiveMatch)
            Debug.Log($"[Main] Room {ClientMatchState.Instance.RoomId} ({ClientMatchState.Instance.MatchType})");

        StartPhase(initialPhase);
    }

    public void StartPhase(GameState phase)
    {
        Debug.Log($"[Game] Phase started: {phase}");
    }
}
