using UnityEngine;

public class PlayerSound : MonoBehaviour
{
	private Rigidbody rBody;

	public Transform groundCheck;

	public float groundDistance = 0.4f;

	public LayerMask groundMask;

	private Vector3 velocity;

	private bool isGround;

	private void Start()
	{
		rBody = GetComponent<Rigidbody>();
	}

	private void Update()
	{
		isGround = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
		if (isGround && velocity.y < 0f)
		{
			velocity.y = -2f;
		}
		if (rBody.velocity != Vector3.zero && isGround)
		{
			AudioManger.Instance.PlayMusic("walk");
		}
	}

	private void OnTrigerEnter(Collider other)
	{
		isGround = true;
	}

	private void OnTrigerExit(Collider other)
	{
		isGround = false;
	}
}
