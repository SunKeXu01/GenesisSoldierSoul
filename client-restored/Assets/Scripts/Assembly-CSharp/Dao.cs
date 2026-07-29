using UnityEngine;

public class Dao : MonoBehaviour
{
	public GameObject option;

	public GameObject BulletPre;

	public Transform BulletPoint;

	public float CD = 0.5f;

	public float CD1 = 1f;

	private float timer;

	public AudioSource music;

	public AudioClip stab;

	public AudioClip slash;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		stab = Resources.Load<AudioClip>("music/reload");
		slash = Resources.Load<AudioClip>("music/slash");
	}

	private void Start()
	{
	}

	private void Update()
	{
		timer += Time.deltaTime;
		if (Input.GetMouseButton(1) && timer > CD1)
		{
			timer = 0f;
			option.SetActive(true);
			Object.Instantiate(BulletPre, BulletPoint.position, BulletPoint.rotation);
			music.clip = slash;
			music.Play();
		}
		if (Input.GetMouseButton(0) && timer > CD)
		{
			timer = 0f;
			option.SetActive(true);
			Object.Instantiate(BulletPre, BulletPoint.position, BulletPoint.rotation);
			music.clip = slash;
			music.Play();
		}
		if ((double)timer > 0.3)
		{
			option.SetActive(false);
		}
	}
}
