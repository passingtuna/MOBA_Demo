using System;
using System.Collections.Generic;
using UnityEngine;

// 로비 전용. 끊김 한 번에 바로 Login 씬 가면 너무 빡빡해서, 실패 횟수 + 시간으로 판단.
// (로비에서는 자동 재로그인 안 함 — Login에서 다시 붙이는 흐름)
public static class LobbyDisconnectPolicy
{
    // 아래 네 개는 체감 맞추면서 조정하면 됨
    private const float FailureWindowSeconds = 12f;       // 이 구간 안의 끊김만 카운트
    private const int FailureThreshold = 3;
    private const float MaxWaitBeforeRedirectSeconds = 25f; // 계속 불안정하면 이 시간 지나면 무조건 Login
    private const float MinGraceBeforeRedirectSeconds = 4f;   // 첫 실패 직후 몇 초는 버퍼

    private static readonly List<float> FailureTimes = new();

    private static float _firstFailureTime = -1f;
    private static bool _unstableNotified;
    private static bool _redirectTriggered;

    public static bool IsWatchingFailures => _firstFailureTime >= 0f;

    // 한 번이라도 불안정 판정 나면 매칭 버튼 막아둠
    public static bool ShouldBlockLobbyActions =>
        IsWatchingFailures && !HasValidServerSession();

    public static event Action OnBecameUnstable;
    public static event Action OnRedirectRequired;

    public static void Reset()
    {
        _firstFailureTime = -1f;
        FailureTimes.Clear();
        _unstableNotified = false;
        _redirectTriggered = false;
    }

    // OnDisconnected, 매칭 시도 실패 등 — Update에서 매 프레임 세션 검사하지 말고 여기만 호출
    public static void NoteConnectionFailure()
    {
        float now = Time.unscaledTime;

        FailureTimes.Add(now);
        if (_firstFailureTime < 0f)
            _firstFailureTime = now;

        PruneFailureWindow(now);

        if (!_unstableNotified)
        {
            _unstableNotified = true;
            OnBecameUnstable?.Invoke();
        }

        TryTriggerRedirect(now);
    }

    // 실패 횟수는 이벤트로만 올라가고, MaxWait 같은 시간 조건은 Tick에서 확인
    public static void Tick()
    {
        if (_firstFailureTime < 0f)
            return;

        // 거의 없지만, 세션 살아있으면 카운트 초기화
        if (HasValidServerSession())
        {
            Reset();
            return;
        }

        TryTriggerRedirect(Time.unscaledTime);
    }

    private static void TryTriggerRedirect(float now)
    {
        if (_redirectTriggered || _firstFailureTime < 0f)
            return;

        PruneFailureWindow(now);

        float sinceFirst = now - _firstFailureTime;
        if (sinceFirst < MinGraceBeforeRedirectSeconds)
            return;

        bool enoughFailures = FailureTimes.Count >= FailureThreshold;
        bool waitedLongEnough = sinceFirst >= MaxWaitBeforeRedirectSeconds;

        if (!enoughFailures && !waitedLongEnough)
            return;

        _redirectTriggered = true;
        OnRedirectRequired?.Invoke();
    }

    private static void PruneFailureWindow(float now)
    {
        for (int i = FailureTimes.Count - 1; i >= 0; i--)
        {
            if (now - FailureTimes[i] > FailureWindowSeconds)
                FailureTimes.RemoveAt(i);
        }

    }

    private static bool HasValidServerSession()
    {
        return ClientPacketManager.Instance != null &&
               ClientPacketManager.Instance.IsConnected &&
               ClientPacketManager.Instance.HasServerSession;
    }
}
