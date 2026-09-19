using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SocialNotificationSfxPlayModeTests
{
    [UnityTest]
    public IEnumerator NotificationComponent_LoadsBothStreamingAssetsThroughUnity()
    {
        Type componentType = Type.GetType("SocialNotificationSfx, Assembly-CSharp");
        Assert.That(componentType, Is.Not.Null);

        componentType.GetMethod("EnsureRunning", BindingFlags.Static | BindingFlags.Public)
            ?.Invoke(null, null);
        yield return null;
        Component component = Resources.FindObjectsOfTypeAll(componentType)
            .OfType<Component>().FirstOrDefault();
        Assert.That(component, Is.Not.Null, "The persistent social-audio component was not created.");
        FieldInfo inviteField = componentType.GetField("lobbyInviteClip",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo messageField = componentType.GetField("friendMessageClip",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(inviteField, Is.Not.Null);
        Assert.That(messageField, Is.Not.Null);

        float deadline = Time.realtimeSinceStartup + 3f;
        while ((inviteField.GetValue(component) == null || messageField.GetValue(component) == null)
               && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(inviteField.GetValue(component), Is.TypeOf<AudioClip>(),
            "Unity did not load the lobby-invite WAV from StreamingAssets.");
        Assert.That(messageField.GetValue(component), Is.TypeOf<AudioClip>(),
            "Unity did not load the friend-message WAV from StreamingAssets.");
    }

    [Test]
    public void CueGate_PlaysEachRemoteInviteAndMessageOnlyOnce()
    {
        Type gateType = Type.GetType("SocialNotificationCueGate, Assembly-CSharp");
        Assert.That(gateType, Is.Not.Null, "Social notification cue gate was not compiled.");

        object gate = Activator.CreateInstance(gateType);
        MethodInfo observeInvite = gateType.GetMethod("ObserveInvite");
        MethodInfo observeMessage = gateType.GetMethod("ObserveMessage");
        Assert.That(observeInvite, Is.Not.Null);
        Assert.That(observeMessage, Is.Not.Null);

        Assert.That(Observe(observeInvite, gate, "invite-1"), Is.True,
            "A newly received lobby invite should play its cue.");
        Assert.That(Observe(observeInvite, gate, "invite-1"), Is.False,
            "Polling the same invite again must stay silent.");
        Assert.That(Observe(observeInvite, gate, "invite-2"), Is.True);
        Assert.That(Observe(observeInvite, gate, ""), Is.False);

        Assert.That(Observe(observeMessage, gate, "friend-a", 10L, false), Is.True,
            "A newly received remote message should play its cue.");
        Assert.That(Observe(observeMessage, gate, "friend-a", 10L, false), Is.False,
            "Polling the same remote message again must stay silent.");
        Assert.That(Observe(observeMessage, gate, "friend-a", 9L, false), Is.False,
            "Older history pages must stay silent.");
        Assert.That(Observe(observeMessage, gate, "friend-a", 11L, false), Is.True);
        Assert.That(Observe(observeMessage, gate, "friend-b", 1L, false), Is.True,
            "Each sender has an independent latest-message cursor.");
        Assert.That(Observe(observeMessage, gate, "friend-a", 12L, true), Is.False,
            "A locally sent message must never play the incoming-message cue.");
        Assert.That(Observe(observeMessage, gate, "", 12L, false), Is.False);
        Assert.That(Observe(observeMessage, gate, "friend-a", 0L, false), Is.False);
    }

    [TestCase("social_lobby_invite.wav")]
    [TestCase("social_friend_message.wav")]
    public void NotificationWav_IsShortNormalizedPcmWithoutClipping(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "sfx", fileName);
        Assert.That(File.Exists(path), Is.True, $"Missing notification clip: {path}");

        WavMetrics wav = ReadPcm16Wav(path);
        Assert.That(wav.Channels, Is.EqualTo(2));
        Assert.That(wav.SampleRate, Is.EqualTo(48000));
        Assert.That(wav.BitsPerSample, Is.EqualTo(16));
        Assert.That(wav.DurationSeconds, Is.InRange(0.30, 0.38),
            "Notification cues should not retain the source MP3's long trailing silence.");
        Assert.That(wav.Peak, Is.GreaterThan(0.45), "Cue should be clearly audible.");
        Assert.That(wav.Peak, Is.LessThan(0.999), "Cue must retain headroom and not clip.");
    }

    private static bool Observe(MethodInfo method, object target, params object[] args)
    {
        return (bool)method.Invoke(target, args);
    }

    private sealed class WavMetrics
    {
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
        public double DurationSeconds;
        public double Peak;
    }

    private static WavMetrics ReadPcm16Wav(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            Assert.That(FourCc(reader), Is.EqualTo("RIFF"));
            reader.ReadUInt32();
            Assert.That(FourCc(reader), Is.EqualTo("WAVE"));

            ushort format = 0;
            ushort channels = 0;
            int sampleRate = 0;
            ushort bits = 0;
            byte[] data = null;

            while (stream.Position + 8 <= stream.Length)
            {
                string chunk = FourCc(reader);
                uint size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1u);
                if (chunk == "fmt ")
                {
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadUInt32();
                    reader.ReadUInt16();
                    bits = reader.ReadUInt16();
                }
                else if (chunk == "data")
                {
                    data = reader.ReadBytes((int)size);
                }
                stream.Position = Math.Min(next, stream.Length);
            }

            Assert.That(format, Is.EqualTo(1), "Notification WAV must be uncompressed PCM.");
            Assert.That(data, Is.Not.Null.And.Not.Empty);
            Assert.That(bits, Is.EqualTo(16), "Peak verification expects signed PCM16 samples.");

            int peak = 0;
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                int sample = Math.Abs((int)BitConverter.ToInt16(data, i));
                if (sample > peak) peak = sample;
            }

            return new WavMetrics
            {
                Channels = channels,
                SampleRate = sampleRate,
                BitsPerSample = bits,
                DurationSeconds = data.Length / (double)(sampleRate * channels * (bits / 8)),
                Peak = peak / 32768.0
            };
        }
    }

    private static string FourCc(BinaryReader reader)
    {
        return new string(reader.ReadChars(4));
    }
}
