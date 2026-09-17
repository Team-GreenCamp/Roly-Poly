using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using Unity.Netcode;
using UnityEngine;

// 실제 호스트에서 중앙 발판의 재생성과 제한시간 승리 판정을 확인합니다.
public static class TimedSurvivalSmokeTest
{
    static IEnumerator run;
    static double next;
    static object Field(object obj,string name) => obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    static void Require(bool value,string message) { if(!value)throw new Exception("SURVIVAL TEST FAILED: "+message); }
    [MenuItem("Tools/Battle Royal/Test Timed Survival")]
    public static void Start()
    {
        Require(EditorApplication.isPlaying,"Enter play mode");
        run=Test();next=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    [MenuItem("Tools/Battle Royal/Test Survival Last Survivor")]
    public static void LastSurvivor()
    {
        Require(EditorApplication.isPlaying,"Enter play mode");
        run=LastTest();next=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    static IEnumerator LastTest()
    {
        var manager=SurvivalGameManager.Instance;
        double deadline=EditorApplication.timeSinceStartup+30;
        while(manager.State!=SurvivalGameManager.MatchState.Playing){Require(EditorApplication.timeSinceStartup<deadline,"Start timeout");yield return null;}
        Require(EditorApplication.isPlaying && manager.State==SurvivalGameManager.MatchState.Playing,"Match must be playing");
        ulong local=NetworkManager.Singleton.LocalClientId;
        var alive=(HashSet<ulong>)Field(manager,"aliveClients");alive.Clear();alive.Add(local);alive.Add(9876);
        typeof(SurvivalGameManager).GetField("soloMode",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(manager,false);
        typeof(SurvivalGameManager).GetMethod("EliminateOnServer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,new object[]{9876UL,true});
        Require(manager.State==SurvivalGameManager.MatchState.Finished && manager.WinnerClientId==local,"Last survivor winner");
        Debug.Log("SURVIVAL TEST PASS: elimination ends match immediately with last survivor winner before deadline.");
        yield break;
    }
    [MenuItem("Tools/Battle Royal/Test Survival Bonus Coins")]
    public static void BonusCoins()
    {
        Require(EditorApplication.isPlaying,"Enter play mode");
        run=BonusTest();next=0;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
    }
    static IEnumerator BonusTest()
    {
        var manager=SurvivalGameManager.Instance;
        double limit=EditorApplication.timeSinceStartup+30;
        while(manager.State!=SurvivalGameManager.MatchState.Playing){Require(EditorApplication.timeSinceStartup<limit,"Start timeout");yield return null;}
        var body=NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Rigidbody>();body.isKinematic=true;body.position=new Vector3(0,4,0);
        var coins=SurvivalCoinManager.Instance;
        Require(!coins.IsBonusTime,"Bonus started early");
        ((NetworkVariable<double>)Field(manager,"survivalDeadline")).Value=NetworkManager.Singleton.ServerTime.Time+29;
        yield return null;
        int bonus=0;
        foreach(DictionaryEntry entry in (IDictionary)Field(coins,"activeCoins"))
        {
            var data=entry.Value;var type=data.GetType();
            if((int)type.GetField("value").GetValue(data)!=3)continue;
            bonus++;
            Vector3 position=(Vector3)type.GetField("position").GetValue(data);
            Require(Mathf.Max(Mathf.Abs(position.x),Mathf.Abs(position.z))>=13.44f,"Bonus outside edge band");
            var visual=(GameObject)type.GetField("visual").GetValue(data);
            Require(visual!=null && visual.transform.GetChild(0).localScale.x==.8f,"Bonus visual size");
        }
        Require(bonus==8 && coins.IsBonusTime,"Immediate bonus wave");
        typeof(SurvivalCoinManager).GetMethod("SpawnWaveOnServer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(coins,new object[]{true});
        int after=0;
        foreach(DictionaryEntry entry in (IDictionary)Field(coins,"activeCoins"))
            if((int)entry.Value.GetType().GetField("value").GetValue(entry.Value)==3)after++;
        Require(after==8,"Bonus cap exceeded");
        Debug.Log("SURVIVAL BONUS TEST PASS: 30-second trigger, 8 outer 3-point coins, larger visuals and duplicate-wave cap.");
    }

    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try { if(!EditorApplication.isPlaying || !run.MoveNext()){EditorApplication.update-=Tick;return;} next=EditorApplication.timeSinceStartup+.1; }
        catch(Exception e){EditorApplication.update-=Tick;Debug.LogException(e);}
    }
    static IEnumerator Test()
    {
        var manager=SurvivalGameManager.Instance;
        double deadline=EditorApplication.timeSinceStartup+30;
        while(manager.State!=SurvivalGameManager.MatchState.Playing){Require(EditorApplication.timeSinceStartup<deadline,"Start timeout");yield return null;}
        Require(manager.IsTimedCoinSurvival && manager.SurvivalRemaining>110 && !manager.IsSuddenDeath,"Rules/timer");
        var body=NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Rigidbody>();
        body.isKinematic=true;body.position=new Vector3(0,4,0);
        FallingPlatform tile=null;
        foreach(var p in UnityEngine.Object.FindObjectsByType<FallingPlatform>(FindObjectsSortMode.None))
            if(tile==null || p.transform.position.sqrMagnitude<tile.transform.position.sqrMagnitude)tile=p;
        Require(tile.triggerByStepping && tile.respawnDelay==3,"Central tile configuration");
        tile.ServerForceFall();
        double start=EditorApplication.timeSinceStartup;
        while(EditorApplication.timeSinceStartup-start<1)yield return null;
        Require(tile.IsFalling && tile.HasPhysicallyDropped && !tile.GetComponent<Collider>().enabled && !tile.GetComponent<Renderer>().enabled,"Hidden phase");
        while(EditorApplication.timeSinceStartup-start<3.85)yield return null;
        Require(tile.GetComponent<Renderer>().enabled && !tile.GetComponent<Collider>().enabled,"Fade-in phase");
        while(EditorApplication.timeSinceStartup-start<4.5)yield return null;
        Require(!tile.IsFalling && tile.GetComponent<Collider>().enabled && tile.GetComponent<Renderer>().enabled,"Regeneration complete");
        tile.ServerForceFall();Require(tile.IsFalling,"Repeat activation");
        Debug.Log("SURVIVAL TEST PASS: central tile hide, fade-in, collider restoration and repeat activation.");
        ulong local=NetworkManager.Singleton.LocalClientId;
        var alive=(HashSet<ulong>)Field(manager,"aliveClients");alive.Clear();alive.Add(local);alive.Add(9876);
        var coins=SurvivalCoinManager.Instance;
        var scores=(Dictionary<ulong,int>)Field(coins,"scores");scores[local]=8;scores[9876]=8;scores[9999]=100;
        var reached=(Dictionary<ulong,double>)Field(coins,"scoreReachedAt");reached[local]=1;reached[9876]=2;
        ((NetworkVariable<double>)Field(manager,"survivalDeadline")).Value=NetworkManager.Singleton.ServerTime.Time-1;
        yield return null;
        Require(manager.State==SurvivalGameManager.MatchState.Finished && manager.WinnerClientId==local,"Timeout score/tie/survivor filter");
        Debug.Log("SURVIVAL TEST PASS: timeout, coin tie-break and exclusion of eliminated high scorer.");
    }
}

