using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Global click feedback for every UI button in the game — menu, deck builder, sealed, and
/// the in-match side panel (Skip, End Turn, and the rest).
///
/// Deliberately a single global watcher rather than a line added to each button. There are ~140
/// onClick.AddListener sites across ten files and three separate AddButton factories, so wiring it
/// per call site would miss the inline ones immediately and drift out of date the first time someone
/// adds a button. This hooks the pointer instead: on mouse-down it asks the EventSystem what is
/// under the cursor and plays the click if the topmost hit belongs to an interactable Button.
///
/// Two consequences of that design worth knowing:
///   • It fires on mouse-DOWN, not on the button's own onClick. That is the responsive feel you want
///     from button feedback, and it means a press that drags off the button still clicked audibly —
///     which is what physical buttons do.
///   • A DISABLED button stays silent, because the topmost hit is found and then rejected rather
///     than falling through to whatever is behind it. Greyed-out controls must not sound available.
///
/// Its own AudioSource on a DontDestroyOnLoad object, so it survives scene changes and never
/// borrows (or re-pitches) a manager's shared source.</summary>
public sealed class UiSfx : MonoBehaviour
{
    private const string ClipName = "ui_click";

    /// <summary>Played at this fraction of the shared SFX level. The click fires on essentially every
    /// interaction, so at parity with one-off sounds like an attack or a burn it wears on you fast.
    /// A relative scale rather than a quieter asset, so it keeps tracking the player's volume setting
    /// and the ratio to everything else stays fixed if that setting changes.</summary>
    private const float ClickVolumeScale = 0.7f;

    private static UiSfx _instance;

    private AudioSource source;
    private AudioClip clickClip;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();

    /// <summary>Create the watcher if it does not exist yet. Safe to call from every scene's
    /// startup; only the first call does anything.</summary>
    public static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("UI SFX");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<UiSfx>();
    }

    /// <summary>Play the click explicitly — for a control that is not a Button (a custom pointer
    /// handler, say) but should still sound like one.</summary>
    public static void Click()
    {
        Ensure();
        if (_instance != null) _instance.Fire();
    }

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "sfx", ClipName + ".wav");
        if (!System.IO.File.Exists(path)) yield break;
        using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                clickClip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
        }
    }

    private void Fire()
    {
        if (clickClip == null || source == null) return;
        source.PlayOneShot(clickClip, GameManager.SfxVolume * ClickVolumeScale);
    }

    private void Update()
    {
        if (clickClip == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        var es = EventSystem.current;
        if (es == null) return;

        hits.Clear();
        es.RaycastAll(new PointerEventData(es) { position = Input.mousePosition }, hits);
        for (int i = 0; i < hits.Count; i++)
        {
            var go = hits[i].gameObject;
            if (go == null) continue;
            var btn = go.GetComponentInParent<Button>();
            if (btn == null) continue;
            // The FIRST button found wins and then we stop, interactable or not: a disabled control
            // must not play a sound, and must not let a button behind it play one either.
            if (btn.IsInteractable()) Fire();
            return;
        }
    }
}
