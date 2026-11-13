using System;
using DogfightThreatAI.UI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>独立服进入联网大厅。普通客户端与自动化测试不会因 batchmode 自动开服。</summary>
public static class DedicatedServerBootstrap
{
    public static bool Requested
    {
        get
        {
#if UNITY_SERVER
            return true;
#else
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-dogfightServer") >= 0;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnterLobby()
    {
        if (!Requested || SceneManager.GetActiveScene().name == SceneNavigation.LobbyScene)
        {
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(SceneNavigation.LobbyScenePath))
        {
            Debug.LogError("独立服启动失败：请把 LobbyScene 加入 Build Settings。");
            return;
        }
        SceneManager.LoadScene(SceneNavigation.LobbyScenePath);
    }
}
