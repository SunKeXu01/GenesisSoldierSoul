using UnityEngine;

public class MouseLook : MonoBehaviour
{
	public float mouseSensitivity = 100f;

	public Transform playerBody;

	public Transform Leida;

	private float xRatation;

	private float zRatation;

	private void Start()
	{
		Cursor.lockState = CursorLockMode.Locked;
	}

	private void Update()
	{
		float num = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
		float num2 = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;
		xRatation -= num2;
		xRatation = Mathf.Clamp(xRatation, -90f, 90f);
		base.transform.localRotation = Quaternion.Euler(xRatation, 0f, 0f);
		playerBody.Rotate(Vector3.up * num);
		float num3 = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
		Leida.Rotate(new Vector3(0f, 0f, 1f) * num3);
	}
}
