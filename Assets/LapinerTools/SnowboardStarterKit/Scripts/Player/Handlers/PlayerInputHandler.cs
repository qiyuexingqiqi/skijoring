using UnityEngine;
using System.Collections;

namespace Snowboard
{
	public class PlayerInputHandler
		{
			// Rotation Constants
		private const float CTRL_SMOOTH_TIME_AIR = 3f; // Time.deltaTime * CTRL_SMOOTH_TIME（降低=转向更平滑）
		private const float CTRL_SMOOTH_TIME_GROUND = 6f; // 加快地面转向响应，对齐双板手感（需求2）
		private const float CTRL_SMOOTH_TIME_BOMB = 2f; // 高速时转向响应（需求2）
			// 空中停留达到此时长才允许前后翻转，避免小颠簸瞬间触发翻转
			private const float MIN_AIR_TIME_FOR_FLIP = 0.25f;

		// vars
		private Rigidbody m_cachedRigidBody;
		private PlayerController m_playerCtrl;
		private bool m_isAlwaysAccelerate = true;
		
		private float m_controlSmootherAxisX = 0.0f;
		
		public float FrameRotX { get; private set; }
		public float FrameRotY { get; private set; }
		public bool IsBraking { get; private set; }
		public bool IsAccelerating { get; private set; }
		public PlayerBalanceState BalanceState { get; private set; }
		
		public PlayerInputHandler(PlayerController p_playerCtrl, Rigidbody p_cachedRigidBody)
		{
			m_playerCtrl = p_playerCtrl;
			m_cachedRigidBody = p_cachedRigidBody;
			FrameRotX = 0f;
			FrameRotY = 0f;
			IsBraking = false;
			BalanceState = PlayerBalanceState.Normal;
			m_isAlwaysAccelerate = PlayerConfig.Instance.ALWAYS_ACCELERATE;
			IsAccelerating = m_isAlwaysAccelerate;
			// reset player input
			PlayerInput.Destroy(m_playerCtrl.m_playerIndex);
			PlayerInput.Create(m_playerCtrl.m_playerIndex);
		}
		
		public void OnUpdate()
		{
			PlayerInput.SetActiveIndex(m_playerCtrl.m_playerIndex);

			// reset acceleration and braking variables
			FrameRotX = 0.0f;
			FrameRotY = 0.0f;
			bool wasBraking = IsBraking;
			IsBraking = false;
			IsAccelerating = false;
			
			switch (m_playerCtrl.State)
			{
				case PlayerState.Ground:
				{
					// BALANCE STATE, ACCELERATE & BRAKE STATE
					if (!PlayerInput.IsSlide)
					{
						BalanceState = PlayerBalanceState.OnEdge;
				
						// accelerate
						if ( PlayerInput.AxisY > 0.5f || m_isAlwaysAccelerate)
						{
							IsBraking = false;
							IsAccelerating = true;
						}
						// brake
						if ( PlayerInput.AxisY < -0.9f )
						{
							IsBraking = true;
							IsAccelerating = false;
						}
					}
					else
					{
						// balance on center
						if ( PlayerInput.AxisY > 0.7f )
						{
							BalanceState = PlayerBalanceState.Center;
						}
						//  balance on edge
						else if ( PlayerInput.AxisY < -0.7f)
						{
							BalanceState = PlayerBalanceState.OnEdge;
						}
						// balance is normal
						else
						{
							BalanceState = PlayerBalanceState.Normal;
						}
					}

					handleBraking(wasBraking);
					handleJumps();
					handleGroundRot();
					break;
				}
				case PlayerState.Locked:
					handleBraking(wasBraking);
					handleJumps();
					handleGroundRot();
					break;

				case PlayerState.TakeOff: // idle state
					handleBraking(wasBraking);
					break;

				case PlayerState.Air:
				{
					handleGrabs();
					handleBraking(wasBraking);
					handleAirRot();
					break;
				}
				case PlayerState.Dead: // idle state
					break;
			}

			// self kill key
			if (Input.GetKeyUp(KeyCode.K) && m_playerCtrl.m_playerIndex == EPlayers.SINGLE)
			{
				m_playerCtrl.die(true);
			}
		}
		
		private void handleGrabs()
		{
			if (PlayerInput.IsGrab && m_playerCtrl.State == PlayerState.Air)
			{
				m_playerCtrl.SetGrabState(PlayerGrabState.RANDOM_GRAB);
			}
			else
			{
				m_playerCtrl.SetGrabState(PlayerGrabState.NO_GRAB);
			}
		}
		
		private void handleBraking(bool pWasBraking)
		{
			// 刹车只做减速，不再旋转板头（修复：按S板头反向180°的问题）
			if (!IsBraking)
			{
				if (pWasBraking != IsBraking)
				{
					m_playerCtrl.UpdateSwitchState();
				}
			}
		}
		
		private void handleJumps()
		{
			if (Time.time - m_playerCtrl.LastJumpTime > PlayerConfig.Instance.JUMP_INTERVAL)
			{
				// JUMP
				if ( PlayerInput.IsJump )
				{
					m_cachedRigidBody.velocity = m_cachedRigidBody.velocity + new Vector3(0.0f, m_playerCtrl.State != PlayerState.Locked ? PlayerConfig.Instance.JUMP_UP_SPEED : PlayerConfig.Instance.JUMP_UP_SPEED * 0.5f, 0.0f);
					m_playerCtrl.GoToTakeOff();
				}
			}
		}
		
		private void handleGroundRot()
		{
			if (IsBraking)
			{
				return; // cannot steer while braking
			}
			
			// SLIDE
			float v = m_cachedRigidBody.velocity.magnitude;
			smoothControls(v);
			float rotFactor = m_controlSmootherAxisX;
			if (v < PlayerConfig.Instance.MIN_SPEED_HILL )
			{
				rotFactor *= ((PlayerConfig.Instance.MIN_SPEED_HILL - v) * PlayerConfig.Instance.FLAT_ROT_SPEED + v * PlayerConfig.Instance.HILL_ROT_SPEED) / PlayerConfig.Instance.MIN_SPEED_HILL;
			}
			else
			{
				rotFactor *= PlayerConfig.Instance.HILL_ROT_SPEED;
			}
			if (IsAccelerating)
			{
				rotFactor *= PlayerConfig.Instance.BOMB_ROT_FACTOR;
			}
			if (BalanceState == PlayerBalanceState.OnEdge && m_playerCtrl.State != PlayerState.Locked) // if player is on edge he cannot rotate that fast (carve first)
			{
				rotFactor *= PlayerConfig.Instance.CARVE_ROT_FACTOR;
			}
		
			applyRotationYAxis(rotFactor);
		}
		
		private void handleAirRot()
		{
			smoothControls(0f);
			// AIR CONTROL
			float rotFactorX = 0.0f;
			float rotFactorY = 0.0f;
			// rotate left or right
			rotFactorY = PlayerConfig.Instance.AIR_ROT_SPEED*m_controlSmootherAxisX;
			
			// flips only if no landing assistance needed
				// 且需要真正的滞空时间（避免轻微起跳就翻转）
				if (m_playerCtrl.AirTime > MIN_AIR_TIME_FOR_FLIP
					&& !m_playerCtrl.IsLanding && m_playerCtrl.TimeToLanding < 0.5f)
				{
					// rotate front or back
					rotFactorX = PlayerConfig.Instance.AIR_ROT_SPEED*PlayerInput.AxisY;
					if (m_playerCtrl.IsSwitch)
					{
						rotFactorX *= -1f;
					}
				}
			
			float rotVectLength = Mathf.Sqrt(rotFactorX*rotFactorX + rotFactorY*rotFactorY);
			if (rotVectLength > PlayerConfig.Instance.AIR_ROT_SPEED)
			{
				rotFactorX *= PlayerConfig.Instance.AIR_ROT_SPEED / rotVectLength;
				rotFactorY *= PlayerConfig.Instance.AIR_ROT_SPEED / rotVectLength;
			}
			
			applyRotationXAxis(rotFactorX);
			applyRotationYAxis(rotFactorY);
		}
		
		private void smoothControls(float v)
		{
			if (PlayerInput.AxisX != 0f)
			{
				float smoothTime;
				if (m_playerCtrl.State == PlayerState.Air)
				{
					smoothTime = CTRL_SMOOTH_TIME_AIR;
				}
				else
				{
					smoothTime = CTRL_SMOOTH_TIME_GROUND;
					if (v > PlayerConfig.Instance.MIN_SPEED_HILL && BalanceState == PlayerBalanceState.OnEdge)
					{
						if (v < PlayerConfig.Instance.MIN_SPEED_BOMB)
						{
							smoothTime = Mathf.Lerp(CTRL_SMOOTH_TIME_GROUND, CTRL_SMOOTH_TIME_BOMB, (v-PlayerConfig.Instance.MIN_SPEED_HILL)/(PlayerConfig.Instance.MIN_SPEED_BOMB-PlayerConfig.Instance.MIN_SPEED_HILL));
						}
						else
						{
							smoothTime = CTRL_SMOOTH_TIME_BOMB;
						}
					}
				}
				m_controlSmootherAxisX = Mathf.Lerp(m_controlSmootherAxisX, PlayerInput.AxisX, Time.deltaTime * smoothTime);
			}
			else
			{
				m_controlSmootherAxisX = 0f;
			}
		}
		
		private void applyRotationXAxis(float rotFactor)
		{
			if (rotFactor != 0.0f && !System.Single.IsNaN(rotFactor * Time.deltaTime))
			{
				FrameRotX += rotFactor * Time.deltaTime;
				Quaternion rotQuat = Quaternion.AngleAxis(rotFactor * Time.deltaTime, Vector3.Cross(m_playerCtrl.BordUpVector, m_playerCtrl.BordDirection));
				m_playerCtrl.SetBordDirection(rotQuat * m_playerCtrl.BordDirection);
				m_playerCtrl.SetBordUpVector(rotQuat * m_playerCtrl.BordUpVector);
			}
		}
		
		private void applyRotationYAxis(float rotFactor)
		{
			if (rotFactor != 0.0f && !System.Single.IsNaN(rotFactor * Time.deltaTime))
			{
				FrameRotY += rotFactor * Time.deltaTime;
				Quaternion rotQuat = Quaternion.AngleAxis(rotFactor * Time.deltaTime, m_playerCtrl.BordUpVector);
				m_playerCtrl.SetBordDirection(rotQuat * m_playerCtrl.BordDirection);
			}
		}
	}
}
