using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Deterministic visual smoke test for the real uGUI arrow and production
/// shader. Run from Tools/Arrow Beam/Capture visual check, or in batch mode:
/// -executeMethod TargetingArrowCapture.Capture.
/// </summary>
public static class TargetingArrowCapture
{
    const int Width = 1100;
    const int Height = 700;

    [MenuItem("Tools/Arrow Beam/Capture visual check")]
    public static void Capture()
    {
        string output = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Library", "ArrowBeamChecks", "targeting-arrow-visual-check.png");

        var cameraObject = new GameObject("Arrow Capture Camera");
        var canvasObject = new GameObject("Arrow Capture Canvas");
        var camera = cameraObject.AddComponent<Camera>();
        var target = new RenderTexture(Width, Height, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 4
        };

        try
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(12, 27, 46, 255);
            camera.orthographic = true;
            camera.orthographicSize = Height * 0.5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.targetTexture = target;

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            CreateArrow(canvas.transform, new Vector2(-485f, -245f),
                new Vector2(-180f, 245f), TargetingArrowGraphic.ArrowState.Aim);
            CreateArrow(canvas.transform, new Vector2(-175f, -245f),
                new Vector2(20f, 65f), TargetingArrowGraphic.ArrowState.Valid);
            CreateArrow(canvas.transform, new Vector2(75f, -245f),
                new Vector2(350f, 185f), TargetingArrowGraphic.ArrowState.Invalid);
            CreateArrow(canvas.transform, new Vector2(360f, -245f),
                new Vector2(485f, -45f), TargetingArrowGraphic.ArrowState.Aim);

            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = previous;
            Debug.Log("[ArrowBeam] visual check written to " + output);
        }
        finally
        {
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static void CreateArrow(Transform parent, Vector2 from, Vector2 to,
                            TargetingArrowGraphic.ArrowState state)
    {
        var go = new GameObject("Captured " + state, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var arrow = go.AddComponent<TargetingArrowGraphic>();
        MethodInfo rebuild = typeof(TargetingArrowGraphic).GetMethod(
            "Rebuild", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo push = typeof(TargetingArrowGraphic).GetMethod(
            "Push", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo stateField = typeof(TargetingArrowGraphic).GetField(
            "state", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo curField = typeof(TargetingArrowGraphic).GetField(
            "cur", BindingFlags.Instance | BindingFlags.NonPublic);

        stateField.SetValue(arrow, state);
        TargetingArrowGraphic.Ramp ramp = state == TargetingArrowGraphic.ArrowState.Valid
            ? arrow.valid
            : state == TargetingArrowGraphic.ArrowState.Invalid
                ? arrow.invalid
                : arrow.aim;
        curField.SetValue(arrow, ramp);
        rebuild.Invoke(arrow, new object[] { from, to });
        push.Invoke(arrow, new object[] { -0.2f, 1f });
    }
}
