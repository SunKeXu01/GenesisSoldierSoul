using UnityEngine;

public class RoleBulletController : MonoBehaviour
{
	private int bullets = 100;

	public static int score;

	public Rigidbody bullet;

	private GameObject firePoint;

	public Texture2D texture;

	private int blood = 100;

	private bool isShowBlood = true;

	public Texture2D bloodBgTexture;

	public Texture2D bloodTexture;

	public Texture2D AllRed;

	private float howAlpha;

	public GameObject Gun;

	private void Start()
	{
		firePoint = GameObject.Find("firePoint");
	}

	private void Update()
	{
		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		if (Input.GetMouseButtonDown(0) && bullets > 0)
		{
			bullets--;
			Vector3 point = ray.GetPoint(20f);
			Object.Instantiate(bullet, firePoint.transform.position, firePoint.transform.rotation).velocity = (point - firePoint.transform.position) * 3f;
			Gun.SendMessage("shootAudio");
			RaycastHit hitInfo;
			if (Physics.Raycast(ray, out hitInfo, 100f, 512))
			{
				Debug.Log(hitInfo.normal);
				Object.Destroy(hitInfo.collider);
				score++;
				hitInfo.transform.gameObject.GetComponent<EnemyController>().dead();
			}
		}
		firePoint.transform.LookAt(Camera.main.ScreenPointToRay(Input.mousePosition).GetPoint(20f));
		if (score >= 50)
		{
			Debug.Log("You Win!");
			Application.LoadLevel("YouWin");
		}
		if (blood <= 0 || bullets <= 0)
		{
			Debug.Log("Game Over!");
			Application.LoadLevel("GameOver");
		}
	}

	public void addBlood()
	{
		if (blood > 50)
		{
			blood = 100;
		}
		else
		{
			blood += 50;
		}
	}

	public void reduceBlood(int attackType)
	{
		switch (attackType)
		{
		case 6:
			blood -= 6;
			break;
		case 8:
			blood -= 8;
			break;
		case 10:
			blood -= 10;
			break;
		default:
			Debug.Log("blood error");
			break;
		}
	}

	private void OnGUI()
	{
		GUI.Label(new Rect(10f, Screen.height - 30, 150f, 50f), "子弹个数 x" + bullets + "   分数" + score);
		GUI.DrawTexture(new Rect(Input.mousePosition.x - (float)(texture.width / 2), (float)Screen.height - Input.mousePosition.y - (float)(texture.height / 2), texture.width, texture.height), texture);
		GUI.DrawTexture(new Rect(0f, 0f, bloodBgTexture.width, bloodBgTexture.height), bloodBgTexture);
		GUI.DrawTexture(new Rect(0f, 0f, (float)bloodTexture.width * ((float)blood * 0.01f), bloodTexture.height), bloodTexture);
		Color color = GUI.color;
		howAlpha = (100f - (float)blood) / 120f;
		if ((double)howAlpha < 0.42)
		{
			howAlpha = 0f;
		}
		color.a = howAlpha;
		GUI.color = color;
		GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), AllRed);
	}
}
