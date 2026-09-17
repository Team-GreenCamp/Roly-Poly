using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 탈락한 로컬 플레이어의 관전 카메라 (순수 로컬 연출, NetworkBehaviour 아님).
//
// NOA(NetworkOwnedObjectActivator)의 BindLocalCamera는 스폰/씬 로드/소유권 변경 시에만 재바인딩하므로,
// 매치 도중 같은 CinemachineCamera의 Follow/LookAt만 갈아끼우면 충돌하지 않는다.
// 로비 복귀 시에는 NOA가 새 씬에서 알아서 다시 바인딩한다.
[DisallowMultipleComponent]
public class SpectatorController : MonoBehaviour
{
    // HUD가 "관전 중: Player N" 표시를 갱신할 때 사용.
    public event Action<string> OnSpectateTargetChanged;

    private CinemachineCamera spectateCamera;
    private NetworkOwnedObjectActivator currentTarget;
    private bool spectating;

    private void Awake()
    {
        enabled = false; // SurvivalGameManager가 탈락 시 BeginSpectating으로 켠다.
    }

    public void BeginSpectating()
    {
        spectating = true;
        enabled = true;
        CycleTarget(1);
    }

    private void Update()
    {
        if (!spectating)
        {
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            CycleTarget(1);
        }
        else if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            CycleTarget(-1);
        }
    }

    // 탈락자 발생 시 SurvivalGameManager가 호출 — 관전 대상이 사라졌으면 다음 생존자로 넘어간다.
    public void HandleAliveListChanged()
    {
        if (!spectating)
        {
            return;
        }

        if (currentTarget == null || IsTargetGone(currentTarget))
        {
            CycleTarget(1);
        }
    }

    private void CycleTarget(int direction)
    {
        List<NetworkOwnedObjectActivator> alive = CollectAlivePlayers();
        if (alive.Count == 0)
        {
            // 전멸 직전 프레임 등 — 현재 카메라를 유지한다(곧 승자 패널이 뜬다).
            return;
        }

        int currentIndex = currentTarget != null ? alive.IndexOf(currentTarget) : -1;
        int nextIndex = currentIndex < 0
            ? 0
            : (((currentIndex + direction) % alive.Count) + alive.Count) % alive.Count;

        ApplyTarget(alive[nextIndex]);
    }

    private void ApplyTarget(NetworkOwnedObjectActivator target)
    {
        currentTarget = target;

        if (spectateCamera == null)
        {
            // NOA가 만든 런타임 3인칭 카메라(또는 씬 카메라)를 그대로 이어받는다.
            spectateCamera = FindFirstObjectByType<CinemachineCamera>();
        }

        if (spectateCamera == null || target == null)
        {
            return;
        }

        Transform followTarget = target.CameraRoot != null ? target.CameraRoot : target.transform;
        spectateCamera.Follow = followTarget;

        // Pan Tilt 카메라는 LookAt을 강제하지 않는다(NOA BindLocalCamera와 동일 규칙).
        if (spectateCamera.GetComponent<CinemachinePanTilt>() == null)
        {
            spectateCamera.LookAt = followTarget;
        }

        OnSpectateTargetChanged?.Invoke($"Player {target.OwnerClientId + 1}");
    }

    private static List<NetworkOwnedObjectActivator> CollectAlivePlayers()
    {
        List<NetworkOwnedObjectActivator> alive = new List<NetworkOwnedObjectActivator>();

        SurvivalGameManager gameManager = SurvivalGameManager.Instance;
        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

        NetworkOwnedObjectActivator[] activators =
            FindObjectsByType<NetworkOwnedObjectActivator>(FindObjectsSortMode.None);

        for (int i = 0; i < activators.Length; i++)
        {
            NetworkOwnedObjectActivator activator = activators[i];
            if (activator == null || activator.OwnerClientId == localClientId)
            {
                continue;
            }

            if (gameManager != null && gameManager.IsEliminated(activator.OwnerClientId))
            {
                continue;
            }

            alive.Add(activator);
        }

        // clientId 순으로 정렬해 순환 순서를 안정적으로 만든다.
        alive.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        return alive;
    }

    private static bool IsTargetGone(NetworkOwnedObjectActivator target)
    {
        if (target == null)
        {
            return true;
        }

        SurvivalGameManager gameManager = SurvivalGameManager.Instance;
        return gameManager != null && gameManager.IsEliminated(target.OwnerClientId);
    }
}
