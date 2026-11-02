using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// EC2용 Linux 데디케이티드 서버(x86_64) 빌드. deploy/deploy.sh가 호출한다.
// - 에디터가 켜져 있으면: Unity CLI eval로 BuildAsync(outputPath) → 상태 파일을 폴링
// - 에디터가 꺼져 있으면: unity build --execute-method LinuxServerBuild.BuildFromCommandLine -o <outputPath>
// 빌드 후 에디터의 활성 플랫폼은 원래대로 되돌린다.
public static class LinuxServerBuild
{
    // 배포 스크립트가 읽는 상태 파일. 첫 줄: running | succeeded | failed, 둘째 줄: 메시지.
    public const string StatusFile = "Library/LinuxServerBuild/status.txt";
    private const string ExecutableName = "server.x86_64";

    [MenuItem("Tools/Build/Linux Server (EC2)")]
    private static void BuildFromMenu()
    {
        string folder = EditorUtility.SaveFolderPanel("Linux 서버 빌드 폴더", "", "");
        if (!string.IsNullOrEmpty(folder))
        {
            BuildAsync(Path.Combine(folder, ExecutableName));
        }
    }

    // eval 호출이 빌드 시간 동안 막히지 않도록 다음 에디터 틱에 빌드를 시작한다.
    public static string BuildAsync(string outputPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            WriteStatus("failed", "플레이 모드에서는 빌드할 수 없습니다. 플레이 모드를 끄고 다시 실행하세요.");
            return "failed: play mode";
        }

        if (pendingOutputPath != null)
        {
            return "already queued";
        }

        WriteStatus("running", outputPath);
        // delayCall은 에디터가 백그라운드일 때 돌지 않아 update 훅을 쓴다.
        pendingOutputPath = outputPath;
        EditorApplication.update += RunPendingBuild;
        return "queued";
    }

    private static string pendingOutputPath;

    private static void RunPendingBuild()
    {
        EditorApplication.update -= RunPendingBuild;
        string outputPath = pendingOutputPath;
        pendingOutputPath = null;
        if (outputPath == null)
        {
            return;
        }

        bool ok = Build(outputPath, out string message);
        WriteStatus(ok ? "succeeded" : "failed", message);
    }

    public static void BuildFromCommandLine()
    {
        string outputPath = GetArg("-buildOutput");
        if (string.IsNullOrEmpty(outputPath))
        {
            Debug.LogError("[LinuxServerBuild] -buildOutput 인자가 필요합니다.");
            EditorApplication.Exit(1);
            return;
        }

        bool ok = Build(outputPath, out string message);
        Debug.Log($"[LinuxServerBuild] {message}");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static bool Build(string outputPath, out string message)
    {
        // 출력 경로가 폴더면 실행 파일 이름을 붙인다.
        if (!outputPath.EndsWith(".x86_64", StringComparison.Ordinal))
        {
            outputPath = Path.Combine(outputPath, ExecutableName);
        }

        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);
        StandaloneBuildSubtarget previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
        string previousArchitecture = GetLinuxArchitecture();

        try
        {
            // EC2 t3는 Intel이다. ARM64로 두면 서버에서 실행되지 않는다.
            SetLinuxArchitecture("x64");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                message = $"빌드 실패({summary.result}): 에러 {summary.totalErrors}개. Unity 콘솔을 확인하세요.";
                return false;
            }

            message = $"빌드 성공: {outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0}초)";
            return true;
        }
        catch (Exception e)
        {
            message = $"빌드 예외: {e.Message}";
            return false;
        }
        finally
        {
            if (previousArchitecture != null)
            {
                SetLinuxArchitecture(previousArchitecture);
            }

            if (!Application.isBatchMode
                && (EditorUserBuildSettings.activeBuildTarget != previousTarget
                    || EditorUserBuildSettings.standaloneBuildSubtarget != previousSubtarget))
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
                EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);
            }
        }
    }

    // UnityEditor.LinuxStandalone.UserBuildSettings는 Linux 모듈 어셈블리에 있어 리플렉션으로 접근한다.
    private static System.Reflection.PropertyInfo LinuxArchitectureProperty()
    {
        Type type = Type.GetType("UnityEditor.LinuxStandalone.UserBuildSettings, UnityEditor.LinuxStandalone.Extensions");
        return type?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
    }

    private static string GetLinuxArchitecture()
    {
        return LinuxArchitectureProperty()?.GetValue(null)?.ToString();
    }

    private static void SetLinuxArchitecture(string architecture)
    {
        System.Reflection.PropertyInfo property = LinuxArchitectureProperty();
        if (property == null)
        {
            Debug.LogWarning("[LinuxServerBuild] Linux 빌드 모듈 설정을 찾지 못해 아키텍처를 지정하지 못했습니다.");
            return;
        }

        property.SetValue(null, Enum.Parse(property.PropertyType, architecture));
    }

    private static void WriteStatus(string status, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatusFile));
        File.WriteAllText(StatusFile, status + "\n" + message + "\n");
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}
