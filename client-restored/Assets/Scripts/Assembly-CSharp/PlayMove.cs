using UnityEngine;

public class PlayMove : MonoBehaviour
{
	public CharacterController controller;

	public float speed = 3.2f;

	private float lastTime;

	private float curTime;

	public AudioSource music;

	public AudioClip walk;

	private Vector3 velocity;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		walk = Resources.Load<AudioClip>("music/walk");
	}

	private void Start()
	{
	}

	private void Update()
	{
		float axis = Input.GetAxis("Horizontal");
		float axis2 = Input.GetAxis("Vertical");
		Vector3 vector = base.transform.right * axis + base.transform.forward * axis2;
		controller.Move(vector * speed * Time.deltaTime);
		controller.Move(velocity * Time.deltaTime);
	}
}
