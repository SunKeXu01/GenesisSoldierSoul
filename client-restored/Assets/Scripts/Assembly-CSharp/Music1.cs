using UnityEngine;

public class Music1 : MonoBehaviour
{
	public CharacterController controller;

	public float gravity = -35f;

	public float jumpHeight = 1.3f;

	public Transform groundCheck;

	public float groundDistance = 0.4f;

	public LayerMask groundMask;

	public AudioSource music;

	public AudioClip jump;

	private Vector3 velocity;

	private bool isGrounded;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		jump = Resources.Load<AudioClip>("music/jump");
	}

	private void Update()
	{
		isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
		if (isGrounded && velocity.y < 0f)
		{
			velocity.y = -2f;
		}
		if (Input.GetButtonDown("Jump") && isGrounded)
		{
			velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
			music.clip = jump;
			music.Play();
		}
		velocity.y += gravity * Time.deltaTime;
		controller.Move(velocity * Time.deltaTime);
	}
}
