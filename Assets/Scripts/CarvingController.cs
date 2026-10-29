using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarvingController : MonoBehaviour
{
    [Header("=== 变量对接 (第2组+第3组) ===")]
    [Range(-1f, 1f)]
    public float turnInput = 0f;
    public string turnInputParamName = "turnInput";

    [Header("Forward Movement")]
    public float forwardForce = 8f;
    public float maxSpeed = 11f;
    public float brakeForce = 14f;
    public float initialBoost = 0.8f; // 起步轻柔推力，避免起步瞬间腿部动作变形
    public float naturalDecay = 0.05f;

    [Header("Gravity & Slope")]
    public float slopeGravityMultiplier = 0.6f;
    public float minSlopeAngle = 5f;
    public float maxSlopeAcceleration = 3f;
    public float groundStickForce = 70f;
    public float airborneGravityMultiplier = 2f;
    public float groundCheckDistance = 1.5f;
    [Tooltip("空中最大上升速度（m/s），钳制飞行高度防止飞太高")]
    public float maxAirUpSpeed = 3.2f;

    [Header("Terrain Alignment")]
    public float terrainAlignmentSpeed = 6f;
    public float maxPitchAngle = 45f;
    public float maxRollAngle = 30f;

    [Header("Carving (Turning)")]
    public float baseTurnSpeed = 16f; // 降低转向速度，画面平稳
    public float speedTurnMultiplier = 0.8f;
    public float maxTurnSpeed = 32f;
    public float carvingForce = 6f; // 降低侧向力，防止按左右键突然偏移
    public float minCarvingSpeed = 1.5f;

    [Header("Drag")]
    public float lateralDrag = 7f;
    public float forwardDrag = 0.06f;
    public float angularDragValue = 3f;

    [Header("Visual")]
    public Color boardColor = new Color(0.8f, 0.1f, 0.1f);

    [Header("Animation")]
    public Animator skierAnimator;
    public float animSmoothSpeed = 10f; // 转向输入平滑（越小越平滑）

    [Header("Animation Triggers")]
    [Tooltip("Use the supplied Male Skier Animator Controller triggers.")]
    public bool useSkierAnimationTriggers = true;

    private Rigidbody rb;
    private float currentSpeed;
    private float steerInput;
    private float currentAnimTurnInput;
    private float currentYawAngle;
    private bool isAccelerating;
    private bool isTurning;
    private bool wasAccelerating;
    private bool isGrounded;
    private bool wasTurning;
    private bool wasBraking;
    private bool hasStartedSkiing;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = 80f;
        rb.drag = 0f;
        rb.angularDrag = angularDragValue;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.centerOfMass = new Vector3(0, -0.08f, 0);
        rb.inertiaTensor = new Vector3(0.3f, 0.8f, 0.3f);

        currentYawAngle = transform.eulerAngles.y;

        SetupPhysicsMaterial();

        if (skierAnimator == null)
        {
            skierAnimator = GetComponentInChildren<Animator>();
        }

        if (skierAnimator != null)
        {
            // The double-board model uses its own animation controller and balance pose.
            var turnBalance = skierAnimator.GetComponent<SkierTurnBalance>();
            if (turnBalance == null) turnBalance = skierAnimator.gameObject.AddComponent<SkierTurnBalance>();
            turnBalance.Initialize(this, skierAnimator);

            Debug.Log("[CarvingController] ✅ Animator: " + skierAnimator.name);
            Debug.Log("   - Controller: " + (skierAnimator.runtimeAnimatorController?.name ?? "NULL"));
            Debug.Log("   - Avatar: " + (skierAnimator.avatar != null ? skierAnimator.avatar.name : "NULL"));
            Debug.Log("   - IsHumanoid: " + (skierAnimator.avatar != null && skierAnimator.avatar.isHuman));
            
            Debug.Log("[CarvingController] Animation input is mapped to the supplied skier triggers.");
        }
        else
        {
            Debug.LogError("[CarvingController] ❌ NO Animator found in children!");
        }

        var r = GetComponent<Renderer>();
        if (r != null)
        {
            r.material = new Material(Shader.Find("Standard"));
            r.material.color = boardColor;
        }
    }

    void SetupPhysicsMaterial()
    {
        var col = GetComponent<Collider>();
        if (col != null)
        {
            var pm = new PhysicMaterial("SnowPhysics");
            pm.dynamicFriction = 0.03f;
            pm.staticFriction = 0.05f;
            pm.bounciness = 0f;
            pm.frictionCombine = PhysicMaterialCombine.Minimum;
            pm.bounceCombine = PhysicMaterialCombine.Minimum;
            col.material = pm;
        }
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            steerInput = -1f;
        else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            steerInput = 1f;
        else
            steerInput = 0f;
        
        turnInput = steerInput;

        bool newAccelerating = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        
        // 起步用轻柔的初始速度（修复：起步瞬间腿部动作歪曲变形）
        if (newAccelerating && !wasAccelerating && currentSpeed < 5f)
        {
            rb.velocity = transform.forward * initialBoost;
        }
        
        isAccelerating = newAccelerating;
        wasAccelerating = newAccelerating;
        isTurning = Mathf.Abs(steerInput) > 0.01f;

        float targetAnim = Mathf.Clamp(steerInput, -1f, 1f);
        currentAnimTurnInput = Mathf.Lerp(currentAnimTurnInput, targetAnim, animSmoothSpeed * Time.deltaTime);

        UpdateSkierAnimation(newAccelerating);
    }

    void UpdateSkierAnimation(bool accelerating)
    {
        if (skierAnimator == null || !useSkierAnimationTriggers) return;

        bool braking = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        // Start the stock animation once when the skier first moves.
        if (accelerating && !hasStartedSkiing)
        {
            skierAnimator.SetTrigger("Idle_To_Skiing");
            hasStartedSkiing = true;
        }

        // The supplied controller uses one carving clip for both left and right.
        if (isTurning && !wasTurning && !braking)
        {
            skierAnimator.SetTrigger("Skiing");
        }
        else if (!isTurning && wasTurning && !braking)
        {
            skierAnimator.SetTrigger("Skiing_To_Straight");
        }

        if (braking && !wasBraking)
        {
            skierAnimator.SetTrigger("Braking");
        }
        else if (braking && wasBraking)
        {
            skierAnimator.SetTrigger("Braking_Loop");
        }
        else if (!braking && wasBraking)
        {
            skierAnimator.SetTrigger("Skiing_Straight");
        }

        wasTurning = isTurning;
        wasBraking = braking;
    }

    void FixedUpdate()
    {
        currentSpeed = rb.velocity.magnitude;

        CheckGround();

        // 空中上升速度钳制：防止跳台冲飞太高，尽量贴地滑行
        if (!isGrounded && rb.velocity.y > maxAirUpSpeed)
        {
            Vector3 clamped = rb.velocity;
            clamped.y = maxAirUpSpeed;
            rb.velocity = clamped;
        }

        if (isAccelerating)
        {
            if (currentSpeed < maxSpeed)
                rb.AddForce(transform.forward * forwardForce, ForceMode.Acceleration);
        }
        else
        {
            ApplyNaturalDecay();
        }

        ApplySlopeGravity();
        ApplyGroundStick();

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            if (currentSpeed > 0.5f)
                rb.AddForce(-rb.velocity.normalized * brakeForce, ForceMode.Acceleration);
        }

        float speedFactor = Mathf.Clamp01(currentSpeed / maxSpeed);
        float currentTurnSpeed = Mathf.Lerp(baseTurnSpeed, maxTurnSpeed, speedFactor * speedTurnMultiplier);
        
        if (isTurning)
        {
            // 使用平滑后的转向输入（修复：按左右键人物突然偏移/转向过猛）
            currentYawAngle += currentAnimTurnInput * currentTurnSpeed * Time.fixedDeltaTime;
        }

        UpdateTerrainAlignment(speedFactor);

        ApplyCarvingForce(speedFactor);
        ApplyDirectionalDrag();
        ClampSpeed();
    }

    void CheckGround()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);
    }

    void ApplyGroundStick()
    {
        if (isGrounded)
        {
            float speedContribution = Mathf.Clamp01(currentSpeed / maxSpeed);
            float stickForce = groundStickForce * (0.5f + 0.5f * speedContribution);
            rb.AddForce(Vector3.down * stickForce, ForceMode.Force);
        }
        else
        {
            rb.AddForce(Physics.gravity * (airborneGravityMultiplier - 1) * rb.mass, ForceMode.Force);
        }
    }

    void UpdateTerrainAlignment(float speedFactor)
    {
        Vector3 terrainNormal = Vector3.up;
        
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, groundCheckDistance))
        {
            terrainNormal = hit.normal;
        }

        Vector3 forwardDir = Quaternion.Euler(0, currentYawAngle, 0) * Vector3.forward;
        Vector3 rightDir = Vector3.Cross(terrainNormal, forwardDir).normalized;
        Vector3 adjustedForward = Vector3.Cross(rightDir, terrainNormal).normalized;

        Quaternion targetRotation = Quaternion.LookRotation(adjustedForward, terrainNormal);
        
        Vector3 targetEuler = targetRotation.eulerAngles;
        targetEuler.x = NormalizeAngle(targetEuler.x);
        targetEuler.z = NormalizeAngle(targetEuler.z);
        targetEuler.x = Mathf.Clamp(targetEuler.x, -maxPitchAngle, maxPitchAngle);
        targetEuler.z = Mathf.Clamp(targetEuler.z, -maxRollAngle, maxRollAngle);
        targetRotation = Quaternion.Euler(targetEuler);

        Quaternion currentRotation = rb.rotation;
        Quaternion smoothRotation = Quaternion.Lerp(currentRotation, targetRotation, terrainAlignmentSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(smoothRotation);
    }

    float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    void ClampSpeed()
    {
        Vector3 horizVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        if (horizVel.magnitude > maxSpeed)
        {
            Vector3 clampedVel = horizVel.normalized * maxSpeed;
            rb.velocity = new Vector3(clampedVel.x, rb.velocity.y, clampedVel.z);
        }
    }

    void ApplyNaturalDecay()
    {
        Vector3 horizVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        if (horizVel.magnitude > 0.1f)
        {
            Vector3 decayForce = -horizVel.normalized * naturalDecay * rb.mass;
            rb.AddForce(decayForce, ForceMode.Force);
        }
    }

    void ApplySlopeGravity()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2f))
        {
            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
            
            if (slopeAngle > minSlopeAngle)
            {
                Vector3 slopeDirection = Vector3.ProjectOnPlane(Physics.gravity, hit.normal).normalized;

                float slopeForce = Mathf.Sin(slopeAngle * Mathf.Deg2Rad) * slopeGravityMultiplier * rb.mass;
                slopeForce = Mathf.Min(slopeForce, maxSlopeAcceleration);

                rb.AddForce(slopeDirection * slopeForce, ForceMode.Force);
            }
        }
    }

    void ApplyCarvingForce(float speedFactor)
    {
        if (!isTurning) return;
        if (currentSpeed < minCarvingSpeed) return;

        // 用水平方向的右向量施力，避免板身翻滚把侧向力带成上下方向
        Vector3 flatRight = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (flatRight.sqrMagnitude < 0.0001f) return;
        flatRight.Normalize();

        float force = carvingForce * currentAnimTurnInput * speedFactor;
        rb.AddForce(flatRight * force, ForceMode.Acceleration);
    }

    void ApplyDirectionalDrag()
    {
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        
        float speedFactor = Mathf.Clamp01(currentSpeed / maxSpeed);
        float dynamicLateralDrag = lateralDrag * (1f + speedFactor * 0.3f);
        
        localVel.x *= Mathf.Clamp01(1f - dynamicLateralDrag * Time.fixedDeltaTime);
        localVel.z *= Mathf.Clamp01(1f - forwardDrag * Time.fixedDeltaTime);
        rb.velocity = transform.TransformDirection(localVel);
    }

    public float GetCurrentSpeed() => currentSpeed;

    public float VisualTurnInput => currentAnimTurnInput;
    public bool IsGrounded => isGrounded;
    public bool IsBraking => Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

    public bool IsTurning() => isTurning;

    void OnGUI()
    {
        if (SkiGameplayHUD.IsPresent) return;
        GUIStyle s = new GUIStyle();
        s.fontSize = 18;
        s.normal.textColor = Color.white;
        s.fontStyle = FontStyle.Bold;

        GUI.Label(new Rect(10, 10, 300, 25), $"Speed: {currentSpeed:F1} m/s ({currentSpeed * 3.6:F0} km/h)", s);
        GUI.Label(new Rect(10, 35, 400, 22), "W/Up: Go  S/Down: Brake  A/D: Carve", s);

        GUIStyle cs = new GUIStyle();
        cs.fontSize = 14;
        cs.normal.textColor = isAccelerating ? Color.green : Color.yellow;
        GUI.Label(new Rect(10, 65, 200, 22), isAccelerating ? "ACCELERATING" : "IDLE", cs);

        cs.normal.textColor = isTurning ? Color.cyan : Color.yellow;
        string status = isTurning && currentSpeed > minCarvingSpeed
            ? $"CARVING [{(steerInput < 0 ? "LEFT" : "RIGHT")}]"
            : "Straight";
        GUI.Label(new Rect(10, 85, 400, 22), status, cs);

        GUIStyle as2 = new GUIStyle();
        as2.fontSize = 14;
        bool animOK = skierAnimator != null && skierAnimator.runtimeAnimatorController != null;
        as2.normal.textColor = animOK ? Color.green : Color.red;
        GUI.Label(new Rect(10, 110, 500, 22),
            animOK ? "Anim: stock skier triggers active" : "Anim: MISSING CONTROLLER",
            as2);
    }
}
