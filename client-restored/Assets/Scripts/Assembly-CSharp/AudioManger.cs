using UnityEngine;

public class AudioManger : MonoBehaviour
{
	public static AudioManger Instance;

	public AudioSource MusicPlayer;

	public AudioSource SoundPlayer;

	private void Start()
	{
		Instance = this;
	}

	public void PlayMusic(string name)
	{
		if (!MusicPlayer.isPlaying)
		{
			AudioClip clip = Resources.Load<AudioClip>(name);
			MusicPlayer.clip = clip;
			MusicPlayer.Play();
		}
	}

	public void StopMusic()
	{
		MusicPlayer.Stop();
	}

	public void PlaySound(string name)
	{
		AudioClip clip = Resources.Load<AudioClip>(name);
		SoundPlayer.PlayOneShot(clip);
	}
}
