using System.Collections;
using UnityEngine;

namespace GenesisSoldierSoul.Multiplayer
{
    /// <summary>
    /// Preserves recovered scene ambience without invoking AudioSource's
    /// play-on-awake path before WebGL has decoded the referenced clip.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GenesisDeferredAudioSource : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var source = GetComponent<AudioSource>();
            if (source == null || source.clip == null)
                yield break;
            source.playOnAwake = false;
            var clip = source.clip;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            var timeout = Time.realtimeSinceStartup + 8f;
            while (clip.loadState == AudioDataLoadState.Loading
                && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            if (source != null
                && source.enabled
                && source.gameObject.activeInHierarchy
                && clip.loadState == AudioDataLoadState.Loaded)
            {
                source.Play();
            }
        }
    }

    /// <summary>
    /// Queues short action sounds until WebGL has decoded their AudioClip.
    /// Unlike ambient playback, one-shots must preserve the volume requested by
    /// the authoritative action that triggered them.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class GenesisDeferredOneShotAudio : MonoBehaviour
    {
        private AudioSource source;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
        }

        public void Play(AudioClip clip, float volume)
        {
            if (clip == null)
                return;
            if (source == null)
                source = GetComponent<AudioSource>();
            if (source == null)
                return;
            if (clip.loadState == AudioDataLoadState.Loaded)
            {
                source.PlayOneShot(clip, volume);
                return;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            StartCoroutine(PlayWhenLoaded(clip, volume));
        }

        private IEnumerator PlayWhenLoaded(AudioClip clip, float volume)
        {
            var timeout = Time.realtimeSinceStartup + 8f;
            while (clip != null
                && clip.loadState == AudioDataLoadState.Loading
                && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }
            if (clip != null
                && source != null
                && source.enabled
                && source.gameObject.activeInHierarchy
                && clip.loadState == AudioDataLoadState.Loaded)
            {
                source.PlayOneShot(clip, volume);
            }
        }
    }
}
