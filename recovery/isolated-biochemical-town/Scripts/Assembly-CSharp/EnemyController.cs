using UnityEngine;
using UnityEngine.AI;

public class EnemyController : MonoBehaviour
{
	private const int ATTACK_DISTANCE = 4;

	private const int RUN_TO_ROLE_DISTANCE = 50;

	private const int BlOOD_REDUCE_ATTACK1 = 6;

	private const int BlOOD_REDUCE_ATTACK2 = 8;

	private const int BlOOD_REDUCE_ATTACK3 = 10;

	private const int STATE_IDLE = 1;

	private const int STATE_RUN = 2;

	private const int STATE_ATTACK = 3;

	private const int STATE_DEAD = 4;

	private const int UNDER_ATTACK = 5;

	private int currentState;

	private Animation ani;

	public GameObject role;

	private Vector3 destination;

	private NavMeshAgent agent;

	private bool isAttacked;

	private bool isAttacking;

	private void Start()
	{
		ani = GetComponent<Animation>();
		agent = GetComponent<NavMeshAgent>();
		destination = agent.destination;
		role = GameObject.Find("FPSController");
	}

	private void Update()
	{
		checkState();
		checkAttack();
		handlerAction();
	}

	private void checkState()
	{
		if (Vector3.Distance(role.transform.position, base.transform.position) <= 50f && Vector3.Distance(role.transform.position, base.transform.position) > 4f && !isAttacked)
		{
			currentState = 2;
			isAttacking = true;
		}
		else if (Vector3.Distance(role.transform.position, base.transform.position) <= 4f && !isAttacked)
		{
			currentState = 3;
		}
		else if (Vector3.Distance(role.transform.position, base.transform.position) > 4f && isAttacking && !isAttacked)
		{
			currentState = 2;
		}
		else if (isAttacked)
		{
			currentState = 5;
		}
		else
		{
			currentState = 1;
		}
	}

	private void checkAttack()
	{
		if (isAttacking && Vector3.Distance(role.transform.position, base.transform.position) > 4f)
		{
			run();
		}
	}

	private void run()
	{
		ani.CrossFade("run", 0.1f, PlayMode.StopAll);
		destination = role.transform.position;
		agent.destination = destination;
	}

	private void handlerAction()
	{
		switch (currentState)
		{
		case 1:
			ani.Play("dance");
			break;
		case 2:
			run();
			break;
		case 3:
			if (!ani.isPlaying)
			{
				attack();
			}
			break;
		case 4:
			dead();
			break;
		case 5:
			ani.Play("die");
			break;
		default:
			Debug.Log("error state = " + currentState);
			break;
		}
	}

	private void attack()
	{
		isAttacking = true;
		switch (Random.Range(1, 4))
		{
		case 1:
			ani.Play("attack");
			role.SendMessage("reduceBlood", 6);
			break;
		case 2:
			ani.Play("attack");
			role.SendMessage("reduceBlood", 8);
			break;
		case 3:
			ani.Play("attack");
			role.SendMessage("reduceBlood", 10);
			break;
		default:
			Debug.Log("error state = " + currentState);
			break;
		}
	}

	public void dead()
	{
		isAttacked = true;
		agent.destination = base.transform.position;
		isAttacking = false;
		agent.enabled = false;
		float t = 1.6f;
		Object.Destroy(base.gameObject, t);
	}
}
