using System.Collections.Generic;
using UnityEngine;

public class vp_FPInput : vp_Component
{
	public Vector2 MouseLookSensitivity = new Vector2(5f, 5f);

	public bool MouseLookMutePitch;

	public bool MouseLookMuteYaw;

	public int MouseLookSmoothSteps = 10;

	public float MouseLookSmoothWeight = 0.5f;

	public bool MouseLookAcceleration;

	public float MouseLookAccelerationThreshold = 0.4f;

	public bool MouseLookInvert;

	protected Vector2 m_MouseLookSmoothMove = Vector2.zero;

	protected Vector2 m_MouseLookRawMove = Vector2.zero;

	protected List<Vector2> m_MouseLookSmoothBuffer = new List<Vector2>();

	protected int m_LastMouseLookFrame = -1;

	protected Vector2 m_CurrentMouseLook = Vector2.zero;

	public Rect[] MouseCursorZones;

	public bool MouseCursorForced;

	public bool MouseCursorBlocksMouseLook = true;

	protected Vector2 m_MousePos = Vector2.zero;

	protected Vector2 m_MoveVector = Vector2.zero;

	protected bool m_AllowGameplayInput = true;

	protected vp_FPPlayerEventHandler m_FPPlayer;

	public Vector2 MousePos
	{
		get
		{
			return m_MousePos;
		}
	}

	public bool AllowGameplayInput
	{
		get
		{
			return m_AllowGameplayInput;
		}
		set
		{
			m_AllowGameplayInput = value;
		}
	}

	public vp_FPPlayerEventHandler FPPlayer
	{
		get
		{
			if (m_FPPlayer == null)
			{
				m_FPPlayer = base.transform.root.GetComponentInChildren<vp_FPPlayerEventHandler>();
			}
			return m_FPPlayer;
		}
	}

	protected virtual Vector2 OnValue_InputMoveVector
	{
		get
		{
			return m_MoveVector;
		}
		set
		{
			switch (vp_Input.Instance.ControlType)
			{
			case 0:
				m_MoveVector = ((value != Vector2.zero) ? value.normalized : value);
				break;
			case 1:
				m_MoveVector = ((value.sqrMagnitude > 1f) ? value.normalized : value);
				break;
			}
		}
	}

	protected virtual float OnValue_InputClimbVector
	{
		get
		{
			return vp_Input.GetAxisRaw("Vertical");
		}
	}

	protected virtual bool OnValue_InputAllowGameplay
	{
		get
		{
			return m_AllowGameplayInput;
		}
		set
		{
			m_AllowGameplayInput = value;
		}
	}

	protected virtual bool OnValue_Pause
	{
		get
		{
			return vp_Gameplay.IsPaused;
		}
		set
		{
			vp_Gameplay.IsPaused = value;
		}
	}

	protected virtual Vector2 OnValue_InputSmoothLook
	{
		get
		{
			Vector2 mouseLook = GetMouseLook();
			mouseLook.x *= ((!MouseLookMuteYaw) ? 1 : 0);
			mouseLook.y *= ((!MouseLookMutePitch) ? 1 : 0);
			return mouseLook;
		}
	}

	protected virtual Vector2 OnValue_InputRawLook
	{
		get
		{
			Vector2 mouseLookRaw = GetMouseLookRaw();
			mouseLookRaw.x *= ((!MouseLookMuteYaw) ? 1 : 0);
			mouseLookRaw.y *= ((!MouseLookMutePitch) ? 1 : 0);
			return mouseLookRaw;
		}
	}

	protected override void OnEnable()
	{
		if (FPPlayer != null)
		{
			FPPlayer.Register(this);
		}
	}

	protected override void OnDisable()
	{
		if (FPPlayer != null)
		{
			FPPlayer.Unregister(this);
		}
	}

	protected override void Update()
	{
		UpdateCursorLock();
		UpdatePause();
		if (!FPPlayer.Pause.Get() && m_AllowGameplayInput)
		{
			InputInteract();
			InputMove();
			InputRun();
			InputJump();
			InputCrouch();
			InputAttack();
			InputReload();
			InputSetWeapon();
			InputCamera();
		}
	}

	protected virtual void InputInteract()
	{
		if (vp_Input.GetButtonDown("Interact"))
		{
			FPPlayer.Interact.TryStart();
		}
		else
		{
			FPPlayer.Interact.TryStop();
		}
	}

	protected virtual void InputMove()
	{
		FPPlayer.InputMoveVector.Set(new Vector2(vp_Input.GetAxisRaw("Horizontal"), vp_Input.GetAxisRaw("Vertical")));
	}

	protected virtual void InputRun()
	{
		if (vp_Input.GetButton("Run") || vp_Input.GetAxisRaw("LeftTrigger") > 0.5f)
		{
			FPPlayer.Run.TryStart();
		}
		else
		{
			FPPlayer.Run.TryStop();
		}
	}

	protected virtual void InputJump()
	{
		if (vp_Input.GetButton("Jump"))
		{
			FPPlayer.Jump.TryStart();
		}
		else
		{
			FPPlayer.Jump.Stop();
		}
	}

	protected virtual void InputCrouch()
	{
		if (vp_Input.GetButton("Crouch"))
		{
			FPPlayer.Crouch.TryStart();
		}
		else
		{
			FPPlayer.Crouch.TryStop();
		}
	}

	protected virtual void InputCamera()
	{
		if (vp_Input.GetButton("Zoom"))
		{
			FPPlayer.Zoom.TryStart();
		}
		else
		{
			FPPlayer.Zoom.TryStop();
		}
		if (vp_Input.GetButtonDown("Toggle3rdPerson"))
		{
			FPPlayer.CameraToggle3rdPerson.Send();
		}
	}

	protected virtual void InputAttack()
	{
		if (vp_Utility.LockCursor)
		{
			if (vp_Input.GetButton("Attack") || vp_Input.GetAxisRaw("RightTrigger") > 0.5f)
			{
				FPPlayer.Attack.TryStart();
			}
			else
			{
				FPPlayer.Attack.TryStop();
			}
		}
	}

	protected virtual void InputReload()
	{
		if (vp_Input.GetButtonDown("Reload"))
		{
			FPPlayer.Reload.TryStart();
		}
	}

	protected virtual void InputSetWeapon()
	{
		if (vp_Input.GetButtonDown("SetPrevWeapon"))
		{
			FPPlayer.SetPrevWeapon.Try();
		}
		if (vp_Input.GetButtonDown("SetNextWeapon"))
		{
			FPPlayer.SetNextWeapon.Try();
		}
		if (vp_Input.GetButtonDown("SetWeapon1"))
		{
			FPPlayer.SetWeapon.TryStart(1);
		}
		if (vp_Input.GetButtonDown("SetWeapon2"))
		{
			FPPlayer.SetWeapon.TryStart(2);
		}
		if (vp_Input.GetButtonDown("SetWeapon3"))
		{
			FPPlayer.SetWeapon.TryStart(3);
		}
		if (vp_Input.GetButtonDown("SetWeapon4"))
		{
			FPPlayer.SetWeapon.TryStart(4);
		}
		if (vp_Input.GetButtonDown("SetWeapon5"))
		{
			FPPlayer.SetWeapon.TryStart(5);
		}
		if (vp_Input.GetButtonDown("SetWeapon6"))
		{
			FPPlayer.SetWeapon.TryStart(6);
		}
		if (vp_Input.GetButtonDown("SetWeapon7"))
		{
			FPPlayer.SetWeapon.TryStart(7);
		}
		if (vp_Input.GetButtonDown("SetWeapon8"))
		{
			FPPlayer.SetWeapon.TryStart(8);
		}
		if (vp_Input.GetButtonDown("SetWeapon9"))
		{
			FPPlayer.SetWeapon.TryStart(9);
		}
		if (vp_Input.GetButtonDown("SetWeapon10"))
		{
			FPPlayer.SetWeapon.TryStart(10);
		}
		if (vp_Input.GetButtonDown("ClearWeapon"))
		{
			FPPlayer.SetWeapon.TryStart(0);
		}
	}

	protected virtual void UpdatePause()
	{
		if (vp_Input.GetButtonDown("Pause"))
		{
			FPPlayer.Pause.Set(!FPPlayer.Pause.Get());
		}
	}

	protected virtual void UpdateCursorLock()
	{
		m_MousePos.x = Input.mousePosition.x;
		m_MousePos.y = (float)Screen.height - Input.mousePosition.y;
		if (MouseCursorForced)
		{
			if (vp_Utility.LockCursor)
			{
				vp_Utility.LockCursor = false;
			}
			return;
		}
		if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
		{
			if (MouseCursorZones.Length != 0)
			{
				Rect[] mouseCursorZones = MouseCursorZones;
				int num = 0;
				while (num < mouseCursorZones.Length)
				{
					Rect rect = mouseCursorZones[num];
					if (!rect.Contains(m_MousePos))
					{
						num++;
						continue;
					}
					goto IL_008a;
				}
			}
			if (!vp_Utility.LockCursor)
			{
				vp_Utility.LockCursor = true;
			}
		}
		goto IL_00b0;
		IL_008a:
		if (vp_Utility.LockCursor)
		{
			vp_Utility.LockCursor = false;
		}
		goto IL_00b0;
		IL_00b0:
		if (vp_Input.GetButtonUp("Accept1") || vp_Input.GetButtonUp("Accept2") || vp_Input.GetButtonUp("Menu"))
		{
			vp_Utility.LockCursor = !vp_Utility.LockCursor;
		}
	}

	protected virtual Vector2 GetMouseLook()
	{
		if (MouseCursorBlocksMouseLook && !vp_Utility.LockCursor)
		{
			return Vector2.zero;
		}
		if (m_LastMouseLookFrame == Time.frameCount)
		{
			return m_CurrentMouseLook;
		}
		if (MouseCursorBlocksMouseLook && !vp_Utility.LockCursor)
		{
			return Vector2.zero;
		}
		if (m_LastMouseLookFrame == Time.frameCount)
		{
			return m_CurrentMouseLook;
		}
		m_LastMouseLookFrame = Time.frameCount;
		m_MouseLookSmoothMove.x = vp_Input.GetAxisRaw("Mouse X") * Time.timeScale;
		m_MouseLookSmoothMove.y = vp_Input.GetAxisRaw("Mouse Y") * Time.timeScale;
		MouseLookSmoothSteps = Mathf.Clamp(MouseLookSmoothSteps, 1, 20);
		MouseLookSmoothWeight = Mathf.Clamp01(MouseLookSmoothWeight);
		while (m_MouseLookSmoothBuffer.Count > MouseLookSmoothSteps)
		{
			m_MouseLookSmoothBuffer.RemoveAt(0);
		}
		m_MouseLookSmoothBuffer.Add(m_MouseLookSmoothMove);
		float num = 1f;
		Vector2 zero = Vector2.zero;
		float num2 = 0f;
		for (int num3 = m_MouseLookSmoothBuffer.Count - 1; num3 > 0; num3--)
		{
			zero += m_MouseLookSmoothBuffer[num3] * num;
			num2 += 1f * num;
			num *= MouseLookSmoothWeight / base.Delta;
		}
		num2 = Mathf.Max(1f, num2);
		m_CurrentMouseLook = vp_MathUtility.NaNSafeVector2(zero / num2);
		float num4 = 0f;
		float num5 = Mathf.Abs(m_CurrentMouseLook.x);
		float num6 = Mathf.Abs(m_CurrentMouseLook.y);
		if (MouseLookAcceleration)
		{
			num4 = Mathf.Sqrt(num5 * num5 + num6 * num6) / base.Delta;
			num4 = ((num4 <= MouseLookAccelerationThreshold || MouseLookAccelerationThreshold == 0f) ? 0f : num4);
		}
		m_CurrentMouseLook.x *= MouseLookSensitivity.x + num4;
		m_CurrentMouseLook.y *= MouseLookSensitivity.y + num4;
		m_CurrentMouseLook.y = (MouseLookInvert ? m_CurrentMouseLook.y : (0f - m_CurrentMouseLook.y));
		return m_CurrentMouseLook;
	}

	protected virtual Vector2 GetMouseLookRaw()
	{
		if (MouseCursorBlocksMouseLook && !vp_Utility.LockCursor)
		{
			return Vector2.zero;
		}
		m_MouseLookRawMove.x = vp_Input.GetAxisRaw("Mouse X");
		m_MouseLookRawMove.y = vp_Input.GetAxisRaw("Mouse Y");
		return m_MouseLookRawMove;
	}

	protected virtual bool OnMessage_InputGetButton(string button)
	{
		return vp_Input.GetButton(button);
	}

	protected virtual bool OnMessage_InputGetButtonUp(string button)
	{
		return vp_Input.GetButtonUp(button);
	}

	protected virtual bool OnMessage_InputGetButtonDown(string button)
	{
		return vp_Input.GetButtonDown(button);
	}
}
