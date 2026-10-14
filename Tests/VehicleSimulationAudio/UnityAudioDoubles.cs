using System;
using System.Collections.Generic;
using System.Linq;

// Only Unity's object/audio boundary is doubled. The playback and vehicle audio
// lifecycle code are the production sources linked by the test project.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }

    public class Component
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public sealed class GameObject
    {
        private readonly List<Component> components = new List<Component>();
        public readonly string name;
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "Test Vehicle")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            components.Add(transform);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault();
        public IEnumerable<T> AllComponents<T>() where T : Component => components.OfType<T>().Concat(transform.children.SelectMany(child => child.gameObject.AllComponents<T>()));
    }

    public sealed class Transform : Component
    {
        internal readonly List<Transform> children = new List<Transform>();
        public Transform parent { get; private set; }
        public int childCount => children.Count;
        public void SetParent(Transform value, bool worldPositionStays)
        {
            parent?.children.Remove(this);
            parent = value;
            parent?.children.Add(this);
        }
        public Transform Find(string name) => children.FirstOrDefault(child => child.gameObject.name == name);
    }

    public sealed class AudioClip { public readonly string name; public AudioClip(string name) { this.name = name; } }
    public enum AudioRolloffMode { Logarithmic, Linear, Custom }
    public sealed class AudioSource : Component
    {
        public AudioClip clip;
        public Audio.AudioMixerGroup outputAudioMixerGroup;
        public bool playOnAwake = true;
        public bool loop;
        public bool ignoreListenerPause;
        public float dopplerLevel = 1f;
        public float volume = 1f;
        public float pitch = 1f;
        public float spatialBlend;
        public float panStereo;
        public bool spatialize;
        public float minDistance = 1f;
        public float maxDistance = 500f;
        public AudioRolloffMode rolloffMode;
        public bool isPlaying { get; private set; }
        public bool isPaused { get; private set; }
        public int playCalls { get; private set; }
        public int pauseCalls { get; private set; }
        public int unpauseCalls { get; private set; }
        public int stopCalls { get; private set; }
        public void Play() { playCalls++; isPlaying = true; isPaused = false; }
        public void Pause() { pauseCalls++; if (isPlaying) { isPlaying = false; isPaused = true; } }
        public void UnPause() { unpauseCalls++; if (isPaused) { isPlaying = true; isPaused = false; } }
        public void Stop() { stopCalls++; isPlaying = false; isPaused = false; }
    }

    public static class AudioListener { public static bool pause; }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float timeScale = 1f; }
    public static class Mathf
    {
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
}

namespace UnityEngine.Audio
{
    public sealed class AudioMixerGroup { public readonly string name; public AudioMixerGroup(string name) { this.name = name; } }
}

public sealed class AudioManager
{
    public static AudioManager Instance;
    public UnityEngine.Audio.AudioMixerGroup SfxOutput;
}
