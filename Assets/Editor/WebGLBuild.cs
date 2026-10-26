using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    [MenuItem("Tools/Factory/Build WebGL for GitHub Pages")]
    public static void Build()
    {
        string output = Environment.GetEnvironmentVariable("DEMO2_WEBGL_OUTPUT");
        if (string.IsNullOrEmpty(output)) output = "Builds/WebGL";
        // GitHub Pages cannot set the Content-Encoding headers required by
        // precompressed Unity files. Include Unity's JavaScript fallback.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.initialMemorySize = 256;
        PlayerSettings.WebGL.maximumMemorySize = 2048;
        PlayerSettings.runInBackground = true;
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes to build.");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            target = BuildTarget.WebGL,
            locationPathName = output,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("WebGL build failed: " + report.summary.result);
        File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
        Debug.Log("WebGL build ready: " + Path.GetFullPath(output));
    }
}
