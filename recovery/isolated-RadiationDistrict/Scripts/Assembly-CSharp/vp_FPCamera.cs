using UnityEngine;

[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(AudioListener))]
public class vp_FPCamera : vp_Component
{
	public delegate void BobStepDelegate();

	public vp_FPController FPController;

	public float RenderingFieldOfView = 60f;

	public float RenderingZoomDamping = 0.2f;

	protected float m_FinalZoomTime;

	protected float m_ZoomOffset;

	public Vector3 PositionOffset = new Vector3(0f, 1.75f, 0.1f);

	public float PositionGroundLimit = 0.1f;

	public float PositionSpringStiffness = 0.01f;

	public float PositionSpringDamping = 0.25f;

	public float PositionSpring2Stiffness = 0.95f;

	public float PositionSpring2Damping = 0.25f;

	public float PositionKneeling = 0.025f;

	public int PositionKneelingSoftness = 1;

	public float PositionEarthQuakeFactor = 1f;

	protected vp_Spring m_PositionSpring;

	protected vp_Spring m_PositionSpring2;

	protected bool m_DrawCameraCollisionDebugLine;

	protected Vector3 PositionOnDeath = Vector3.zero;

	public Vector2 RotationPitchLimit = new Vector2(90f, -90f);

	public Vector2 RotationYawLimit = new Vector2(-360f, 360f);

	public float RotationSpringStiffness = 0.01f;

	public float RotationSpringDamping = 0.25f;

	public float RotationKneeling = 0.025f;

	public int RotationKneelingSoftness = 1;

	public float RotationStrafeRoll = 0.01f;

	public float RotationEarthQuakeFactor;

	public Vector3 LookPoint = Vector3.zero;

	protected float m_Pitch;

	protected float m_Yaw;

	protected vp_Spring m_RotationSpring;

	protected RaycastHit m_LookPointHit;

	public Vector3 Position3rdPersonOffset = new Vector3(0.5f, 0.1f, 0.75f);

	protected float m_Current3rdPersonBlend;

	protected Vector3 m_Final3rdPersonCameraOffset = Vector3.zero;

	public float ShakeSpeed;

	public Vector3 ShakeAmplitude = new Vector3(10f, 10f, 0f);

	protected Vector3 m_Shake = Vector3.zero;

	public Vector4 BobRate = new Vector4(0f, 1.4f, 0f, 0.7f);

	public Vector4 BobAmplitude = new Vector4(0f, 0.25f, 0f, 0.5f);

	public float BobInputVelocityScale = 1f;

	public float BobMaxInputVelocity = 100f;

	public bool BobRequireGroundContact = true;

	protected float m_LastBobSpeed;

	protected Vector4 m_CurrentBobAmp = Vector4.zero;

	protected Vector4 m_CurrentBobVal = Vector4.zero;

	protected float m_BobSpeed;

	public BobStepDelegate BobStepCallback;

	public float BobStepThreshold = 10f;

	protected float m_LastYBob;

	protected bool m_BobWasElevating;

	public bool HasCollision = true;

	protected Vector3 m_CollisionVector = Vector3.zero;

	protected Vector3 m_CameraCollisionStartPos = Vector3.zero;

	protected Vector3 m_CameraCollisionEndPos = Vector3.zero;

	protected RaycastHit m_CameraHit;

	public bool MuteRoll;

	public bool MuteBob;

	public bool MuteShakes;

	public bool MuteEarthquakes;

	public bool MuteBombShakes;

	public bool MuteFallImpacts;

	public bool MuteHeadImpacts;

	public bool MuteGroundStomps;

	private vp_FPPlayerEventHandler m_Player;

	private Rigidbody m_FirstRigidbody;

	public bool DisableVRModeOnStartup;

	public float ZoomOffset
	{
		get
		{
			return m_ZoomOffset;
		}
		set
		{
			m_ZoomOffset = value;
		}
	}

	public Vector3 SpringState
	{
		get
		{
			return m_PositionSpring.State + m_PositionSpring2.State;
		}
	}

	public bool DrawCameraCollisionDebugLine
	{
		get
		{
			return m_DrawCameraCollisionDebugLine;
		}
		set
		{
			m_DrawCameraCollisionDebugLine = value;
		}
	}

	public Vector3 CollisionVector
	{
		get
		{
			return m_CollisionVector;
		}
	}

	private vp_FPPlayerEventHandler Player
	{
		get
		{
			if (m_Player == null && base.EventHandler != null)
			{
				m_Player = (vp_FPPlayerEventHandler)base.EventHandler;
			}
			return m_Player;
		}
	}

	private Rigidbody FirstRigidBody
	{
		get
		{
			if (m_FirstRigidbody == null)
			{
				m_FirstRigidbody = base.Transform.root.GetComponentInChildren<Rigidbody>();
			}
			return m_FirstRigidbody;
		}
	}

	public Vector2 Angle
	{
		get
		{
			return new Vector2(m_Pitch, m_Yaw);
		}
		set
		{
			Pitch = value.x;
			Yaw = value.y;
		}
	}

	public Vector3 Forward
	{
		get
		{
			return m_Transform.forward;
		}
	}

	public float Pitch
	{
		get
		{
			return m_Pitch;
		}
		set
		{
			if (value > 90f)
			{
				value -= 360f;
			}
			m_Pitch = value;
		}
	}

	public float Yaw
	{
		get
		{
			return m_Yaw;
		}
		set
		{
			m_Yaw = value;
		}
	}

	public virtual Vector3 OnValue_LookPoint
	{
		get
		{
			return LookPoint;
		}
	}

	protected virtual Vector3 OnValue_CameraLookDirection
	{
		get
		{
			return (Player.LookPoint.Get() - base.Transform.position).normalized;
		}
	}

	protected virtual Vector2 OnValue_Rotation
	{
		get
		{
			return Angle;
		}
		set
		{
			Angle = value;
		}
	}

	protected virtual bool OnValue_IsLocal
	{
		get
		{
			return true;
		}
	}

	protected override void Awake()
	{
		base.Awake();
		FPController = base.Root.GetComponent<vp_FPController>();
		SetRotation(new Vector2(base.Transform.eulerAngles.x, base.Transform.eulerAngles.y));
		base.Parent.gameObject.layer = 30;
		base.Camera.cullingMask &= 1073741823;
		base.Camera.depth = 0f;
		Camera camera = null;
		foreach (Transform item in base.Transform)
		{
			camera = (Camera)item.GetComponent(typeof(Camera));
			if (camera != null)
			{
				camera.transform.localPosition = Vector3.zero;
				camera.transform.localEulerAngles = Vector3.zero;
				camera.clearFlags = CameraClearFlags.Depth;
				camera.cullingMask = int.MinValue;
				camera.depth = 1f;
				camera.farClipPlane = 100f;
				camera.nearClipPlane = 0.01f;
				camera.fieldOfView = 60f;
				break;
			}
		}
		m_PositionSpring = new vp_Spring(base.Transform, vp_Spring.UpdateMode.Position, false);
		m_PositionSpring.MinVelocity = 0f;
		m_PositionSpring.RestState = PositionOffset;
		m_PositionSpring2 = new vp_Spring(base.Transform, vp_Spring.UpdateMode.PositionAdditiveLocal, false);
		m_PositionSpring2.MinVelocity = 0f;
		m_RotationSpring = new vp_Spring(base.Transform, vp_Spring.UpdateMode.RotationAdditiveLocal, false);
		m_RotationSpring.MinVelocity = 0f;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		vp_TargetEvent<float>.Register(m_Root, "CameraBombShake", OnMessage_CameraBombShake);
		vp_TargetEvent<float>.Register(m_Root, "CameraGroundStomp", OnMessage_CameraGroundStomp);
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		vp_TargetEvent<float>.Unregister(m_Root, "CameraBombShake", OnMessage_CameraBombShake);
		vp_TargetEvent<float>.Unregister(m_Root, "CameraGroundStomp", OnMessage_CameraGroundStomp);
	}

	protected override void Start()
	{
		base.Start();
		Refresh();
		SnapSprings();
		SnapZoom();
	}

	protected override void Init()
	{
		base.Init();
	}

	protected override void Update()
	{
		base.Update();
		if (Time.timeScale != 0f)
		{
			UpdateInput();
		}
	}

	protected override void FixedUpdate()
	{
		base.FixedUpdate();
		if (Time.timeScale != 0f)
		{
			UpdateZoom();
			UpdateSwaying();
			UpdateBob();
			UpdateEarthQuake();
			UpdateShakes();
			UpdateSprings();
		}
	}

	protected override void LateUpdate()
	{
		base.LateUpdate();
		if (Time.timeScale != 0f)
		{
			m_Transform.position = FPController.SmoothPosition;
			if (Player.IsFirstPerson.Get())
			{
				m_Transform.localPosition += m_PositionSpring.State + m_PositionSpring2.State;
			}
			else
			{
				m_Transform.localPosition += m_PositionSpring.State + Vector3.Scale(m_PositionSpring2.State, Vector3.up);
			}
			TryCameraCollision();
			Quaternion quaternion = Quaternion.AngleAxis(m_Yaw, Vector3.up);
			Quaternion quaternion2 = Quaternion.AngleAxis(0f, Vector3.left);
			base.Parent.rotation = vp_MathUtility.NaNSafeQuaternion(quaternion * quaternion2, base.Parent.rotation);
			quaternion2 = Quaternion.AngleAxis(0f - m_Pitch, Vector3.left);
			base.Transform.rotation = vp_MathUtility.NaNSafeQuaternion(quaternion * quaternion2, base.Transform.rotation);
			base.Transform.localEulerAngles += vp_MathUtility.NaNSafeVector3(Vector3.forward * m_RotationSpring.State.z);
			Update3rdPerson();
		}
	}

	private void Update3rdPerson()
	{
		if (Position3rdPersonOffset == Vector3.zero)
		{
			return;
		}
		if (PositionOnDeath != Vector3.zero)
		{
			base.Transform.position = PositionOnDeath;
			if (FirstRigidBody != null)
			{
				base.Transform.LookAt(FirstRigidBody.transform.position + Vector3.up);
			}
			else
			{
				base.Transform.LookAt(base.Root.position + Vector3.up);
			}
			return;
		}
		if (Player.IsFirstPerson.Get())
		{
			m_Final3rdPersonCameraOffset = Vector3.zero;
			m_Current3rdPersonBlend = 0f;
			LookPoint = GetLookPoint();
			return;
		}
		m_Current3rdPersonBlend = Mathf.Lerp(m_Current3rdPersonBlend, 1f, Time.deltaTime);
		m_Final3rdPersonCameraOffset = base.Transform.position;
		if (base.Transform.localPosition.z > -0.2f)
		{
			base.Transform.localPosition = new Vector3(base.Transform.localPosition.x, base.Transform.localPosition.y, -0.2f);
		}
		Vector3 position = base.Transform.position;
		position += m_Transform.right * Position3rdPersonOffset.x;
		position += m_Transform.up * Position3rdPersonOffset.y;
		position += m_Transform.forward * Position3rdPersonOffset.z;
		base.Transform.position = Vector3.Lerp(base.Transform.position, position, m_Current3rdPersonBlend);
		m_Final3rdPersonCameraOffset -= base.Transform.position;
		TryCameraCollision();
		LookPoint = GetLookPoint();
	}

	public virtual void TryCameraCollision()
	{
		if (HasCollision)
		{
			m_CameraCollisionStartPos = FPController.Transform.TransformPoint(0f, PositionOffset.y, 0f) - (m_Player.IsFirstPerson.Get() ? Vector3.zero : (FPController.Transform.position - FPController.SmoothPosition));
			m_CameraCollisionEndPos = base.Transform.position + (base.Transform.position - m_CameraCollisionStartPos).normalized * FPController.CharacterController.radius;
			m_CollisionVector = Vector3.zero;
			if (Physics.Linecast(m_CameraCollisionStartPos, m_CameraCollisionEndPos, out m_CameraHit, vp_Layer.Mask.ExternalBlockers) && !m_CameraHit.collider.isTrigger)
			{
				base.Transform.position = m_CameraHit.point - (m_CameraHit.point - m_CameraCollisionStartPos).normalized * FPController.CharacterController.radius;
				m_CollisionVector = m_CameraHit.point - m_CameraCollisionEndPos;
			}
			if (base.Transform.localPosition.y < PositionGroundLimit)
			{
				base.Transform.localPosition = new Vector3(base.Transform.localPosition.x, PositionGroundLimit, base.Transform.localPosition.z);
			}
		}
	}

	public virtual void AddForce(Vector3 force)
	{
		m_PositionSpring.AddForce(force);
	}

	public virtual void AddForce(float x, float y, float z)
	{
		AddForce(new Vector3(x, y, z));
	}

	public virtual void AddForce2(Vector3 force)
	{
		m_PositionSpring2.AddForce(force);
	}

	public void AddForce2(float x, float y, float z)
	{
		AddForce2(new Vector3(x, y, z));
	}

	public virtual void AddRollForce(float force)
	{
		m_RotationSpring.AddForce(Vector3.forward * force);
	}

	protected virtual void UpdateInput()
	{
		if (!Player.Dead.Active && !(Player.InputSmoothLook.Get() == Vector2.zero))
		{
			m_Yaw += Player.InputSmoothLook.Get().x;
			m_Pitch += Player.InputSmoothLook.Get().y;
			m_Yaw = ((m_Yaw < -360f) ? (m_Yaw += 360f) : m_Yaw);
			m_Yaw = ((m_Yaw > 360f) ? (m_Yaw -= 360f) : m_Yaw);
			m_Yaw = Mathf.Clamp(m_Yaw, RotationYawLimit.x, RotationYawLimit.y);
			m_Pitch = ((m_Pitch < -360f) ? (m_Pitch += 360f) : m_Pitch);
			m_Pitch = ((m_Pitch > 360f) ? (m_Pitch -= 360f) : m_Pitch);
			m_Pitch = Mathf.Clamp(m_Pitch, 0f - RotationPitchLimit.x, 0f - RotationPitchLimit.y);
		}
	}

	protected virtual void UpdateZoom()
	{
		if (!(m_FinalZoomTime <= Time.time))
		{
			RenderingZoomDamping = Mathf.Max(RenderingZoomDamping, 0.01f);
			float t = 1f - (m_FinalZoomTime - Time.time) / RenderingZoomDamping;
			base.Camera.fieldOfView = Mathf.SmoothStep(base.Camera.fieldOfView, RenderingFieldOfView + ZoomOffset, t);
		}
	}

	public void RefreshZoom()
	{
		float t = 1f - (m_FinalZoomTime - Time.time) / RenderingZoomDamping;
		base.Camera.fieldOfView = Mathf.SmoothStep(base.Camera.fieldOfView, RenderingFieldOfView + ZoomOffset, t);
	}

	public virtual void Zoom()
	{
		m_FinalZoomTime = Time.time + RenderingZoomDamping;
	}

	public virtual void SnapZoom()
	{
		base.Camera.fieldOfView = RenderingFieldOfView + ZoomOffset;
	}

	protected virtual void UpdateShakes()
	{
		if (!MuteShakes && ShakeSpeed != 0f)
		{
			m_Yaw -= m_Shake.y;
			m_Pitch -= m_Shake.x;
			m_Shake = Vector3.Scale(vp_SmoothRandom.GetVector3Centered(ShakeSpeed), ShakeAmplitude);
			m_Yaw += m_Shake.y;
			m_Pitch += m_Shake.x;
			m_RotationSpring.AddForce(Vector3.forward * m_Shake.z * Time.timeScale);
		}
	}

	protected virtual void UpdateBob()
	{
		if (!MuteBob && !(BobAmplitude == Vector4.zero) && !(BobRate == Vector4.zero) && Player.IsFirstPerson.Get())
		{
			m_BobSpeed = ((BobRequireGroundContact && !FPController.Grounded) ? 0f : FPController.CharacterController.velocity.sqrMagnitude);
			m_BobSpeed = Mathf.Min(m_BobSpeed * BobInputVelocityScale, BobMaxInputVelocity);
			m_BobSpeed = Mathf.Round(m_BobSpeed * 1000f) / 1000f;
			if (m_BobSpeed == 0f)
			{
				m_BobSpeed = Mathf.Min(m_LastBobSpeed * 0.93f, BobMaxInputVelocity);
			}
			m_CurrentBobAmp.y = m_BobSpeed * (BobAmplitude.y * -0.0001f);
			m_CurrentBobVal.y = Mathf.Cos(Time.time * (BobRate.y * 10f)) * m_CurrentBobAmp.y;
			m_CurrentBobVal.y = vp_MathUtility.SnapToZero(m_CurrentBobVal.y, 0.0003f);
			m_CurrentBobAmp.x = m_BobSpeed * (BobAmplitude.x * 0.0001f);
			m_CurrentBobVal.x = Mathf.Cos(Time.time * (BobRate.x * 10f)) * m_CurrentBobAmp.x;
			m_CurrentBobVal.x = vp_MathUtility.SnapToZero(m_CurrentBobVal.x, 0.0003f);
			m_CurrentBobAmp.z = m_BobSpeed * (BobAmplitude.z * 0.0001f);
			m_CurrentBobVal.z = Mathf.Cos(Time.time * (BobRate.z * 10f)) * m_CurrentBobAmp.z;
			m_CurrentBobVal.z = vp_MathUtility.SnapToZero(m_CurrentBobVal.z, 0.0003f);
			m_CurrentBobAmp.w = m_BobSpeed * (BobAmplitude.w * 0.0001f);
			m_CurrentBobVal.w = Mathf.Cos(Time.time * (BobRate.w * 10f)) * m_CurrentBobAmp.w;
			m_CurrentBobVal.w = vp_MathUtility.SnapToZero(m_CurrentBobVal.w, 0.0003f);
			m_PositionSpring.AddForce((Vector3)m_CurrentBobVal * Time.timeScale);
			AddRollForce(m_CurrentBobVal.w * Time.timeScale);
			m_LastBobSpeed = m_BobSpeed;
			DetectBobStep(m_BobSpeed, m_CurrentBobVal.y);
		}
	}

	protected virtual void DetectBobStep(float speed, float yBob)
	{
		if (BobStepCallback != null && !(speed < BobStepThreshold) && !(yBob > 0f))
		{
			if (m_LastYBob < yBob && !m_BobWasElevating)
			{
				BobStepCallback();
			}
			m_BobWasElevating = m_LastYBob < yBob;
			m_LastYBob = yBob;
		}
	}

	protected virtual void UpdateSwaying()
	{
		if (!MuteRoll)
		{
			AddRollForce((base.Transform.InverseTransformDirection(FPController.CharacterController.velocity * 0.016f) * Time.timeScale).x * RotationStrafeRoll);
		}
	}

	protected virtual void UpdateEarthQuake()
	{
		if (!MuteEarthquakes && !(Player == null) && Player.CameraEarthQuake.Active)
		{
			if (m_PositionSpring.State.y >= m_PositionSpring.RestState.y)
			{
				Vector3 o = Player.CameraEarthQuakeForce.Get();
				o.y = 0f - o.y;
				Player.CameraEarthQuakeForce.Set(o);
			}
			m_PositionSpring.AddForce(Player.CameraEarthQuakeForce.Get() * PositionEarthQuakeFactor);
			m_RotationSpring.AddForce(Vector3.forward * ((0f - Player.CameraEarthQuakeForce.Get().x) * 2f) * RotationEarthQuakeFactor);
		}
	}

	protected virtual void UpdateSprings()
	{
		m_PositionSpring.FixedUpdate();
		m_PositionSpring2.FixedUpdate();
		m_RotationSpring.FixedUpdate();
	}

	public virtual void DoBomb(Vector3 positionForce, float minRollForce, float maxRollForce)
	{
		if (!MuteBombShakes)
		{
			AddForce2(positionForce);
			float num = Random.Range(minRollForce, maxRollForce);
			if (Random.value > 0.5f)
			{
				num = 0f - num;
			}
			AddRollForce(num);
		}
	}

	public override void Refresh()
	{
		if (Application.isPlaying)
		{
			if (m_PositionSpring != null)
			{
				m_PositionSpring.Stiffness = new Vector3(PositionSpringStiffness, PositionSpringStiffness, PositionSpringStiffness);
				m_PositionSpring.Damping = Vector3.one - new Vector3(PositionSpringDamping, PositionSpringDamping, PositionSpringDamping);
				m_PositionSpring.MinState.y = PositionGroundLimit;
				m_PositionSpring.RestState = PositionOffset;
			}
			if (m_PositionSpring2 != null)
			{
				m_PositionSpring2.Stiffness = new Vector3(PositionSpring2Stiffness, PositionSpring2Stiffness, PositionSpring2Stiffness);
				m_PositionSpring2.Damping = Vector3.one - new Vector3(PositionSpring2Damping, PositionSpring2Damping, PositionSpring2Damping);
				m_PositionSpring2.MinState.y = 0f - PositionOffset.y + PositionGroundLimit;
			}
			if (m_RotationSpring != null)
			{
				m_RotationSpring.Stiffness = new Vector3(RotationSpringStiffness, RotationSpringStiffness, RotationSpringStiffness);
				m_RotationSpring.Damping = Vector3.one - new Vector3(RotationSpringDamping, RotationSpringDamping, RotationSpringDamping);
			}
			Zoom();
		}
	}

	public virtual void SnapSprings()
	{
		if (m_PositionSpring != null)
		{
			m_PositionSpring.RestState = PositionOffset;
			m_PositionSpring.State = PositionOffset;
			m_PositionSpring.Stop(true);
		}
		if (m_PositionSpring2 != null)
		{
			m_PositionSpring2.RestState = Vector3.zero;
			m_PositionSpring2.State = Vector3.zero;
			m_PositionSpring2.Stop(true);
		}
		if (m_RotationSpring != null)
		{
			m_RotationSpring.RestState = Vector3.zero;
			m_RotationSpring.State = Vector3.zero;
			m_RotationSpring.Stop(true);
		}
	}

	public virtual void StopSprings()
	{
		if (m_PositionSpring != null)
		{
			m_PositionSpring.Stop(true);
		}
		if (m_PositionSpring2 != null)
		{
			m_PositionSpring2.Stop(true);
		}
		if (m_RotationSpring != null)
		{
			m_RotationSpring.Stop(true);
		}
		m_BobSpeed = 0f;
		m_LastBobSpeed = 0f;
	}

	public virtual void Stop()
	{
		SnapSprings();
		SnapZoom();
		Refresh();
	}

	public virtual void SetRotation(Vector2 eulerAngles, bool stopZoomAndSprings)
	{
		Angle = eulerAngles;
		if (stopZoomAndSprings)
		{
			Stop();
		}
	}

	public virtual void SetRotation(Vector2 eulerAngles)
	{
		Angle = eulerAngles;
		Stop();
	}

	public virtual void SetRotation(Vector2 eulerAngles, bool stopZoomAndSprings, bool obsolete)
	{
		SetRotation(eulerAngles, stopZoomAndSprings);
	}

	public Vector3 GetLookPoint()
	{
		if (!Player.IsFirstPerson.Get() && Physics.Linecast(base.Transform.position, base.Transform.position + base.Transform.forward * 1000f, out m_LookPointHit, vp_Layer.Mask.ExternalBlockers) && !m_LookPointHit.collider.isTrigger && base.Root.InverseTransformPoint(m_LookPointHit.point).z > 0f)
		{
			return m_LookPointHit.point;
		}
		return base.Transform.position + base.Transform.forward * 1000f;
	}

	public Transform GetLookTransform(float maxRange, int layerMask = -1)
	{
		if (layerMask == -1)
		{
			layerMask = vp_Layer.Mask.ExternalBlockers;
		}
		if (Physics.Linecast(base.Transform.position, base.Transform.position + base.Transform.forward * maxRange, out m_LookPointHit, layerMask) && !m_LookPointHit.collider.isTrigger && base.Root.InverseTransformPoint(m_LookPointHit.point).z > 0f)
		{
			return m_LookPointHit.transform;
		}
		return null;
	}

	protected virtual void OnMessage_FallImpact(float impact)
	{
		if (!MuteFallImpacts)
		{
			impact = Mathf.Abs(impact * 55f);
			float t = impact * PositionKneeling;
			float t2 = impact * RotationKneeling;
			t = Mathf.SmoothStep(0f, 1f, t);
			t2 = Mathf.SmoothStep(0f, 1f, t2);
			t2 = Mathf.SmoothStep(0f, 1f, t2);
			if (m_PositionSpring != null)
			{
				m_PositionSpring.AddSoftForce(Vector3.down * t, PositionKneelingSoftness);
			}
			if (m_RotationSpring != null)
			{
				float num = ((Random.value > 0.5f) ? (t2 * 2f) : (0f - t2 * 2f));
				m_RotationSpring.AddSoftForce(Vector3.forward * num, RotationKneelingSoftness);
			}
		}
	}

	protected virtual void OnMessage_HeadImpact(float impact)
	{
		if (!MuteHeadImpacts && m_RotationSpring != null && Mathf.Abs(m_RotationSpring.State.z) < 30f)
		{
			m_RotationSpring.AddForce(Vector3.forward * (impact * 20f) * Time.timeScale);
		}
	}

	protected virtual void OnMessage_CameraGroundStomp(float impact)
	{
		if (!MuteGroundStomps)
		{
			AddForce2(new Vector3(0f, -1f, 0f) * impact);
		}
	}

	protected virtual void OnMessage_CameraBombShake(float impact)
	{
		DoBomb(new Vector3(1f, -10f, 1f) * impact, 1f, 2f);
	}

	protected virtual void OnStart_Zoom()
	{
		if (!(Player == null))
		{
			Player.Run.Stop();
		}
	}

	protected virtual bool CanStart_Run()
	{
		if (Player == null)
		{
			return true;
		}
		if (Player.Zoom.Active)
		{
			return false;
		}
		return true;
	}

	protected virtual void OnMessage_Stop()
	{
		Stop();
	}

	protected virtual void OnStart_Dead()
	{
		if (!Player.IsFirstPerson.Get())
		{
			PositionOnDeath = base.Transform.position - m_Final3rdPersonCameraOffset;
		}
	}

	protected virtual void OnStop_Dead()
	{
		if (!Player.IsFirstPerson.Get())
		{
			PositionOnDeath = Vector3.zero;
			m_Current3rdPersonBlend = 0f;
		}
	}

	protected virtual void OnMessage_CameraToggle3rdPerson()
	{
		m_Player.IsFirstPerson.Set(!m_Player.IsFirstPerson.Get());
	}
}
