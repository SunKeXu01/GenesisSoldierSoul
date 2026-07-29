using UnityEngine;

public class Sound2 : MonoBehaviour
{
	public float CD = 0.5f;

	public float CD1 = 1f;

	private float timer;

	public AudioSource music;

	public AudioClip stab;

	public AudioClip slash;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		stab = Resources.Load<AudioClip>("music/stab");
		slash = Resources.Load<AudioClip>("music/slash");
	}

	private void Update()
	{
		if (Input.GetMouseButton(1) && timer > CD1)
		{
			timer = 0f;
			music.clip = stab;
			music.Play();
		}
		timer += Time.deltaTime;
		if (Input.GetMouseButton(0) && timer > CD)
		{
			timer = 0f;
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			music.clip = slash;
			music.Play();
		}
	}
}
