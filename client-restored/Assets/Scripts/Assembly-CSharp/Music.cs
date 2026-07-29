using UnityEngine;

public class Music : MonoBehaviour
{
	public AudioSource music;

	public AudioClip SYA;

	public AudioClip Tai;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		SYA = Resources.Load<AudioClip>("music/See You Again");
		Tai = Resources.Load<AudioClip>("music/Tai2");
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.T))
		{
			music.clip = SYA;
			music.loop = true;
			music.Play();
		}
		if (Input.GetKeyDown(KeyCode.Q))
		{
			music.clip = Tai;
			music.loop = true;
			music.Play();
		}
		if (Input.GetKeyUp(KeyCode.Q))
		{
			music.clip = Tai;
			music.Stop();
		}
	}
}
