using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
	[SerializeField]
	private Transform target;

	public float endsVelocity = -10f;

	public float runSpeed = 2f;

	public float walkSpeed = 4f;

	private float moveSpeed;

	private CharacterController character;

	public Transform groundCheck;

	public float groundDistance = 0.4f;

	public LayerMask groundMask;

	public AudioSource music;

	public AudioClip walk;

	private Vector3 velocity;

	private bool isGrounded;

	private void Awake()
	{
		music = base.gameObject.AddComponent<AudioSource>();
		walk = Resources.Load<AudioClip>("music/walk");
	}

	private void Start()
	{
		character = GetComponent<CharacterController>();
		moveSpeed = walkSpeed;
	}

	private void Update()
	{
		isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
		if (isGrounded && velocity.y < 0f)
		{
			velocity.y = -2f;
		}
		Vector3 vector = Vector3.zero;
		float axis = Input.GetAxis("Horizontal");
		float axis2 = Input.GetAxis("Vertical");
		if (axis != 0f || axis2 != 0f)
		{
			vector.x = axis * moveSpeed;
			vector.z = axis2 * moveSpeed;
			vector = Vector3.ClampMagnitude(vector, moveSpeed);
			vector = target.TransformDirection(vector);
		}
		if (Input.GetKey(KeyCode.CapsLock))
		{
			moveSpeed = runSpeed;
		}
		else
		{
			moveSpeed = walkSpeed;
		}
		if (Input.GetKey(KeyCode.Space))
		{
			music.Stop();
		}
		if (Input.GetKeyDown(KeyCode.W))
		{
			music.clip = walk;
			music.loop = true;
			music.Play();
		}
		if (Input.GetKeyDown(KeyCode.S))
		{
			music.clip = walk;
			music.loop = true;
			music.Play();
		}
		if (Input.GetKeyDown(KeyCode.A))
		{
			music.clip = walk;
			music.loop = true;
			music.Play();
		}
		if (Input.GetKeyDown(KeyCode.D))
		{
			music.clip = walk;
			music.loop = true;
			music.Play();
		}
		if (!Input.anyKey && music.isPlaying)
		{
			music.Stop();
		}
		vector *= Time.deltaTime;
		character.Move(vector);
	}
}
