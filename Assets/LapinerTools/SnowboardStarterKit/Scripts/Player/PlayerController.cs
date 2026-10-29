using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Snowboard
{
	public enum PlayerState
	{
		Locked,
		Ground,
		TakeOff,
		Air,
		Dead,
		LevelEnd
	}

	public enum PlayerBalanceState
	{
		Center,
		Normal,
		OnEdge
	}

	public enum PlayerGrabState
	{
		NO_GRAB,
		RANDOM_GRAB
	}

	[RequireComponent (typeof (AudioSource))]
	[RequireComponent (typeof (Collider))]
	public class PlayerController : MonoBehaviour
	{
		public EPlayers m_playerIndex = EPlayers.SINGLE;
		
		public Transform damper = null;
		
		public AudioSource AdditionalAudioSource1 = null;
		public AudioSource AdditionalAudioSource2 = null;
		public AudioClip SoundLanding = null;
		public AudioClip SoundLandingLocker = null;
		public AudioClip SoundSlideSoft = null;
		public AudioClip SoundRoll = null;
		public AudioClip SoundGrind = null;

		private float AdditionalAudioSource1VolumeSmoothDampV = 0f;
		private float AdditionalAudioSource2VolumeSmoothDampV = 0f;
		private float AdditionalAudioSource2PitchSmoothDampV = 0f;

		private const float SPAWN_SAFE_TIME = 3f;

		private const float SLOMOTION_VELOCITY_MIN = 17f;
		private const float BOUNCE_OFF_WALL_MIN_INTERVAL = 0.5f;
		private const float BOUNCE_OFF_WALL_SAFE_TIME = 1.0f;
		// 低于此速度的碰撞视为擦碰，不判定死亡（对标极限国度：低速撞障碍只会减速，不会摔）
		private const float MIN_COLLISION_DEATH_SPEED = 10.0f;

		// 单滑板永不死亡：撞护栏/障碍只停下滑行，不生成尸体、不放飞人物（需求3）
		private const bool ALLOW_DEATH = false;
		
		private const int COLLISION_ABSENCE_MAX = 30;
		private const int COLLISION_ABSENCE_MAX_DIV_2 = COLLISION_ABSENCE_MAX / 2;
		
		// Handler
		private PlayerInputHandler m_inputHandler;
		private PlayerPhysicsHandler m_physicsHandler;
		private PlayerModelHandler m_modelHandler;
		private PlayerScoreHandler m_scoreHandler;
		private PlayerLandingAssistantHandler m_landingHandler;
		
		// Members
		private bool m_isSwitch = false;
		private Vector3 m_bordDirection;
		private Vector3 m_bordUpVector;
		private Vector3 m_bordUpVectorAccumulator = Vector3.zero;
		private Vector3 m_keepSameUpVectorAccumulator = Vector3.zero;
		private bool m_keepSameUpVector = false;
		private float m_lastWallBounceTime = 0.0f;
		private int m_skippedCollisionFramesNum = 0;

		private bool m_isCollidingWithTerrain = false;
		private bool m_isCollidingWithRigidBody = false;
		private int m_lastCollisionPointAccumulatorNum = 0;
		private Vector3 m_lastCollisionPointAccumulator = Vector3.zero;
		private Vector3 m_lastCollisionPoint = Vector3.zero;
		private Vector3 m_velocityLastFrame = Vector3.zero;
		private float m_lastSlideStartTime = 0.0f;
		private float m_lastRailLandingSoundTime = 0.0f;
		private float m_lastOffRoadTime = -1.0f;
		
		private PlayerState m_stateLastFrame = PlayerState.Air;
		private PlayerState m_state = PlayerState.Air;
		private PlayerGrabState m_grabState = PlayerGrabState.NO_GRAB;
		
		private int m_collisionAbsenceCounter = COLLISION_ABSENCE_MAX;

		private Vector3 m_lockedDirection = Vector3.zero;
		private Vector3 m_lockedTarget = Vector3.zero;
		
		private float m_lastJumpTime = 0.0f;
		
		private float m_airTime = 0.0f;
		
		private Vector3 m_lastFrameVelocity = Vector3.zero;
		
		private int m_takeOffCounter = 0;
		
		private bool m_isDeathMessageSuppressed = false;
		
		private Rigidbody m_cachedRigidBody = null;
		private SpringJoint m_cachedDamperJoint = null;
		private float m_cachedDamperRadius = 1;

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// PROPERTIES
		////////////////////////////////////////////////////////////////////////////////////////////////////////////

		public static EGameOverType GameOverType { get; set; }
		
		public Vector3 Velocity
		{
			get{ return m_cachedRigidBody.velocity; }
			set{ m_cachedRigidBody.velocity = value; }
		}

		public PlayerPhysicsHandler PhysicsHandler { get{ return m_physicsHandler; } }
		public PlayerScoreHandler ScoreHandler { get{ return m_scoreHandler; } }
		private PlayerInputHandler InputHandler { get{ return m_inputHandler; } }
		private PlayerModelHandler ModelHandler { get{ return m_modelHandler; } }
		private PlayerLandingAssistantHandler LandingHandler { get{ return m_landingHandler; } }

		public bool IsAccelerating { get{ return m_inputHandler.IsAccelerating; } }
		public bool IsBraking { get{ return m_inputHandler.IsBraking; } }
		public bool IsOffRoad { get{ return m_physicsHandler.IsOffroad; } }
		public bool IsOnAWall { get{ return m_physicsHandler.IsOnAWall; } }
		public bool IsSwitch { get{ return m_isSwitch; } }
		public bool IsLanding { get{ return m_landingHandler.IsAssisting; } }
		public bool IsCollidingWithTerrain { get{ return m_isCollidingWithTerrain; } }

		public float AirTime { get{ return m_airTime; } }
		public float TimeToLanding { get{ return m_landingHandler.TimeToLanding; } }
		/// <summary>
		/// 0->will go to air state, 1->solid ground state
		/// valid only if State=Ground
		/// </summary>
		public float GroundStateStability
		{
			get
			{
				if (m_state == PlayerState.Locked)
				{
					return Mathf.Clamp01(((float)m_collisionAbsenceCounter - (float)COLLISION_ABSENCE_MAX_DIV_2) / COLLISION_ABSENCE_MAX_DIV_2);
				}
				else
				{
					return Mathf.Clamp01((float)m_collisionAbsenceCounter / (float)COLLISION_ABSENCE_MAX);
				}
			}
		}

		public float LastLandTime { get{ return m_physicsHandler.LastLandTime; } }
		public float LastLandVChange { get{ return m_physicsHandler.LastLandVChange; } }
		public Vector3 LastFrameVelocity { get{ return m_lastFrameVelocity; } }
		public Vector3 LastSlideVLoss { get{ return m_physicsHandler.LastSlideVLoss; } }
		public Vector3 LastCollisionPoint { get{ return m_lastCollisionPoint; } }
		public float LastJumpTime { get{ return m_lastJumpTime; } }
		
		public Vector3 Position { get{ return transform.position; } }
		public Vector3 BordLateralDirection { get{ return Vector3.Cross(m_bordDirection, m_bordUpVector).normalized; } }
		public Vector3 BordUpVector { get{ return m_bordUpVector; } }
		public Vector3 BordDirection { get{ return m_bordDirection; } }

		public Vector3 LockedDirection { get{ return m_lockedDirection; } }	
		public Vector3 LockedTarget { get{ return m_lockedTarget; } }
		
		public PlayerState State { get{ return m_state; } }
		public PlayerGrabState GrabState { get{ return m_grabState; } }
		public PlayerBalanceState BalanceState { get{ return m_inputHandler.BalanceState; } }

		public SpringJoint DamperJoint { get{ return m_cachedDamperJoint; } }
		public float DamperRadius { get{ return m_cachedDamperRadius; } }

		public float FrameRotX { get{ return m_inputHandler.FrameRotX; } }
		public float FrameRotY { get{ return m_inputHandler.FrameRotY;} }

		public void SetBordDirection(Vector3 pBordDirection) // function istead of property for clearness of what is done
		{
			m_bordDirection = pBordDirection;
		}
		
		public void SetBordUpVector(Vector3 pBordUpVector) // function istead of property for clearness of what is done
		{
			m_bordUpVector = pBordUpVector;
		}
		
		public void SetState(PlayerState pNextState) // function istead of property for clearness of what is done
		{
			if (m_state != pNextState)
			{
				m_state = pNextState;
				if (m_state == PlayerState.LevelEnd)
				{
					m_scoreHandler.LevelEnd();
				}
			}
		}
		
		public void SetGrabState(PlayerGrabState pNextGrabState) // function istead of property for clearness of what is done
		{
			m_grabState = pNextGrabState;
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// PUBLIC METHODS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		public void OnPlayerOffRoad()
		{
			m_lastOffRoadTime = Time.time;
			m_physicsHandler.IsOffroad = true;
		}
		
		public void SuppressDeathMessage()
		{
			m_isDeathMessageSuppressed = true;
		}
		
		public void OnPlayerCollisionDeadlySurface()
		{
			die();
		}
		
		public void OnPlayerCollisionLocker()
		{
			die();
		}
		
		public void OnPlayerCollision()
		{
			StartCoroutine(WaitForEndOfFrameAndDieByCollision());
		}
		
		private IEnumerator WaitForEndOfFrameAndDieByCollision()
		{
			yield return new WaitForEndOfFrame();
			// 刚发生撞墙反弹后的一段时间内免死，且低速擦碰不判定死亡
			if (Time.time - m_lastWallBounceTime > BOUNCE_OFF_WALL_SAFE_TIME
				&& Velocity.magnitude >= MIN_COLLISION_DEATH_SPEED)
			{
				die();
			}
		}
		
		public void ApplyRotationAxis(Vector3 pAxis, float rotFactor)
		{
			if (rotFactor != 0.0f && !System.Single.IsNaN(rotFactor * Time.deltaTime))
			{
				Quaternion rotQuat = Quaternion.AngleAxis(rotFactor * Time.deltaTime, pAxis);
				m_bordDirection = rotQuat * m_bordDirection;
				m_bordUpVector = rotQuat * m_bordUpVector;
			}
		}
		
		public void GoToTakeOff()
		{
			m_state = PlayerState.TakeOff;
			m_lastJumpTime = Time.time;
		}
		
		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// MONO BEHAVIOUR METHODS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		void Awake()
		{
			GameOverType = EGameOverType.NONE;
			m_cachedRigidBody = GetComponent<Rigidbody>();
			m_cachedDamperJoint = GetComponent<SpringJoint>();
			m_cachedDamperRadius = damper.GetComponent<SphereCollider>().radius;
		}
		
		void Start ()
		{
			if (SoundLanding == null || SoundLandingLocker == null ||
				SoundSlideSoft == null || SoundGrind == null ||
			    (SoundRoll == null && AdditionalAudioSource2 != null) ||
			    (SoundRoll != null && AdditionalAudioSource2 == null))
			{
				Debug.LogWarning("PlayerController: optional audio clips are absent from the supplied resource; movement and animation remain enabled.");
			}
			
			// assign looping sounds
			if (AdditionalAudioSource2 != null)
			{
				AdditionalAudioSource2.clip = SoundRoll;
				AdditionalAudioSource2.Play();
			}
			
			m_inputHandler = new PlayerInputHandler(this, m_cachedRigidBody);
			m_physicsHandler = new PlayerPhysicsHandler(this, m_cachedRigidBody);
			m_modelHandler = new PlayerModelHandler(this);
			m_scoreHandler = new PlayerScoreHandler(this);
			m_landingHandler = new PlayerLandingAssistantHandler(this);
			
			m_bordDirection = transform.forward;
			m_bordUpVector = transform.up;
		}
		
		void FixedUpdate ()
	{	
		if (m_state != PlayerState.LevelEnd)
		{
			SnapToGroundIfBuried(); // 陷入雪地时上提，修复穿模
			m_physicsHandler.HandlePhysicsAccuracy(Velocity.magnitude);
				if (Time.time - m_lastOffRoadTime > 0.25f) {
					m_physicsHandler.IsOffroad = false;
				}
				
				// if there is no collision for multiple frames then we are in air
				if (m_state == PlayerState.TakeOff)
				{
					m_collisionAbsenceCounter -= 2;	
				}
				else
				{
					m_collisionAbsenceCounter--;
				}
				if (m_collisionAbsenceCounter < 0 || (m_state == PlayerState.Locked && m_collisionAbsenceCounter < COLLISION_ABSENCE_MAX_DIV_2))
				{
					setIsAir(true);
				}
				
				m_physicsHandler.OnFixedUpdate();
			}
		}
		
		void Update ()
		{
			if (m_state != PlayerState.LevelEnd)
			{	
				handleSounds();
				
				applyCollisionInfo();
				
				UpdateSwitchState();
				
				if (Time.timeScale > 0.01f)
				{
					// ignore controls when popups are opened or game is paused
					m_inputHandler.OnUpdate();
				}
				
				handleStates ();
				
				m_modelHandler.OnUpdate();
				m_scoreHandler.OnUpdate();
				m_landingHandler.OnUpdate();
				
				if (m_state != PlayerState.Air)
				{
					m_airTime = 0.0f;
				}
				else
				{
					m_airTime += Time.deltaTime;
				}
				
				m_stateLastFrame = m_state;
				
				m_physicsHandler.OnUpdate();
			}
			else
			{
				m_modelHandler.OnUpdate();
				m_cachedRigidBody.isKinematic = true; // stop player
				if (AdditionalAudioSource1 != null)
				{
					AdditionalAudioSource1.Stop();
				}
				if (AdditionalAudioSource2 != null)
				{
					AdditionalAudioSource2.Stop();
				}
			}
			
			m_lastFrameVelocity = Velocity;
		}

		void OnDestroy()
		{
			if (m_scoreHandler != null)
			{
				m_scoreHandler.OnUpdate();
			}
		}

		public void OnCollisionExtern(Collision collisionInfo)
		{
			saveCollisionInfo(collisionInfo);
			setIsAir(false);
			checkForLocker(collisionInfo);
		}
		
		public void UpdateSwitchState()
		{
			// update switch state
			if (m_state != PlayerState.Air)
			{
				m_isSwitch = Mathf.Sign(m_bordUpVector.y) * Mathf.Abs(Vector3.Angle(m_bordDirection, m_cachedRigidBody.velocity.normalized)) > 90;
			}
		}
		
		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		// PRIVATE METHODS
		////////////////////////////////////////////////////////////////////////////////////////////////////////////
		public void die()
		{
			die(false);
		}
			
		public void die(bool pIsByKKey)
		{
			if (!ALLOW_DEATH) return; // 单滑板永不死亡（需求3）：撞护栏只停下，不飞尸
			if (m_state != PlayerState.Dead && m_state != PlayerState.LevelEnd && Time.timeSinceLevelLoad > SPAWN_SAFE_TIME)
			{
				GameObject deadPlayer = m_modelHandler.GenerateDeadBody();
				if (pIsByKKey)
				{
					deadPlayer.GetComponent<PlayerDeadSoundManager>().SetDeathByKey();
				}
				if (m_isDeathMessageSuppressed)
				{
					deadPlayer.GetComponentInChildren<PlayerDeadHipsManager>().m_sendFailLevelMessage = false;
				}
				else if (GameOverType != EGameOverType.NONE)
				{
					deadPlayer.GetComponentInChildren<PlayerDeadHipsManager>().m_gameOverType = GameOverType;
				}
				
				Destroy(gameObject);
				
				// make the camera follow the dead body
				CameraFollow cam = CameraFollow.GetInstance(this);
				if (cam != null)
				{
					cam.target = PlayerAnimationHelper.GetHipsBone(deadPlayer);
					cam.maxDistance = 5.0f;
					cam.height = 3.0f;
					cam.cameraState = CameraFollow.State.FollowPlayerDead;
				}
				
				m_state = PlayerState.Dead;
			}
		}
		
		private void checkForLocker(Collision collisionInfo)
		{
			GameObject collObj = collisionInfo.gameObject;
			bool isLocker = collObj.tag == "Locker";
			PlayerMovementLocker locker = null;
			if (isLocker)
			{
				locker = collObj.GetComponent<PlayerMovementLocker>();
				if (locker == null && collObj.transform.parent != null)
				{
					locker = collObj.GetComponentInParent<PlayerMovementLocker>();
				}
				isLocker = locker != null;
			}
			if (m_state == PlayerState.Ground)
			{
				if (isLocker)
				{
					TryLockTarget(collisionInfo, locker);
				}
			}
			else if (m_state == PlayerState.Locked)
			{
				if (!isLocker)
				{
					m_state = PlayerState.Ground;
				}
				else
				{
					TryLockTarget(collisionInfo, locker);
				}
			}
		}
		
		private void TryLockTarget(Collision p_collisionInfo, PlayerMovementLocker p_locker)
		{
			Transform lockFrom;
			Transform lockTo;
			Vector3 collisionPointAverage = Vector3.zero;
			for (int i = 0; i < p_collisionInfo.contacts.Length; i++)
			{
				collisionPointAverage += p_collisionInfo.contacts[i].point;
			}
			collisionPointAverage /= (float)p_collisionInfo.contacts.Length;
			if (p_locker.LockPoints.Length == 0)
			{
				m_lockedDirection = m_cachedRigidBody.velocity;	
				m_lockedTarget = collisionPointAverage;
				m_state = PlayerState.Locked;
			}
			else if (p_locker.GetLockPoints(collisionPointAverage, m_cachedRigidBody.velocity, out lockTo, out lockFrom))
			{
				m_lockedDirection = (lockTo.position - lockFrom.position).normalized;	
				m_lockedTarget = lockTo.position;
				if (m_physicsHandler.LockOnRail())
				{
					m_state = PlayerState.Locked;
				}
			}		
		}
		
		private void setIsAir(bool value)
		{
			m_collisionAbsenceCounter = COLLISION_ABSENCE_MAX;
				
			switch (m_state)
			{
				case PlayerState.Locked:
				case PlayerState.Ground:
				case PlayerState.TakeOff:
					if (value)
					{
						m_state = PlayerState.Air;
					}
					break;
				case PlayerState.Air:
					if (!value)
					{
						if (m_grabState == PlayerGrabState.NO_GRAB)
						{
							m_state = PlayerState.Ground;
							KillGroundBounce(); // 落地消除向上速度，禁止再次弹起
							AlignBoardToVelocityOnLanding(); // 落地板头自动回正朝前，禁止反向
						}
						else
						{
							GameOverType = EGameOverType.LANDED_WITH_GRAB;
							die(); // tried to land while grabbed
						}
					}
					break;
			}
		}

		/// <summary>落地时清除向上速度，防止弹簧阻尼再次把人物弹起（修复：落地反弹）。</summary>
		private void KillGroundBounce()
		{
			Vector3 v = Velocity;
			if (v.y > 0f)
			{
				v.y = 0f;
				Velocity = v;
			}

			Rigidbody damperRb = m_cachedDamperJoint != null ? m_cachedDamperJoint.connectedBody : null;
			if (damperRb != null)
			{
				Vector3 dv = damperRb.velocity;
				if (dv.y > 0f)
				{
					dv.y = 0f;
					damperRb.velocity = dv;
				}
			}
		}

		/// <summary>落地时把板头方向对齐到水平速度方向，保证人物始终朝前（修复：空中旋转后落地反向）。</summary>
		private void AlignBoardToVelocityOnLanding()
		{
			Vector3 flatV = Vector3.ProjectOnPlane(Velocity, Vector3.up);
			if (flatV.magnitude > 1.5f)
			{
				m_bordDirection = flatV.normalized;
				// 保持 up 向量与新方向正交
				Vector3 lateralDirection = Vector3.Cross(m_bordDirection, m_bordUpVector);
				if (lateralDirection.sqrMagnitude > 0.0001f)
				{
					m_bordUpVector = Vector3.Cross(lateralDirection, m_bordDirection).normalized;
				}
			}
		}

		/// <summary>接地状态时若人物陷入雪地下方（穿模），强制上提到雪面（修复：开局陷雪穿模）。</summary>
		private void SnapToGroundIfBuried()
		{
			if (m_state != PlayerState.Ground && m_state != PlayerState.TakeOff) return;
			if (m_cachedRigidBody == null) return;

			Vector3 origin = transform.position + Vector3.up * 1.0f;
			if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3.5f))
			{
				// 正常悬停高度：根部离雪面约一个阻尼球半径；低于此值说明陷入雪中
				if (transform.position.y < hit.point.y + 0.05f)
				{
					Vector3 pos = transform.position;
					pos.y = hit.point.y + 0.55f; // 抬离雪面，弹簧随后自然落到稳定高度
					transform.position = pos;

					Rigidbody damperRb = m_cachedDamperJoint != null ? m_cachedDamperJoint.connectedBody : null;
					if (damperRb != null)
					{
						Vector3 dpos = damperRb.transform.position;
						dpos.y = pos.y - 0.55f;
						damperRb.transform.position = dpos;
					}
				}
			}
		}

		private void saveCollisionInfo(Collision collisionInfo)
		{
			if (collisionInfo.contacts.Length > 0)
			{
				m_isCollidingWithRigidBody = collisionInfo.rigidbody != null;
				m_isCollidingWithTerrain = collisionInfo.gameObject.layer == PlayerConfig.Instance.TERRAIN_LAYER;
				// calculate the new up vector
				foreach (ContactPoint contP in collisionInfo.contacts)
				{
					// 只取朝上的地形接触面更新板头上向量：墙面/护栏法线（几乎水平）一律忽略，
					// 否则撞护栏后 up 向量被污染成水平，A/D 转向失效、镜头前后乱转（需求1/3）
					if (!m_keepSameUpVector && contP.normal.y > 0.25f)
					{
						m_bordUpVectorAccumulator += contP.normal;
					}
					m_lastCollisionPointAccumulator+= contP.point;
					m_lastCollisionPointAccumulatorNum++;
					// dont start riding up walls
					if (m_state == PlayerState.Ground)
					{
						m_keepSameUpVectorAccumulator += contP.normal;
						if (contP.normal.y > 0.25f && (contP.normal - m_bordUpVector).magnitude < 0.3f)
						{
							m_bordUpVectorAccumulator = contP.normal;
							m_keepSameUpVectorAccumulator -= contP.normal;
							m_keepSameUpVector = true;
						}
					}
				}
			}
		}
		
		private void updateBordDirection()
		{
			// update the bord's direction
			Vector3 lateralDirection = Vector3.Cross(m_bordDirection, m_bordUpVector);
			m_bordDirection = Vector3.Cross(m_bordUpVector, lateralDirection).normalized;
		}
		
		private void applyCollisionInfo()
		{
			if (m_lastCollisionPointAccumulatorNum > 0)
			{
				m_lastCollisionPoint = m_lastCollisionPointAccumulator / (float)m_lastCollisionPointAccumulatorNum;
				m_lastCollisionPointAccumulator = Vector3.zero;
				m_lastCollisionPointAccumulatorNum = 0;
				
				// 撞护栏：直接停下滑行，不反弹、不反向（需求1/3/7：停住、不穿墙、不死、不自动跳）
				if (!m_isCollidingWithRigidBody &&
					Time.time - m_lastWallBounceTime > BOUNCE_OFF_WALL_MIN_INTERVAL &&
					m_keepSameUpVectorAccumulator.magnitude > 0.5f &&
					Vector3.Dot(m_keepSameUpVectorAccumulator, BordUpVector) < 0.5f)
				{
					m_keepSameUpVectorAccumulator.Normalize();
					if (Mathf.Abs(m_keepSameUpVectorAccumulator.y) < 0.5f)
					{
						Vector3 wallNormal = Vector3.ProjectOnPlane(m_keepSameUpVectorAccumulator, Vector3.up);
						if (wallNormal.sqrMagnitude > 0.0001f) wallNormal.Normalize();
						Vector3 v = Velocity;
						float vIntoWall = Vector3.Dot(v, wallNormal);
						if (vIntoWall < 0f)
						{
							// 只清除撞向墙面的速度分量：既停下又不反弹
							v -= vIntoWall * wallNormal;
							// 沿墙滑行分量快速衰减 → 贴墙停住
							v *= 0.85f;
							if (v.y > 0f) v.y = 0f; // 撞护栏不带向上弹跳
							Velocity = v;
							Rigidbody damperRb = m_cachedDamperJoint != null ? m_cachedDamperJoint.connectedBody : null;
							if (damperRb != null) damperRb.velocity = v;
						}
						// 板头只朝滑行方向缓慢偏转：不瞬时翻转，镜头稳定在后上方（修复：镜头卡闪、跑到人物前方）
						Vector3 flatV = Vector3.ProjectOnPlane(Velocity, Vector3.up);
						if (flatV.magnitude > 1.0f)
						{
							m_bordDirection = Vector3.Slerp(m_bordDirection, flatV.normalized, 0.4f);
						}
						m_lastWallBounceTime = Time.time;
					}
				}
				
				m_bordUpVectorAccumulator.Normalize();
				RaycastHit hit;
				Vector3 damperToCollision = m_lastCollisionPoint - damper.position;
				if (Physics.Raycast(damper.position, damperToCollision, out hit, damperToCollision.magnitude * 1.35f))
				{
					// 只接受朝上的接触面作为板头上向量（墙面法线跳过，防止 up 向量被污染）
					if (hit.normal.y > 0.35f && (m_bordUpVectorAccumulator - hit.normal).magnitude < 0.3f)
					{
						m_bordUpVector = SnapToUprightIfLowSlope(hit.normal);
						updateBordDirection();
						m_skippedCollisionFramesNum = 0;
					}
					else
					{
						if (m_skippedCollisionFramesNum > 3 && m_bordUpVectorAccumulator.sqrMagnitude > 0.01f)
						{
							m_bordUpVector = SnapToUprightIfLowSlope(m_bordUpVectorAccumulator);
							updateBordDirection();
							m_skippedCollisionFramesNum = 0;
						}
						
						m_skippedCollisionFramesNum++;
					}
				}
				m_bordUpVectorAccumulator = Vector3.zero;
			}
			m_keepSameUpVectorAccumulator = Vector3.zero;
			m_keepSameUpVector = false;
		}

		/// <summary>低坡度时保持人物站立：接触面接近水平（<16°）就取世界正上方，避免随地形抖动（需求4）。</summary>
		private Vector3 SnapToUprightIfLowSlope(Vector3 pNormal)
		{
			if (pNormal.y > 0.96f) return Vector3.up;
			return pNormal;
		}
		
		private void handleSounds()
		{
			// DEFAULT AUDIO SOURCE
			// play landing sound
			if (m_stateLastFrame == PlayerState.Air)
			{
				if (m_state == PlayerState.Ground || m_state == PlayerState.TakeOff)
				{
					if (m_airTime > 0.3f && // make sure the sound is only played on noticeable airs
						m_lastJumpTime + 0.3f < Time.time) // make sure the sound is not repeated to often
					{
						// play ground landing sound
						float landVFactor = Mathf.Abs(Vector3.Dot(m_bordUpVector, m_velocityLastFrame)) * 0.05f;
						if (landVFactor > 0.1f)
						{
							if (SoundLanding != null) GetComponent<AudioSource>().PlayOneShot(SoundLanding, landVFactor);
						}
					}
				}
				else if (m_state == PlayerState.Locked && m_lastRailLandingSoundTime + 0.3f < Time.time)
				{
					// play locker landing sound
					float landVFactor = Mathf.Abs(Vector3.Dot(m_bordUpVector, m_velocityLastFrame)) * 0.03f;
					if (SoundLandingLocker != null) GetComponent<AudioSource>().PlayOneShot(SoundLandingLocker, 0.1f + landVFactor);
					m_lastRailLandingSoundTime = Time.time;
				}
			}
			m_velocityLastFrame = m_cachedRigidBody.velocity;
			
			// ADDITIONAL AUDIO SOURCE 1
			float volume1 = 0.0f;
			// slide and grind sounds
			float bodyVLength = m_cachedRigidBody.velocity.magnitude;
			float slideV = Mathf.Abs(m_physicsHandler.LastSlideV);
			if (m_state == PlayerState.Ground)
			{
				// play sliding sound
				if (AdditionalAudioSource1 != null )
				{
					if (AdditionalAudioSource1.clip != SoundSlideSoft)
					{
						AdditionalAudioSource1.clip = SoundSlideSoft;
					}
					AdditionalAudioSource1.pitch =  Mathf.Min(0.85f, 0.35f + Mathf.Pow(bodyVLength * 0.02f, 2.0f));
					float movementVolume = 0.03f * Mathf.Clamp01(bodyVLength*0.25f) + 0.05f * Mathf.Clamp01(bodyVLength*0.05f);
					volume1 = Mathf.SmoothDamp(AdditionalAudioSource1.volume, Mathf.Clamp(movementVolume + Mathf.Clamp(Mathf.Pow(slideV * 0.075f, 2.0f), 0.0f, 1.0f) * 4.0f * m_physicsHandler.LastSlideVLoss.magnitude, 0.0f, 0.6f), ref AdditionalAudioSource1VolumeSmoothDampV, 0.1f);
					if (AdditionalAudioSource1.clip != null && !AdditionalAudioSource1.isPlaying)
					{
						AdditionalAudioSource1.Play();
						m_lastSlideStartTime = Time.realtimeSinceStartup;
					}
				}
			}
			else if (m_lastSlideStartTime + 0.1f < Time.realtimeSinceStartup)
			{
				if (m_state == PlayerState.Locked)
				{
					// play grinding sound
					if (AdditionalAudioSource1 != null )
					{
						if (AdditionalAudioSource1.clip != SoundGrind)
						{
							AdditionalAudioSource1.clip = SoundGrind;
						}
						float factor =  Mathf.Min(1.0f, bodyVLength * 0.035f);
						AdditionalAudioSource1.pitch = 0.7f;
						volume1 = 0.1f + Mathf.Pow(factor, 2.0f) * 0.6f;
						if (AdditionalAudioSource1.clip != null && !AdditionalAudioSource1.isPlaying)
						{
							AdditionalAudioSource1.Play();
							m_lastSlideStartTime = Time.realtimeSinceStartup;
						}
					}
				}
				else if (AdditionalAudioSource1 != null && AdditionalAudioSource1.isPlaying)
				{
					volume1 = 0.0f;
					AdditionalAudioSource1.Stop();
				}
			}
			if (AdditionalAudioSource1 != null)
			{
				AdditionalAudioSource1.volume = volume1;
			}
			
			
			// ADDITIONAL AUDIO SOURCE 2
			// play rolling sound
			if (AdditionalAudioSource2 != null)
			{
				if (m_state == PlayerState.Ground || m_state == PlayerState.TakeOff && bodyVLength > 2.5f)
				{
					float factor =  Mathf.Min(1.0f, bodyVLength * 0.035f);
					AdditionalAudioSource2.pitch = 0.5f + Mathf.Pow(factor, 2.0f);
					AdditionalAudioSource2.volume = factor*1.5f - volume1 * 0.5f;
				}
				else
				{
					if ((m_stateLastFrame == PlayerState.Ground || m_stateLastFrame == PlayerState.TakeOff) && bodyVLength > 2.5f)
					{
						AdditionalAudioSource2.pitch = 0.5f + 0.5f * (AdditionalAudioSource2.pitch - 0.5f);
						AdditionalAudioSource2.volume = AdditionalAudioSource2.volume * 0.5f;
					}
					
					AdditionalAudioSource2.pitch =  Mathf.SmoothDamp(AdditionalAudioSource2.pitch, 0.5f, ref AdditionalAudioSource2PitchSmoothDampV, 0.2f);
					AdditionalAudioSource2.volume = Mathf.SmoothDamp(AdditionalAudioSource2.volume, 0.0f, ref AdditionalAudioSource2VolumeSmoothDampV, 0.2f);
				}
			}
		}
		
		private void handleStates ()
		{
			// handle take off
			if (m_state == PlayerState.TakeOff)
			{
				m_takeOffCounter++;
				if (m_takeOffCounter > 20)
				{
					m_state = PlayerState.Ground;
					m_takeOffCounter = 0;
				}
			}
			else
			{
				m_takeOffCounter = 0;
			}
			
			// correct board up vector
			if (m_state == PlayerState.Locked)
			{
				Vector3 lateralDirection = Vector3.Cross(m_lockedDirection, m_bordUpVector);
				m_bordUpVector = Vector3.Cross(lateralDirection, m_lockedDirection).normalized;
			}		
		}
		
	/// <summary>左上角参数显示（与双滑板 CarvingController 的 OnGUI 保持一致风格）。</summary>
		void OnGUI()
		{
			if (global::SkiGameplayHUD.IsPresent) return;
			if (m_state == PlayerState.Dead || m_state == PlayerState.LevelEnd) return;
			if (m_cachedRigidBody == null) return;

			GUIStyle s = new GUIStyle();
			s.fontSize = 18;
			s.normal.textColor = Color.white;
			s.fontStyle = FontStyle.Bold;

			float speed = m_cachedRigidBody.velocity.magnitude;
			GUI.Label(new Rect(10, 10, 300, 25), $"Speed: {speed:F1} m/s ({speed * 3.6:F0} km/h)", s);
			GUI.Label(new Rect(10, 35, 400, 22), "W/Up: Go  S/Down: Brake  A/D: Carve", s);

			GUIStyle cs = new GUIStyle();
			cs.fontSize = 14;
			cs.normal.textColor = IsAccelerating ? Color.green : (IsBraking ? Color.red : Color.yellow);
			GUI.Label(new Rect(10, 65, 200, 22), IsAccelerating ? "ACCELERATING" : (IsBraking ? "BRAKING" : "IDLE"), cs);

			string stateStr = m_state.ToString() + (m_isSwitch ? " [SWITCH]" : "");
			cs.normal.textColor = Color.cyan;
			GUI.Label(new Rect(10, 85, 400, 22), stateStr, cs);

			if (m_state == PlayerState.Air)
			{
				cs.normal.textColor = Color.magenta;
				GUI.Label(new Rect(10, 105, 300, 22), $"AIR TIME: {m_airTime:F2}s", cs);
			}
		}

	#if UNITY_EDITOR
		private void OnDrawGizmos ()
		{
			// board direction
			Gizmos.color = Color.blue;
			Gizmos.DrawLine(transform.position, transform.position + m_bordDirection);
			// board up vector
			Gizmos.color = Color.green;
			Gizmos.DrawLine(transform.position, transform.position + m_bordUpVector);
			// locker
			Gizmos.color = Color.red;
			Gizmos.DrawSphere(m_lockedTarget, 0.25f);
			Gizmos.DrawLine(m_lockedTarget, m_lockedTarget+m_lockedDirection);
		}
	#endif
	}
}
