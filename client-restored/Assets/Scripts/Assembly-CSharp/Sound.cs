using UnityEngine;

public class Sound : MonoBehaviour
{
	public AudioSource music;

	public AudioClip stab;

	public AudioClip slash;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		stab = Resources.Load<AudioClip>("music/reload");
		slash = Resources.Load<AudioClip>("music/fire");
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.R))
		{
			music.clip = stab;
			music.Play();
		}
		if (Input.GetKeyDown(KeyCode.Mouse0))
		{
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			music.clip = slash;
			music.Play();
		}
	}
}
