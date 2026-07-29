using System.Collections.Generic;
using UnityEngine;

public class vp_FPController : vp_CharacterController
{
	protected Vector3 m_FixedPosition = Vector3.zero;

	protected Vector3 m_SmoothPosition = Vector3.zero;

	protected bool m_IsFirstPerson = true;

	protected bool m_HeadContact;

	protected RaycastHit m_CeilingHit;

	protected RaycastHit m_WallHit;

	protected CapsuleCollider m_TriggerCollider;

	public bool PhysicsHasCollisionTrigger = true;

	protected GameObject m_Trigger;

	protected vp_SurfaceIdentifier m_SurfaceIdentifier;

	public float MotorAcceleration = 0.18f;

	public float MotorDamping = 0.17f;

	public float MotorBackwardsSpeed = 0.65f;

	public float MotorAirSpeed = 0.35f;

	public float MotorSlopeSpeedUp = 1f;

	public float MotorSlopeSpeedDown = 1f;

	protected Vector3 m_MoveDirection = Vector3.zero;

	protected float m_SlopeFactor = 1f;

	protected Vector3 m_MotorThrottle = Vector3.zero;

	protected float m_MotorAirSpeedModifier = 1f;

	protected float m_CurrentAntiBumpOffset;

	public float MotorJumpForce = 0.18f;

	public float MotorJumpForceDamping = 0.08f;

	public float MotorJumpForceHold = 0.003f;

	public float MotorJumpForceHoldDamping = 0.5f;

	protected int m_MotorJumpForceHoldSkipFrames;

	protected float m_MotorJumpForceAcc;

	protected bool m_MotorJumpDone = true;

	public float PhysicsForceDamping = 0.05f;

	public float PhysicsSlopeSlideLimit = 30f;

	public float PhysicsSlopeSlidiness = 0.15f;

	public float PhysicsWallBounce;

	public float PhysicsWallFriction;

	protected Vector3 m_ExternalForce = Vector3.zero;

	protected Vector3[] m_SmoothForceFrame = new Vector3[120];

	protected bool m_Slide;

	protected bool m_SlideFast;

	protected float m_SlideFallSpeed;

	protected float m_OnSteepGroundSince;

	protected float m_SlopeSlideSpeed;

	protected Vector3 m_PredictedPos = Vector3.zero;

	protected Vector3 m_PrevDir = Vector3.zero;

	protected Vector3 m_NewDir = Vector3.zero;

	protected float m_ForceImpact;

	protected float m_ForceMultiplier;

	protected Vector3 CapsuleBottom = Vector3.zero;

	protected Vector3 CapsuleTop = Vector3.zero;

	public Vector3 SmoothPosition
	{
		get
		{
			return m_SmoothPosition;
		}
	}

	public Vector3 Velocity
	{
		get
		{
			return base.CharacterController.velocity;
		}
	}

	public bool HeadContact
	{
		get
		{
			return m_HeadContact;
		}
	}

	public Vector3 GroundNormal
	{
		get
		{
			return m_GroundHit.normal;
		}
	}

	public float GroundAngle
	{
		get
		{
			return Vector3.Angle(m_GroundHit.normal, Vector3.up);
		}
	}

	protected vp_SurfaceIdentifier SurfaceIdentifier
	{
		get
		{
			if (m_SurfaceIdentifier == null)
			{
				m_SurfaceIdentifier = GetComponent<vp_SurfaceIdentifier>();
			}
			return m_SurfaceIdentifier;
		}
	}

	protected virtual Vector3 OnValue_MotorThrottle
	{
		get
		{
			return m_MotorThrottle;
		}
		set
		{
			m_MotorThrottle = value;
		}
	}

	protected virtual bool OnValue_MotorJumpDone
	{
		get
		{
			return m_MotorJumpDone;
		}
	}

	protected virtual bool OnValue_IsFirstPerson
	{
		get
		{
			return m_IsFirstPerson;
		}
		set
		{
			m_IsFirstPerson = value;
		}
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		vp_TargetEvent<Vector3>.Register(m_Transform, "ForceImpact", AddForce);
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		vp_TargetEvent<Vector3>.Unregister(m_Root, "ForceImpact", AddForce);
	}

	protected override void Start()
	{
		base.Start();
		SetPosition(base.Transform.position);
		if (!PhysicsHasCollisionTrigger)
		{
			return;
		}
		m_Trigger = new GameObject("Trigger");
		m_Trigger.transform.parent = m_Transform;
		m_Trigger.layer = 30;
		m_Trigger.transform.localPosition = Vector3.zero;
		m_TriggerCollider = m_Trigger.AddComponent<CapsuleCollider>();
		m_TriggerCollider.isTrigger = true;
		m_TriggerCollider.radius = base.CharacterController.radius + base.SkinWidth;
		m_TriggerCollider.height = base.CharacterController.height + base.SkinWidth * 2f;
		m_TriggerCollider.center = base.CharacterController.center;
		m_Trigger.gameObject.AddComponent<vp_DamageTransfer>();
		if (SurfaceIdentifier != null)
		{
			vp_Timer.In(0.05f, delegate
			{
				vp_SurfaceIdentifier obj = m_Trigger.gameObject.AddComponent<vp_SurfaceIdentifier>();
				obj.SurfaceType = SurfaceIdentifier.SurfaceType;
				obj.AllowDecals = SurfaceIdentifier.AllowDecals;
			});
		}
	}

	protected override void RefreshCollider()
	{
		base.RefreshCollider();
		if (m_TriggerCollider != null)
		{
			m_TriggerCollider.radius = base.CharacterController.radius + base.SkinWidth;
			m_TriggerCollider.height = base.CharacterController.height + base.SkinWidth * 2f;
			m_TriggerCollider.center = base.CharacterController.center;
		}
	}

	public override void EnableCollider(bool isEnabled = true)
	{
		if (base.CharacterController != null)
		{
			base.CharacterController.enabled = isEnabled;
		}
	}

	protected override void Update()
	{
		base.Update();
		SmoothMove();
	}

	protected override void FixedUpdate()
	{
		if (Time.timeScale != 0f)
		{
			UpdateMotor();
			UpdateJump();
			UpdateForces();
			UpdateSliding();
			UpdateOutOfControl();
			FixedMove();
			UpdateCollisions();
			UpdatePlatformMove();
			UpdateVelocity();
		}
	}

	protected virtual void UpdateMotor()
	{
		if (!MotorFreeFly)
		{
			UpdateThrottleWalk();
		}
		else
		{
			UpdateThrottleFree();
		}
		m_MotorThrottle = vp_MathUtility.SnapToZero(m_MotorThrottle);
	}

	protected virtual void UpdateThrottleWalk()
	{
		UpdateSlopeFactor();
		m_MotorAirSpeedModifier = (m_Grounded ? 1f : MotorAirSpeed);
		m_MotorThrottle += ((base.Player.InputMoveVector.Get().y > 0f) ? base.Player.InputMoveVector.Get().y : (base.Player.InputMoveVector.Get().y * MotorBackwardsSpeed)) * (base.Transform.TransformDirection(Vector3.forward * (MotorAcceleration * 0.1f) * m_MotorAirSpeedModifier) * m_SlopeFactor);
		m_MotorThrottle += base.Player.InputMoveVector.Get().x * (base.Transform.TransformDirection(Vector3.right * (MotorAcceleration * 0.1f) * m_MotorAirSpeedModifier) * m_SlopeFactor);
		m_MotorThrottle.x /= 1f + MotorDamping * m_MotorAirSpeedModifier * Time.timeScale;
		m_MotorThrottle.z /= 1f + MotorDamping * m_MotorAirSpeedModifier * Time.timeScale;
	}

	protected virtual void UpdateThrottleFree()
	{
		m_MotorThrottle += base.Player.InputMoveVector.Get().y * base.Transform.TransformDirection(base.Transform.InverseTransformDirection(((vp_FPPlayerEventHandler)base.Player).CameraLookDirection.Get()) * (MotorAcceleration * 0.1f));
		m_MotorThrottle += base.Player.InputMoveVector.Get().x * base.Transform.TransformDirection(Vector3.right * (MotorAcceleration * 0.1f));
		m_MotorThrottle.x /= 1f + MotorDamping * Time.timeScale;
		m_MotorThrottle.z /= 1f + MotorDamping * Time.timeScale;
	}

	protected virtual void UpdateJump()
	{
		if (m_HeadContact)
		{
			base.Player.Jump.Stop(1f);
		}
		if (!MotorFreeFly)
		{
			UpdateJumpForceWalk();
		}
		else
		{
			UpdateJumpForceFree();
		}
		m_MotorThrottle.y += m_MotorJumpForceAcc * Time.timeScale;
		m_MotorJumpForceAcc /= 1f + MotorJumpForceHoldDamping * Time.timeScale;
		m_MotorThrottle.y /= 1f + MotorJumpForceDamping * Time.timeScale;
	}

	protected virtual void UpdateJumpForceWalk()
	{
		if (!base.Player.Jump.Active || m_Grounded)
		{
			return;
		}
		if (m_MotorJumpForceHoldSkipFrames > 2)
		{
			if (!(base.Player.Velocity.Get().y < 0f))
			{
				m_MotorJumpForceAcc += MotorJumpForceHold;
			}
		}
		else
		{
			m_MotorJumpForceHoldSkipFrames++;
		}
	}

	protected virtual void UpdateJumpForceFree()
	{
		if (base.Player.Jump.Active && base.Player.Crouch.Active)
		{
			return;
		}
		if (base.Player.Jump.Active)
		{
			m_MotorJumpForceAcc += MotorJumpForceHold;
		}
		else if (base.Player.Crouch.Active)
		{
			m_MotorJumpForceAcc -= MotorJumpForceHold;
			if (base.Grounded && base.CharacterController.height == m_NormalHeight)
			{
				base.CharacterController.height = m_CrouchHeight;
				base.CharacterController.center = m_CrouchCenter;
			}
		}
	}

	protected override void UpdateForces()
	{
		base.UpdateForces();
		if (m_SmoothForceFrame[0] != Vector3.zero)
		{
			AddForceInternal(m_SmoothForceFrame[0]);
			for (int i = 0; i < 120; i++)
			{
				m_SmoothForceFrame[i] = ((i < 119) ? m_SmoothForceFrame[i + 1] : Vector3.zero);
				if (m_SmoothForceFrame[i] == Vector3.zero)
				{
					break;
				}
			}
		}
		m_ExternalForce /= 1f + PhysicsForceDamping * vp_TimeUtility.AdjustedTimeScale;
	}

	protected virtual void UpdateSliding()
	{
		bool slideFast = m_SlideFast;
		bool slide = m_Slide;
		m_Slide = false;
		if (!m_Grounded)
		{
			m_OnSteepGroundSince = 0f;
			m_SlideFast = false;
		}
		else if (GroundAngle > PhysicsSlopeSlideLimit)
		{
			m_Slide = true;
			if (GroundAngle <= base.Player.SlopeLimit.Get())
			{
				m_SlopeSlideSpeed = Mathf.Max(m_SlopeSlideSpeed, PhysicsSlopeSlidiness * 0.01f);
				m_OnSteepGroundSince = 0f;
				m_SlideFast = false;
				m_SlopeSlideSpeed = ((Mathf.Abs(m_SlopeSlideSpeed) < 0.0001f) ? 0f : (m_SlopeSlideSpeed / (1f + 0.05f * vp_TimeUtility.AdjustedTimeScale)));
			}
			else
			{
				if (m_SlopeSlideSpeed > 0.01f)
				{
					m_SlideFast = true;
				}
				if (m_OnSteepGroundSince == 0f)
				{
					m_OnSteepGroundSince = Time.time;
				}
				m_SlopeSlideSpeed += PhysicsSlopeSlidiness * 0.01f * ((Time.time - m_OnSteepGroundSince) * 0.125f) * vp_TimeUtility.AdjustedTimeScale;
				m_SlopeSlideSpeed = Mathf.Max(PhysicsSlopeSlidiness * 0.01f, m_SlopeSlideSpeed);
			}
			AddForce(Vector3.Cross(Vector3.Cross(GroundNormal, Vector3.down), GroundNormal) * m_SlopeSlideSpeed * vp_TimeUtility.AdjustedTimeScale);
		}
		else
		{
			m_OnSteepGroundSince = 0f;
			m_SlideFast = false;
			m_SlopeSlideSpeed = 0f;
		}
		if (m_MotorThrottle != Vector3.zero)
		{
			m_Slide = false;
		}
		if (m_SlideFast)
		{
			m_SlideFallSpeed = base.Transform.position.y;
		}
		else if (slideFast && !base.Grounded)
		{
			m_FallSpeed = base.Transform.position.y - m_SlideFallSpeed;
		}
		if (slide != m_Slide)
		{
			base.Player.SetState("Slide", m_Slide);
		}
	}

	private void UpdateOutOfControl()
	{
		if (m_ExternalForce.magnitude > 0.2f || m_FallSpeed < -0.2f || m_SlideFast)
		{
			base.Player.OutOfControl.Start();
		}
		else if (base.Player.OutOfControl.Active)
		{
			base.Player.OutOfControl.Stop();
		}
	}

	protected override void FixedMove()
	{
		m_MoveDirection = Vector3.zero;
		m_MoveDirection += m_ExternalForce;
		m_MoveDirection += m_MotorThrottle;
		m_MoveDirection.y += m_FallSpeed;
		m_CurrentAntiBumpOffset = 0f;
		if (m_Grounded && m_MotorThrottle.y <= 0.001f)
		{
			m_CurrentAntiBumpOffset = Mathf.Max(base.Player.StepOffset.Get(), Vector3.Scale(m_MoveDirection, Vector3.one - Vector3.up).magnitude);
			m_MoveDirection += m_CurrentAntiBumpOffset * Vector3.down;
		}
		m_PredictedPos = base.Transform.position + vp_MathUtility.NaNSafeVector3(m_MoveDirection * base.Delta * Time.timeScale);
		if (m_Platform != null && PositionOnPlatform != Vector3.zero)
		{
			base.Player.Move.Send(vp_MathUtility.NaNSafeVector3(m_Platform.TransformPoint(PositionOnPlatform) - m_Transform.position));
		}
		base.Player.Move.Send(vp_MathUtility.NaNSafeVector3(m_MoveDirection * base.Delta * Time.timeScale));
		if (base.Player.Dead.Active)
		{
			base.Player.InputMoveVector.Set(Vector2.zero);
			return;
		}
		StoreGroundInfo();
		if (!m_Grounded && base.Player.Velocity.Get().y > 0f)
		{
			Physics.SphereCast(new Ray(base.Transform.position, Vector3.up), base.Player.Radius.Get(), out m_CeilingHit, base.Player.Height.Get() - (base.Player.Radius.Get() - base.SkinWidth) + 0.01f, vp_Layer.Mask.ExternalBlockers);
			m_HeadContact = m_CeilingHit.collider != null;
		}
		else
		{
			m_HeadContact = false;
		}
		if (m_GroundHitTransform == null && m_LastGroundHitTransform != null)
		{
			if (m_Platform != null && PositionOnPlatform != Vector3.zero)
			{
				AddForce(m_Platform.position - m_LastPlatformPos);
				m_Platform = null;
			}
			if (m_CurrentAntiBumpOffset != 0f)
			{
				base.Player.Move.Send(vp_MathUtility.NaNSafeVector3(m_CurrentAntiBumpOffset * Vector3.up) * base.Delta * Time.timeScale);
				m_PredictedPos += vp_MathUtility.NaNSafeVector3(m_CurrentAntiBumpOffset * Vector3.up) * base.Delta * Time.timeScale;
				m_MoveDirection += m_CurrentAntiBumpOffset * Vector3.up;
			}
		}
	}

	protected virtual void SmoothMove()
	{
		if (Time.timeScale != 0f)
		{
			m_FixedPosition = base.Transform.position;
			base.Transform.position = m_SmoothPosition;
			base.Player.Move.Send(vp_MathUtility.NaNSafeVector3(m_MoveDirection * base.Delta * Time.timeScale));
			m_SmoothPosition = base.Transform.position;
			base.Transform.position = m_FixedPosition;
			if (Vector3.Distance(base.Transform.position, m_SmoothPosition) > base.Player.Radius.Get() || (m_Platform != null && m_LastPlatformPos != m_Platform.position))
			{
				m_SmoothPosition = base.Transform.position;
			}
			m_SmoothPosition = Vector3.Lerp(m_SmoothPosition, base.Transform.position, Time.deltaTime);
		}
	}

	protected override void UpdateCollisions()
	{
		base.UpdateCollisions();
		if (m_OnNewGround)
		{
			if (m_WasFalling)
			{
				DeflectDownForce();
				m_SmoothPosition.y = base.Transform.position.y;
				m_MotorThrottle.y = 0f;
				m_MotorJumpForceAcc = 0f;
				m_MotorJumpForceHoldSkipFrames = 0;
			}
			if (m_GroundHit.collider.gameObject.layer == 28)
			{
				m_Platform = m_GroundHitTransform;
				m_LastPlatformAngle = m_Platform.eulerAngles.y;
			}
			else
			{
				m_Platform = null;
			}
		}
		if (m_PredictedPos.y > base.Transform.position.y && (m_ExternalForce.y > 0f || m_MotorThrottle.y > 0f))
		{
			DeflectUpForce();
		}
		if (m_PredictedPos.x != base.Transform.position.x || (m_PredictedPos.z != base.Transform.position.z && m_ExternalForce != Vector3.zero))
		{
			DeflectHorizontalForce();
		}
	}

	protected virtual void UpdateSlopeFactor()
	{
		if (!m_Grounded)
		{
			m_SlopeFactor = 1f;
			return;
		}
		m_SlopeFactor = 1f + (1f - Vector3.Angle(m_GroundHit.normal, m_MotorThrottle) / 90f);
		if (Mathf.Abs(1f - m_SlopeFactor) < 0.01f)
		{
			m_SlopeFactor = 1f;
		}
		else if (m_SlopeFactor > 1f)
		{
			if (MotorSlopeSpeedDown == 1f)
			{
				m_SlopeFactor = 1f / m_SlopeFactor;
				m_SlopeFactor *= 1.2f;
			}
			else
			{
				m_SlopeFactor *= MotorSlopeSpeedDown;
			}
		}
		else
		{
			if (MotorSlopeSpeedUp == 1f)
			{
				m_SlopeFactor *= 1.2f;
			}
			else
			{
				m_SlopeFactor *= MotorSlopeSpeedUp;
			}
			m_SlopeFactor = ((GroundAngle > base.Player.SlopeLimit.Get()) ? 0f : m_SlopeFactor);
		}
	}

	protected override void UpdatePlatformMove()
	{
		base.UpdatePlatformMove();
		if (m_Platform != null)
		{
			m_SmoothPosition = base.Transform.position;
		}
	}

	protected override void UpdatePlatformRotation()
	{
		if (!(m_Platform == null))
		{
			base.UpdatePlatformRotation();
		}
	}

	public override void SetPosition(Vector3 position)
	{
		base.SetPosition(position);
		m_SmoothPosition = position;
	}

	protected virtual void AddForceInternal(Vector3 force)
	{
		m_ExternalForce += force;
	}

	public virtual void AddForce(float x, float y, float z)
	{
		AddForce(new Vector3(x, y, z));
	}

	public virtual void AddForce(Vector3 force)
	{
		if (Time.timeScale >= 1f)
		{
			AddForceInternal(force);
		}
		else
		{
			AddSoftForce(force, 1f);
		}
	}

	public virtual void AddSoftForce(Vector3 force, float frames)
	{
		force /= Time.timeScale;
		frames = Mathf.Clamp(frames, 1f, 120f);
		AddForceInternal(force / frames);
		for (int i = 0; i < Mathf.RoundToInt(frames) - 1; i++)
		{
			m_SmoothForceFrame[i] += force / frames;
		}
	}

	public virtual void StopSoftForce()
	{
		for (int i = 0; i < 120 && !(m_SmoothForceFrame[i] == Vector3.zero); i++)
		{
			m_SmoothForceFrame[i] = Vector3.zero;
		}
	}

	public override void Stop()
	{
		base.Stop();
		m_MotorThrottle = Vector3.zero;
		m_MotorJumpDone = true;
		m_MotorJumpForceAcc = 0f;
		m_ExternalForce = Vector3.zero;
		StopSoftForce();
		m_SmoothPosition = base.Transform.position;
	}

	public virtual void DeflectDownForce()
	{
		if (GroundAngle > PhysicsSlopeSlideLimit)
		{
			m_SlopeSlideSpeed = m_FallImpact * (0.25f * Time.timeScale);
		}
		if (GroundAngle > 85f)
		{
			m_MotorThrottle += vp_3DUtility.HorizontalVector(GroundNormal * m_FallImpact);
			m_Grounded = false;
		}
	}

	protected virtual void DeflectUpForce()
	{
		if (m_HeadContact)
		{
			m_NewDir = Vector3.Cross(Vector3.Cross(m_CeilingHit.normal, Vector3.up), m_CeilingHit.normal);
			m_ForceImpact = m_MotorThrottle.y + m_ExternalForce.y;
			Vector3 vector = m_NewDir * (m_MotorThrottle.y + m_ExternalForce.y) * (1f - PhysicsWallFriction);
			m_ForceImpact -= vector.magnitude;
			AddForce(vector * Time.timeScale);
			m_MotorThrottle.y = 0f;
			m_ExternalForce.y = 0f;
			m_FallSpeed = 0f;
			m_NewDir.x = base.Transform.InverseTransformDirection(m_NewDir).x;
			base.Player.HeadImpact.Send((m_NewDir.x < 0f || (m_NewDir.x == 0f && Random.value < 0.5f)) ? (0f - m_ForceImpact) : m_ForceImpact);
		}
	}

	protected virtual void DeflectHorizontalForce()
	{
		m_PredictedPos.y = base.Transform.position.y;
		m_PrevPosition.y = base.Transform.position.y;
		m_PrevDir = (m_PredictedPos - m_PrevPosition).normalized;
		CapsuleBottom = m_PrevPosition + Vector3.up * base.Player.Radius.Get();
		CapsuleTop = CapsuleBottom + Vector3.up * (base.Player.Height.Get() - base.Player.Radius.Get() * 2f);
		if (Physics.CapsuleCast(CapsuleBottom, CapsuleTop, base.Player.Radius.Get(), m_PrevDir, out m_WallHit, Vector3.Distance(m_PrevPosition, m_PredictedPos), vp_Layer.Mask.ExternalBlockers))
		{
			m_NewDir = Vector3.Cross(m_WallHit.normal, Vector3.up).normalized;
			if (Vector3.Dot(Vector3.Cross(m_WallHit.point - base.Transform.position, m_PrevPosition - base.Transform.position), Vector3.up) > 0f)
			{
				m_NewDir = -m_NewDir;
			}
			m_ForceMultiplier = Mathf.Abs(Vector3.Dot(m_PrevDir, m_NewDir)) * (1f - PhysicsWallFriction);
			if (PhysicsWallBounce > 0f)
			{
				m_NewDir = Vector3.Lerp(m_NewDir, Vector3.Reflect(m_PrevDir, m_WallHit.normal), PhysicsWallBounce);
				m_ForceMultiplier = Mathf.Lerp(m_ForceMultiplier, 1f, PhysicsWallBounce * (1f - PhysicsWallFriction));
			}
			m_ForceImpact = 0f;
			float y = m_ExternalForce.y;
			m_ExternalForce.y = 0f;
			m_ForceImpact = m_ExternalForce.magnitude;
			m_ExternalForce = m_NewDir * m_ExternalForce.magnitude * m_ForceMultiplier;
			m_ForceImpact -= m_ExternalForce.magnitude;
			for (int i = 0; i < 120 && !(m_SmoothForceFrame[i] == Vector3.zero); i++)
			{
				m_SmoothForceFrame[i] = m_SmoothForceFrame[i].magnitude * m_NewDir * m_ForceMultiplier;
			}
			m_ExternalForce.y = y;
		}
	}

	public float CalculateMaxSpeed(string stateName = "Default", float accelDuration = 5f)
	{
		if (stateName != "Default")
		{
			bool flag = false;
			foreach (vp_State state in States)
			{
				if (state.Name == stateName)
				{
					flag = true;
				}
			}
			if (!flag)
			{
				Debug.LogError(string.Concat("Error (", this, ") Controller has no such state: '", stateName, "'."));
				return 0f;
			}
		}
		Dictionary<vp_State, bool> dictionary = new Dictionary<vp_State, bool>();
		foreach (vp_State state2 in States)
		{
			dictionary.Add(state2, state2.Enabled);
			state2.Enabled = false;
		}
		base.StateManager.Reset();
		if (stateName != "Default")
		{
			SetState(stateName);
		}
		float num = 0f;
		float num2 = 5f;
		for (int i = 0; (float)i < 60f * num2; i++)
		{
			num += MotorAcceleration * 0.1f * 60f;
			num /= 1f + MotorDamping;
		}
		foreach (vp_State state3 in States)
		{
			bool value;
			dictionary.TryGetValue(state3, out value);
			state3.Enabled = value;
		}
		return num;
	}

	protected virtual void OnControllerColliderHit(ControllerColliderHit hit)
	{
		if (hit.gameObject.isStatic || hit.gameObject.layer == 29)
		{
			return;
		}
		Rigidbody attachedRigidbody = hit.collider.attachedRigidbody;
		if (!(attachedRigidbody == null) && (!vp_Gameplay.IsMaster || !attachedRigidbody.isKinematic) && !(Time.time < m_NextAllowedPushTime))
		{
			m_NextAllowedPushTime = Time.time + PhysicsPushInterval;
			if (vp_Gameplay.IsMultiplayer)
			{
				vp_TargetEvent<Vector3, Vector3>.Send(attachedRigidbody, "Push", hit.moveDirection, hit.point);
			}
			else
			{
				PushRigidbody(attachedRigidbody, hit.moveDirection, hit.point);
			}
		}
	}

	protected virtual bool CanStart_Jump()
	{
		if (MotorFreeFly)
		{
			return true;
		}
		if (!m_Grounded)
		{
			return false;
		}
		if (!m_MotorJumpDone)
		{
			return false;
		}
		if (GroundAngle > base.Player.SlopeLimit.Get())
		{
			return false;
		}
		return true;
	}

	protected virtual bool CanStart_Run()
	{
		if (base.Player.Crouch.Active)
		{
			return false;
		}
		return true;
	}

	protected virtual void OnStart_Jump()
	{
		m_MotorJumpDone = false;
		if (!MotorFreeFly || base.Grounded)
		{
			m_MotorThrottle.y = MotorJumpForce / Time.timeScale;
			m_SmoothPosition.y = base.Transform.position.y;
		}
	}

	protected virtual void OnStop_Jump()
	{
		m_MotorJumpDone = true;
	}

	protected virtual bool CanStop_Crouch()
	{
		if (Physics.SphereCast(new Ray(base.Transform.position, Vector3.up), base.Player.Radius.Get(), m_NormalHeight - base.Player.Radius.Get() + 0.01f, vp_Layer.Mask.ExternalBlockers))
		{
			base.Player.Crouch.NextAllowedStopTime = Time.time + 1f;
			return false;
		}
		return true;
	}

	protected virtual void OnMessage_ForceImpact(Vector3 force)
	{
		AddForce(force);
	}

	protected virtual void OnStart_Dead()
	{
		m_Platform = null;
	}

	protected virtual void OnStop_Dead()
	{
		base.Player.OutOfControl.Stop();
	}
}
