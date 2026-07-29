using UnityEngine;

public class Diren : MonoBehaviour
{
	public float HP = 100f;

	public float CD = 0.5f;

	public float CD1 = 0.3f;

	public float timer;

	private float lastTime;

	private float curTime;

	public GameObject BulletPre;

	public Transform BulletPoint;

	public GameObject option;

	public GameObject optione;

	public GameObject ZhunXing;

	private void OnTriggerEnter(Collider other)
	{
		timer += Time.deltaTime;
		timer = 0f;
		ZhunXing.SetActive(true);
		HP -= 46f;
		if (HP <= 0f)
		{
			Object.Instantiate(BulletPre, BulletPoint.position, BulletPoint.rotation);
			option.SetActive(true);
			optione.SetActive(true);
			Object.Destroy(base.gameObject);
		}
	}
}
