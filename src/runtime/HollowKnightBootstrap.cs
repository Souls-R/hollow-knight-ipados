using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class HollowKnightBootstrap : MonoBehaviour
{
    static bool installed;
    static string logPath;
    static readonly object gate = new object();
    static long bytes;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        NativePreferences.Initialize();
        logPath = Path.Combine(Application.persistentDataPath, "native.log");
        try { File.WriteAllText(logPath, "Hollow Knight native iOS experimental build\n"); } catch { }
        Application.logMessageReceivedThreaded += Log;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Debug.Log("HK_SCENE: " + scene.name + "; " + mode);
        NativeAnimatorCompatibility.Install();
        try { NativeInputCompatibility.Install(); }
        catch (Exception error) { Debug.LogException(error); }
        Debug.Log("HK_NATIVE: Unity=" + Application.unityVersion + "; platform=" + Application.platform);
    }

    public static void Boot()
    {
        var host = new GameObject("HollowKnightNativeCompatibility");
        DontDestroyOnLoad(host);
        host.AddComponent<HollowKnightBootstrap>();
        host.AddComponent<NativeAnimatorCompatibility>();
        host.AddComponent<NativeDisplaySettings>();
        host.AddComponent<NativeSkins>();
        host.AddComponent<NativeEnemyHud>();
        if (NativePreferences.StartupDetails) host.AddComponent<NativeDiagnosticOverlay>();
        if (NativePreferences.ResourceLogs) host.AddComponent<NativePerformanceSampler>();
    }

    static void Log(string message, string trace, LogType type)
    {
        lock (gate)
        {
            bool error = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            // Selection/repair summaries are sparse and useful even when the
            // optional per-frame diagnostics are disabled.
            if (!error && !NativePreferences.Diagnostics && !message.StartsWith("HK_SKIN:",StringComparison.Ordinal) && !message.StartsWith("HK_ENEMY_HUD:",StringComparison.Ordinal)) return;
            if (NativePreferences.StartupDetails) NativeDiagnosticOverlay.Add(message);
            if (bytes > 4 * 1024 * 1024) return;
            string line = DateTime.UtcNow.ToString("O") + " " + type + ": " + message + "\n";
            if (type == LogType.Error || type == LogType.Exception) line += trace + "\n";
            try { File.AppendAllText(logPath, line); bytes += line.Length; } catch { }
        }
    }
}

// Created only when enabled in Settings.bundle; no disabled diagnostic Update/OnGUI.
public sealed class NativeDiagnosticOverlay : MonoBehaviour
{
    static readonly Queue<string> lines = new Queue<string>();
    static string text = "";
    GUIStyle style;
    public static void Add(string message)
    {
        lock (lines) {
            lines.Enqueue(message);
            while (lines.Count > 8) lines.Dequeue();
            text = string.Join("\n", lines.ToArray());
        }
    }
    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = Math.Max(14, Screen.height / 65), wordWrap = true };
        GUI.Label(new Rect(12, 12, Screen.width - 24, Screen.height * .4f), text, style);
    }
}
public sealed class NativePerformanceSampler : MonoBehaviour
{
    float elapsed;
    int frames;
    void LateUpdate()
    {
        elapsed += Time.unscaledDeltaTime;
        frames++;
        if (elapsed < 10f) return;
        Debug.Log("HK_PERF: scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            + "; fps=" + (frames / elapsed).ToString("F1") + "; targetFPS=" + Application.targetFrameRate
            + "; render=" + Display.main.renderingWidth + "x" + Display.main.renderingHeight
            + "; managedMB=" + GC.GetTotalMemory(false) / 1048576);
        frames = 0; elapsed = 0;
    }
}
