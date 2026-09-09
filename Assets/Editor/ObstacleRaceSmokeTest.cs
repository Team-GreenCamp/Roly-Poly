using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Unity.Netcode;
using UnityEngine;

// Play 모드에서 실제 서버 판정과 소유자 리스폰을 검증하는 개발용 메뉴입니다.
public static class ObstacleRaceSmokeTest
{
    private static IEnumerator run;
    private static double nextStep;
    [MenuItem("Tools/Battle Royal/Test Race Timeout")]
    public static void Timeout()
    {
        var manager = SurvivalGameManager.Instance;
        if (!EditorApplication.isPlaying || manager == null || !manager.IsRace || manager.State != SurvivalGameManager.MatchState.Playing)
            throw new InvalidOperationException("Race must be playing for timeout test.");
        var field = typeof(SurvivalGameManager).GetField("raceDeadline", BindingFlags.NonPublic | BindingFlags.Instance);
        ((NetworkVariable<double>)field.GetValue(manager)).Value = NetworkManager.Singleton.ServerTime.Time - 1;
        EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
        {
            Require(manager.State == SurvivalGameManager.MatchState.Finished && manager.WinnerClientId == ulong.MaxValue,
                "Timeout with no finishers must not invent a winner");
            Debug.Log("RACE TEST PASS: timeout without finishers ends match with no winner.");
        };
    }
    [MenuItem("Tools/Battle Royal/Test Race Flow")]
    public static void Start()
    {
        if (!EditorApplication.isPlaying || SurvivalGameManager.Instance == null || !SurvivalGameManager.Instance.IsRace)
            throw new InvalidOperationException("Open Obstacle Race in Play Mode first.");
        EditorApplication.update -= Tick;
        run = Test(); nextStep = 0;
        EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextStep) return;
        try
        {
            if (!EditorApplication.isPlaying || !run.MoveNext()) { EditorApplication.update -= Tick; return; }
            nextStep = EditorApplication.timeSinceStartup + .7;
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.update -= Tick; }
    }
    private static IEnumerator Test()
    {
        var manager = SurvivalGameManager.Instance;
        double deadline = EditorApplication.timeSinceStartup + 30;
        while (manager.State != SurvivalGameManager.MatchState.Playing)
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Countdown did not complete");
            yield return null;
        }
        var player = NetworkManager.Singleton.LocalClient.PlayerObject;
        var body = player.GetComponent<Rigidbody>();
        var progress = (Dictionary<ulong, int>)typeof(SurvivalGameManager).GetField("raceProgress", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        progress.Clear();
        ulong id = NetworkManager.Singleton.LocalClientId;
        void Move(Vector3 position)
        {
            body.position = position; player.transform.position = position;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
        Move(new Vector3(12, 1.5f, 2)); yield return null;
        Require(manager.RaceFinishCount == 0, "Finish shortcut accepted");
        Move(new Vector3(-12, 1.5f, 18)); yield return null;
        Require(progress.TryGetValue(id, out int step) && step == 1,
            "Checkpoint not recorded: progress=" + step + ", position=" + player.transform.position);
        Move(new Vector3(-12, -8, 18)); yield return null;
        Require(player.transform.position.y > -1 && Mathf.Abs(player.transform.position.z - 18) < 3, "Checkpoint respawn failed");
        Debug.Log("RACE TEST PASS: shortcut rejection and owner checkpoint respawn.");
        foreach (var point in new[] { new Vector3(-12,1.5f,38), new(-12,1.5f,58), new(0,1.5f,67),
            new(12,1.5f,58), new(12,1.5f,38), new(12,1.5f,18), new(12,1.5f,2) })
        {
            Move(point); yield return null;
        }
        Require(manager.State == SurvivalGameManager.MatchState.Finished && manager.WinnerClientId == id,
            "Finish or winner assignment failed");
        Require(manager.RaceFinishCount == 1, "Duplicate or missing finish");
        Debug.Log("RACE TEST PASS: ordered gates, solo completion, winner assignment, duplicate finish prevention.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("RACE TEST FAILED: " + message);
    }
}
