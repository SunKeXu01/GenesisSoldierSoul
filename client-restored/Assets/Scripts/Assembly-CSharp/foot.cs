using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(AudioSource))]
public class foot : MonoBehaviour
{
	private AudioSource _audioSources;

	public AudioMixerGroup _audioMixerGroup;

	private float currentSecond;

	private void Start()
	{
		_audioSources = GetComponent<AudioSource>();
		if (_audioSources == null)
		{
			Debug.LogWarning("audioSources is null");
			return;
		}
		if (_audioMixerGroup != null)
		{
			_audioSources.outputAudioMixerGroup = _audioMixerGroup;
		}
		AudioClip audioClip = Resources.Load("WW") as AudioClip;
		if (audioClip == null)
		{
			Debug.LogWarning("audioClip is null");
			return;
		}
		_audioSources.clip = audioClip;
		_audioSources.playOnAwake = false;
		_audioSources.loop = true;
		_audioSources.priority = 128;
		_audioSources.volume = 1f;
		_audioSources.pitch = 1f;
		_audioSources.spatialBlend = 0f;
		_audioSources.minDistance = 1f;
		_audioSources.maxDistance = 100f;
		_audioSources.rolloffMode = AudioRolloffMode.Linear;
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.W) && _audioSources != null)
		{
			_audioSources.time = currentSecond;
			_audioSources.Play();
		}
		if (Input.GetKeyDown(KeyCode.Space) && _audioSources != null && _audioSources.isPlaying)
		{
			currentSecond = _audioSources.time;
			_audioSources.Pause();
		}
		if (Input.GetKeyDown(KeyCode.Space) && _audioSources != null && _audioSources.isPlaying)
		{
			_audioSources.Pause();
			AudioClip clip = _audioSources.clip;
			_audioSources.clip = null;
			Resources.UnloadAsset(clip);
		}
	}
}
