// Keeps the MCP for Unity stdio bridge listening whenever this project loads.
//
// Why this exists: the MCP for Unity package only auto-starts its bridge when the
// "Use HTTP transport" preference is OFF, and even then it can miss the start on
// projects that live inside OneDrive (the file watcher that would trigger it does
// not always fire). Without the bridge, MCP clients see "no Unity Editor instances".
//
// This script starts the bridge on every domain reload AND keeps a small watchdog
// running, because a forced recompile can leave the bridge dead until the next
// recompile — which on this project only happens when you press Ctrl+R yourself.
// The watchdog notices a dead bridge within a few seconds and restarts it.
//
// Safe to delete if you ever stop using the Unity MCP integration.

using System;
using UnityEditor;
using MCPForUnity.Editor.Services.Transport.Transports;

[InitializeOnLoad]
internal static class McpBridgeAutoStart
{
    private const double CheckIntervalSeconds = 5.0;
    private const double RetryIntervalSeconds = 15.0;

    private static double nextCheck;
    private static double nextAllowedStart;

    static McpBridgeAutoStart()
    {
        EditorApplication.delayCall += TryStart;
        EditorApplication.update += Watchdog;
    }

    // Runs a few times per second but only does work every CheckIntervalSeconds.
    private static void Watchdog()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextCheck) return;
        nextCheck = now + CheckIntervalSeconds;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (StdioBridgeHost.IsRunning) return;
        if (now < nextAllowedStart) return;

        nextAllowedStart = now + RetryIntervalSeconds;
        TryStart("watchdog");
    }

    private static void TryStart()
    {
        TryStart("load");
    }

    private static void TryStart(string reason)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryStart;
            return;
        }

        // Keep the Unity-side setting aligned with the stdio transport that MCP clients use.
        if (EditorPrefs.GetBool("MCPForUnity.UseHttpTransport", false))
        {
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", false);
        }

        try
        {
            StdioBridgeHost.Start();
            UnityEngine.Debug.Log($"[McpBridgeAutoStart] MCP for Unity stdio bridge start requested ({reason}).");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[McpBridgeAutoStart] Could not start the bridge ({reason}): {e.Message}");
        }
    }
}
