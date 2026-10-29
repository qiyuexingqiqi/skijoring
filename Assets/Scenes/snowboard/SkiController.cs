using UnityEngine;

public class SkiController : MonoBehaviour
{
    [Header("转向设置")]
    [SerializeField] private float steerForce = 15f;
    [SerializeField] private float torqueAmount = 5f;
    [SerializeField] private bool useTorque = false;

    [Header("前后移动设置")]
    [SerializeField] private float forwardForce = 120f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private bool useWorldForward = true;

    [Header("平滑与防弹射")]
    [SerializeField] private float smoothDamp = 0.1f;          // 速度平滑系数
    [SerializeField] private float collisionSlowFactor = 0.5f; // 碰撞后速度乘数
    [SerializeField] private float collisionPenaltyTime = 0.3f; // 碰撞后削弱推力时间

    [Header("地形摩擦力调节")]
    [SerializeField] private float baseDynamicFriction = 0.2f;
    [SerializeField] private float slopeFrictionReduction = 0.5f; // 斜坡上减小摩擦力

    private Rigidbody rb;
    private float horizontalInput;
    private float verticalInput;
    private float collisionTimer = 0f;
    private Vector3 targetVelocity = Vector3.zero;

    [HideInInspector] public float externalSteerInput = 0f;
    [HideInInspector] public float externalForwardInput = 0f;

    private PhysicMaterial skiMaterial;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("需要Rigidbody组件");
            return;
        }

        // 碰撞检测设置为连续动态，避免高速穿透地形
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // 适当降低质量，使力更敏感
        if (rb.mass > 3f) rb.mass = 1.5f;

        // 创建高度非弹性的物理材质，消除弹跳
        skiMaterial = new PhysicMaterial("SkiMaterial");
        skiMaterial.dynamicFriction = baseDynamicFriction;
        skiMaterial.staticFriction = baseDynamicFriction;
        skiMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
        skiMaterial.bounciness = 0f;          // 完全无弹性
        skiMaterial.bounceCombine = PhysicMaterialCombine.Minimum;

        Collider col = GetComponent<Collider>();
        if (col != null) col.material = skiMaterial;
        else Debug.LogWarning("找不到Collider，请添加合适的碰撞体（推荐CapsuleCollider）");
    }

    void Update()
    {
        // 输入处理
        horizontalInput = Mathf.Abs(externalSteerInput) > 0.01f ? externalSteerInput : Input.GetAxis("Horizontal");
        verticalInput = Mathf.Abs(externalForwardInput) > 0.01f ? externalForwardInput : Input.GetAxis("Vertical");

        if (collisionTimer > 0) collisionTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // 1. 动态调整摩擦力（根据地形坡度）
        AdjustFrictionBasedOnSlope();

        // 2. 转向力
        if (useTorque)
            rb.AddTorque(transform.up * horizontalInput * torqueAmount);
        else
            rb.AddForce(transform.right * horizontalInput * steerForce);

        // 3. 前后推力（碰撞惩罚期间大幅度削弱）
        float effectiveForwardForce = forwardForce;
        if (collisionTimer > 0) effectiveForwardForce *= 0.2f;

        Vector3 forwardDir = useWorldForward ? Vector3.forward : transform.forward;
        Vector3 targetMove = forwardDir * verticalInput * effectiveForwardForce;
        rb.AddForce(targetMove);

        // 4. 速度限制与平滑（避免突变）
        Vector3 horizVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        if (horizVel.magnitude > maxSpeed)
        {
            Vector3 clampedVel = horizVel.normalized * maxSpeed;
            rb.velocity = new Vector3(clampedVel.x, rb.velocity.y, clampedVel.z);
        }

        // 5. 轻微的速度阻尼，使地形滑动更平顺
        if (Mathf.Abs(verticalInput) < 0.1f && horizVel.magnitude > 0.5f)
        {
            Vector3 dampingForce = -horizVel * (smoothDamp * rb.mass);
            rb.AddForce(dampingForce);
        }
    }

    // 根据地形坡度调整摩擦力（使上下坡更自然）
    private void AdjustFrictionBasedOnSlope()
    {
        if (!Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 1.5f))
            return;

        float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
        float targetFriction = baseDynamicFriction;
        if (slopeAngle > 15f)
        {
            // 坡度越大摩擦力越小，更容易滑动
            targetFriction = baseDynamicFriction * (1f - slopeFrictionReduction);
            targetFriction = Mathf.Clamp(targetFriction, 0.01f, baseDynamicFriction);
        }
        skiMaterial.dynamicFriction = targetFriction;
        skiMaterial.staticFriction = targetFriction;
    }

    // 碰撞响应：降低速度并进入惩罚期
    private void OnCollisionEnter(Collision collision)
    {
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed > 1f)
        {
            // 降低碰撞后的速度，避免弹飞
            Vector3 newVel = rb.velocity;
            newVel.x *= collisionSlowFactor;
            newVel.z *= collisionSlowFactor;
            rb.velocity = newVel;
            // 开启惩罚计时，期间推力减弱
            collisionTimer = collisionPenaltyTime;
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying && rb != null)
        {
            Gizmos.color = collisionTimer > 0 ? Color.red : Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.color = Color.blue;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 1.5f))
            {
                Gizmos.DrawLine(transform.position, hit.point);
            }
        }
    }
}