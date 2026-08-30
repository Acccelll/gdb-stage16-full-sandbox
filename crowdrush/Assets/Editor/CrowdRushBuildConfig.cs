#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class CrowdRushBuildConfig
{
    static CrowdRushBuildConfig()
    {
        Apply();
    }

    [MenuItem("CrowdRush/Apply Android Build Settings")]
    public static void Apply()
    {
        PlayerSettings.companyName = "CrowdRush";
        PlayerSettings.productName = "Crowd Rush";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.crowdrush.game");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.buildAppBundle = false;
    }
}

public sealed class CrowdRushPreBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        CrowdRushBuildConfig.Apply();
    }
}
#endif
