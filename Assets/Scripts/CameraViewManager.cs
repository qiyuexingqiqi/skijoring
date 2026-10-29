using UnityEngine;
using Cinemachine;

/// <summary>
/// 镜头视角管理器（Cinemachine）
/// 功能：自动绑定当前激活的滑板 + 多视角切换 + 地形自适应 + 转弯倾斜 + 平滑阻尼
/// 挂载位置：B3_FollowCamera 同一 GameObject
///
/// 关键设计：单板(LapinerTools)只会旋转模型，根节点朝向由物理阻尼决定，
/// 直接跟随根节点会导致镜头朝向错乱；因此这里建立一个只带水平朝向的
/// "跟随节点"(CamFollowRig)，摄像机始终跟随该节点，保证两种滑板都稳定。
/// </summary>
[DefaultExecutionOrder(3000)] // 晚于 CinemachineBrain 执行，保证防穿模修正最后生效
public class CameraViewManager : MonoBehaviour
{
    [System.Serializable]
    public struct ViewPreset
    {
        public string name;
        public Vector3 followOffset;
        public float xDamping;
        public float yDamping;
        public float zDamping;
        public float yawDamping;
        public float fov;
        [Tooltip("Transposer 绑定模式：1=跟随朝向保持世界上方(推荐) 2=跟随俯仰不翻滚 3=完全绑定")]
        public int bindingMode;
        [Tooltip("Composer 瞄准点偏移（目标本地坐标）。第一人称用 (0,1.55,12) 向前看")]
        public Vector3 trackedOffset;
        public float deadZone;
    }

    [Header("=== 视角预设 ===")]
    [SerializeField] private ViewPreset[] presets = new ViewPreset[]
    {
        new ViewPreset
        {
            name = "Follow 追随",
            // 后上方高角度：能看到前方轨道
            followOffset = new Vector3(0, 3.5f, -5.6f),
            xDamping = 0.4f, yDamping = 0.55f, zDamping = 0.3f, yawDamping = 0.3f,
            fov = 62f,
            bindingMode = 1, trackedOffset = new Vector3(0, 2.0f, 0f), deadZone = 0.06f
        },
        new ViewPreset
        {
            name = "Bird's Eye 鸟瞰",
            followOffset = new Vector3(0, 16f, -7f),
            xDamping = 1.2f, yDamping = 1.2f, zDamping = 1.2f, yawDamping = 0.5f,
            fov = 55f,
            bindingMode = 1, trackedOffset = new Vector3(0, 1.2f, 0f), deadZone = 0.2f
        },
        new ViewPreset
        {
            name = "Close 近距",
            // 后上方近距：固定距离、能看到前方
            followOffset = new Vector3(0, 2.8f, -3.8f),
            xDamping = 0.3f, yDamping = 0.4f, zDamping = 0.2f, yawDamping = 0.22f,
            fov = 68f,
            bindingMode = 1, trackedOffset = new Vector3(0, 1.9f, 0f), deadZone = 0.04f
        },
        new ViewPreset
        {
            name = "First Person 第一人称",
            // 头部后上方固定视角：持续保留头部与滑板前端在画面中
            followOffset = new Vector3(0, 2.15f, -0.85f),
            xDamping = 0f, yDamping = 0f, zDamping = 0f, yawDamping = 0f,
            fov = 75f,
            bindingMode = 2, trackedOffset = new Vector3(0, 0.7f, 9f), deadZone = 0.02f
        }
    };

    [Header("=== 玩家自动绑定 ===")]
    [Tooltip("目标失效（切换滑板）后重新查找激活滑板的间隔")]
    [SerializeField] private float rebindInterval = 0.25f;
    [Tooltip("跟随节点水平朝向的最大转向角速度（度/秒）：落地/撞墙后镜头匀速转回人物后上方，不会甩到人物前方")]
    [SerializeField] private float rigYawMaxDegPerSec = 180f;

    [Header("=== 防镜头反转 ===")]
    [Tooltip("地面跟随速度方向的最低水平速度（m/s）：低于该速度时锁定镜头朝向")]
    [SerializeField] private float minFollowSpeed = 1.5f;
    [Tooltip("空中跟随的最低水平速度（m/s）：滞空顶点速度不足时锁定镜头朝向")]
    [SerializeField] private float minAirFollowSpeed = 4f;

    [Header("=== 地形自适应 ===")]
    [Tooltip("启用后镜头会根据前方下坡地形自动升高并拉远，避免画面被地形挡住")]
    [SerializeField] private bool enableTerrainAdaptation = true;
    [SerializeField] private float forwardRaycastDist = 10f;
    [SerializeField] private float maxHeightBoost = 1.0f;
    [SerializeField] private float maxDistPullback = 0.8f;
    [SerializeField] private float terrainSmoothTime = 0.6f;

    [Header("=== 转弯倾斜 ===")]
    [SerializeField] private float turnLeanAmount = 0.15f;
    [SerializeField] private float leanSmoothTime = 0.35f;
    [Tooltip("横向速度归一化参考值（米/秒），越大倾斜越不明显")]
    [SerializeField] private float lateralReference = 3f;

    [Header("=== 过渡速度 ===")]
    [SerializeField] private float viewTransitionTime = 0.45f;

    [Header("=== 跳跃拉近 ===")]
    [Tooltip("人物在空中（飞跃跳台）时镜头拉近比例，落地后恢复")]
    [SerializeField] private float airZoomFactor = 0.7f;
    [Tooltip("拉近/恢复的平滑时间（秒），越大约缓慢")]
    [SerializeField] private float airZoomSmoothTime = 0.55f;

    // --- 组件引用 ---
    private CinemachineVirtualCamera vcam;
    private CinemachineTransposer transposer;
    private CinemachineComposer composer;
    private Transform playerTransform;
    private Rigidbody playerRigidbody;
    private CarvingController carvingController;
    private Snowboard.PlayerController singleController;
    private Transform rig;

    // --- 当前状态 ---
    private int currentViewIndex = 0;
    private float nextRebindTime;
    private Vector3 lastFlatForward = Vector3.forward;

    // --- 平滑过渡用变量 ---
    private Vector3 currentOffset;
    private Vector3 offsetVelocity;
    private float currentXDamping, xDampVelocity;
    private float currentYDamping, yDampVelocity;
    private float currentZDamping, zDampVelocity;
    private float currentYawDamping, yawDampVelocity;
    private float currentFOV, fovVelocity;
    private float currentDeadZone = 0.06f, deadZoneVelocity;

    // --- 地形自适应结果 ---
    private float terrainHeightBoost;
    private float terrainDistPullback;
    private float terrainHeightVel;
    private float terrainDistVel;

    // --- 转弯倾斜结果 ---
    private float currentLeanOffset;
    private float leanVelocity;

    // --- 目标值 ---
    private Vector3 targetOffset;
    private float targetXDamping, targetYDamping, targetZDamping, targetYawDamping, targetFOV;
    private float targetDeadZone;
    private int targetBindingMode;

    // --- 跳跃拉近 ---
    private float currentZoom = 1f;
    private float zoomVelocity;
    private Vector3 antiClipVelocity;

    // 防穿模只认地面/地形类遮挡，护栏等竖向薄片不算（避免镜头卡闪）
    private int terrainLayer;

    // ============ 公开属性 ============

    public int CurrentViewIndex => currentViewIndex;
    public int ViewCount => presets.Length;
    public string CurrentViewName => presets[currentViewIndex].name;
    public bool IsFirstPerson => presets[currentViewIndex].bindingMode == 2;

    // ============ 生命周期 ============

    void Start()
    {
        terrainLayer = LayerMask.NameToLayer("Terrain");
        vcam = GetComponent<CinemachineVirtualCamera>();
        if (vcam == null)
        {
            Debug.LogError("[CameraViewManager] 缺少 CinemachineVirtualCamera 组件，已禁用");
            enabled = false;
            return;
        }

        transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer == null)
        {
            Debug.LogError("[CameraViewManager] 缺少 CinemachineTransposer(Body) 组件，已禁用");
            enabled = false;
            return;
        }

        composer = vcam.GetCinemachineComponent<CinemachineComposer>();
        if (composer == null)
        {
            Debug.LogWarning("[CameraViewManager] 缺少 CinemachineComposer(Aim) 组件，瞄准点将无法调整");
        }

        // 禁用镜头碰撞体，避免遮挡推拉造成的画面抖动
        var camCollider = GetComponent<CinemachineCollider>();
        if (camCollider != null)
            camCollider.enabled = false;

        // 接管镜头后禁用旧的按速度调距脚本，避免两个脚本争抢 transposer
        var speedCam = GetComponent<SpeedDependentCamera>();
        if (speedCam != null)
            speedCam.enabled = false;

        // 使用 Main Camera 上的 Brain 输出，确保 Cinemachine 真正生效
        var brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
        if (brain != null && !brain.enabled)
        {
            brain.enabled = true;
            Debug.Log("[CameraViewManager] 已启用 Main Camera 上的 CinemachineBrain");
        }

        EnsureRig();
        ResolvePlayer();

        // 初始化为预设值（不产生过渡跳变）
        ApplyPresetImmediate(currentViewIndex);
        ApplyValues();
    }

    void OnDestroy()
    {
        if (rig != null)
            Destroy(rig.gameObject);
    }

    void LateUpdate()
    {
        if (vcam == null) return;

        // 目标失效时重新绑定当前激活的滑板
        if (NeedRebind() && Time.unscaledTime >= nextRebindTime)
        {
            nextRebindTime = Time.unscaledTime + rebindInterval;
            ResolvePlayer();
        }

        if (playerTransform != null)
        {
            UpdateRig();

            if (enableTerrainAdaptation && !IsFirstPerson)
                UpdateTerrainAdaptation();

            if (!IsFirstPerson)
                UpdateTurnLean();
            else
                currentLeanOffset = 0f;
        }

        // 合成最终目标偏移（含地形与转弯修正）
        Vector3 finalTargetOffset = targetOffset;
        finalTargetOffset.y += terrainHeightBoost;
        finalTargetOffset.z -= terrainDistPullback;
        finalTargetOffset.x += currentLeanOffset;

        float dt = Time.deltaTime;

        // 跳跃拉近：空中（飞跃跳台）缓慢拉近镜头但保留距离，落地后恢复固定距离
        float targetZoom = (!IsFirstPerson && IsPlayerAirborne()) ? airZoomFactor : 1f;
        currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, airZoomSmoothTime, Mathf.Infinity, dt);
        finalTargetOffset *= currentZoom;

        // 视角切换时用过渡时间平滑，平时保持稳定
        currentOffset = Vector3.SmoothDamp(currentOffset, finalTargetOffset, ref offsetVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentXDamping = Mathf.SmoothDamp(currentXDamping, targetXDamping, ref xDampVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentYDamping = Mathf.SmoothDamp(currentYDamping, targetYDamping, ref yDampVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentZDamping = Mathf.SmoothDamp(currentZDamping, targetZDamping, ref zDampVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentYawDamping = Mathf.SmoothDamp(currentYawDamping, targetYawDamping, ref yawDampVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentFOV = Mathf.SmoothDamp(currentFOV, targetFOV, ref fovVelocity, viewTransitionTime, Mathf.Infinity, dt);
        currentDeadZone = Mathf.SmoothDamp(currentDeadZone, targetDeadZone, ref deadZoneVelocity, viewTransitionTime, Mathf.Infinity, dt);

        ApplyValues();

        // 防穿模：相机不得穿入雪道地形/遮挡物
        ApplyCameraAntiClip();
    }

    // ============ 跟随节点 ============

    private void EnsureRig()
    {
        if (rig != null) return;
        var go = new GameObject("CamFollowRig");
        rig = go.transform;
    }

    /// <summary>把跟随节点对齐到玩家位置 + 水平朝向（忽略俯仰/翻滚，保证画面稳定）。</summary>
    private void UpdateRig()
    {
        rig.position = playerTransform.position;

        Vector3 flatVelocity = playerRigidbody != null
            ? Vector3.ProjectOnPlane(playerRigidbody.velocity, Vector3.up)
            : Vector3.zero;
        float flatSpeed = flatVelocity.magnitude;

        Vector3 desired = Vector3.zero;
        float yawRate = rigYawMaxDegPerSec;

        if (IsPlayerAirborne())
        {
            // 空中：镜头只朝"水平速度方向"缓慢对齐（角速度 0.4 倍），完全不受板头空翻/自旋影响，
            // 滞空顶点水平速度不足时锁定朝向；落地时镜头已大体在行进方向后上方（需求5）
            if (flatSpeed >= minAirFollowSpeed)
            {
                desired = flatVelocity / flatSpeed;
                yawRate *= 0.4f;
            }
        }
        else
        {
            // 地面：仅在"有速度且速度方向与板头基本一致"时才跟随速度方向。
            // 跳跃落地板头瞬间掉头、撞墙反弹（速度与板头反向）、停下时一律锁定镜头，
            // 防止镜头翻到人物前方导致 A/D 左右键相反；等玩家重新朝板头方向滑行后自动恢复跟随
            if (flatSpeed >= minFollowSpeed)
            {
                Vector3 flatNose = GetNoseFlatForward();
                if (flatNose.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(flatVelocity / flatSpeed, flatNose) > 0.3f)
                {
                    desired = flatVelocity / flatSpeed;
                }
            }
        }

        if (desired.sqrMagnitude < 0.00001f)
        {
            // 锁定：保持最后有效的前进方向（撞墙停下/反弹时镜头稳住不动）
            if (lastFlatForward.sqrMagnitude > 0.0001f)
                desired = lastFlatForward;
            else
                return;
        }
        else
        {
            lastFlatForward = desired;
        }

        Quaternion targetRot = Quaternion.LookRotation(desired, Vector3.up);
        // 匀速限角速度回正：永远走最短路径、每次最多 0.4x/1x rigYawMaxDegPerSec 度/秒，
        // 落地/撞墙后镜头只会平稳转回人物后方，不甩到人物前方（需求1/5）
        rig.rotation = Quaternion.RotateTowards(rig.rotation, targetRot, yawRate * Time.deltaTime);
    }

    /// <summary>判断当前滑板是否处于空中（单板 TakeOff/Air 状态；双板离地）。</summary>
    private bool IsPlayerAirborne()
    {
        if (singleController != null)
        {
            return singleController.State == Snowboard.PlayerState.Air
                || singleController.State == Snowboard.PlayerState.TakeOff;
        }
        if (carvingController != null)
        {
            return !carvingController.IsGrounded;
        }
        return false;
    }

    /// <summary>取玩家水平朝向：优先滑板板头方向，其次速度方向。</summary>
    private Vector3 GetPlayerFlatForward()
    {
        Vector3 forward = Vector3.zero;

        if (carvingController != null)
        {
            forward = playerTransform.forward;
        }
        else if (singleController != null)
        {
            // 单板的板头方向（根节点朝向不可靠）
            forward = singleController.BordDirection;
        }

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        // 速度足够大时用速度方向兜底，避免静止或朝向异常时镜头乱转
        if (forward.sqrMagnitude < 0.01f && playerRigidbody != null && playerRigidbody.velocity.sqrMagnitude > 1f)
            forward = Vector3.ProjectOnPlane(playerRigidbody.velocity, Vector3.up);

        return forward.normalized;
    }

    /// <summary>取滑板板头的水平朝向（无速度兜底），用于判断速度与板头是否同向。</summary>
    private Vector3 GetNoseFlatForward()
    {
        Vector3 forward = carvingController != null ? playerTransform.forward
            : (singleController != null ? singleController.BordDirection : playerTransform.forward);

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.zero;
    }

    /// <summary>防穿模：遮挡时拉近相机 + 雪面下限硬钳制，保证镜头不会穿出雪道。</summary>
    private void ApplyCameraAntiClip()
    {
        Camera cam = Camera.main;
        if (cam == null || playerTransform == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 lookTarget = playerTransform.position + Vector3.up * 1.3f;

        Vector3 desired = camPos;

        // 1) 相机与人物之间有遮挡物时把相机拉近（跳过角色自身碰撞体）；
        //    只认地面/地形类遮挡：护栏等竖向薄片不触发，避免贴栏滑行时镜头在护栏两侧反复横跳（需求1）
        if (LinecastIgnorePlayer(lookTarget, camPos, out RaycastHit hit))
        {
            bool groundLike = hit.normal.y > 0.7f || hit.collider.gameObject.layer == terrainLayer;
            if (groundLike)
            {
                desired = hit.point + (camPos - hit.point).normalized * 0.15f;
            }
        }

        Vector3 final = Vector3.SmoothDamp(camPos, desired, ref antiClipVelocity, 0.12f);

        // 2) 雪面下限硬钳制：相机位置绝不低到雪道地形下方（修复穿模）
        if (RaycastDownIgnorePlayer(final + Vector3.up * 6f, 9f, out RaycastHit groundHit))
        {
            float minY = groundHit.point.y + 0.35f;
            if (final.y < minY) final.y = minY;
        }

        cam.transform.position = final;
    }

    /// <summary>角色自身碰撞体是否属于该 collider 层级。</summary>
    private bool IsOwnPlayerCollider(Collider c)
    {
        if (c == null) return false;
        Transform t = c.transform;
        while (t != null)
        {
            if (t == playerTransform) return true;
            t = t.parent;
        }
        return false;
    }

    private bool LinecastIgnorePlayer(Vector3 from, Vector3 to, out RaycastHit hit)
    {
        hit = default(RaycastHit);
        bool found = false;
        float minDist = float.MaxValue;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.001f) return false;
        foreach (RaycastHit h in Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsOwnPlayerCollider(h.collider)) continue;
            if (h.distance < minDist) { minDist = h.distance; hit = h; found = true; }
        }
        return found;
    }

    private bool RaycastDownIgnorePlayer(Vector3 origin, float maxDist, out RaycastHit hit)
    {
        hit = default(RaycastHit);
        bool found = false;
        float minDist = float.MaxValue;
        foreach (RaycastHit h in Physics.RaycastAll(origin, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsOwnPlayerCollider(h.collider)) continue;
            if (h.distance < minDist) { minDist = h.distance; hit = h; found = true; }
        }
        return found;
    }

    // ============ 玩家绑定 ============

    private bool NeedRebind()
    {
        if (playerTransform == null) return true;
        return !playerTransform.gameObject.activeInHierarchy;
    }

    /// <summary>自动查找当前激活的滑板（双板 CarvingController / 单板 PlayerController）。</summary>
    private void ResolvePlayer()
    {
        CarvingController activeCarving = null;
        foreach (var c in FindObjectsOfType<CarvingController>(true))
        {
            if (c != null && c.gameObject.activeInHierarchy) { activeCarving = c; break; }
        }

        if (activeCarving != null)
        {
            Bind(activeCarving.transform, activeCarving, null);
            return;
        }

        Snowboard.PlayerController lapinerPlayer = null;
        foreach (var p in FindObjectsOfType<Snowboard.PlayerController>(true))
        {
            if (p != null && p.gameObject.activeInHierarchy) { lapinerPlayer = p; break; }
        }

        if (lapinerPlayer != null)
        {
            Bind(lapinerPlayer.transform, null, lapinerPlayer);
            return;
        }

        // 两个滑板都未激活（选择界面）时保持当前绑定
    }

    /// <summary>把摄像机绑定到指定滑板。</summary>
    public void Bind(Transform target, CarvingController carving, Snowboard.PlayerController single)
    {
        if (target == null) return;

        bool changed = playerTransform != target;
        playerTransform = target;
        carvingController = carving;
        singleController = single;
        playerRigidbody = target.GetComponent<Rigidbody>();

        EnsureRig();

        if (changed)
        {
            rig.position = target.position;
            Vector3 flatForward = GetPlayerFlatForward();
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = target.forward;
            lastFlatForward = flatForward;
            rig.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

            if (vcam != null)
            {
                vcam.Follow = rig;
                vcam.LookAt = rig;
                // 让 Cinemachine 丢弃旧目标的阻尼状态，避免切换瞬间的抽动
                vcam.PreviousStateIsValid = false;
            }

            // 切换滑板时清空地形/倾斜残留，避免一次性大偏移
            terrainHeightBoost = 0f;
            terrainDistPullback = 0f;
            currentLeanOffset = 0f;
            offsetVelocity = Vector3.zero;

            Debug.Log($"[CameraViewManager] 摄像机已绑定到: {target.name}");
        }
    }

    // ============ 核心逻辑 ============

    /// <summary>切换视角</summary>
    public void SwitchToView(int index)
    {
        if (index < 0 || index >= presets.Length) return;
        if (index == currentViewIndex) return;

        currentViewIndex = index;
        ApplyPreset(index);

        Debug.Log($"[CameraViewManager] 切换到视角: {presets[index].name}");
    }

    /// <summary>切换到下一个视角（循环）</summary>
    public void NextView()
    {
        SwitchToView((currentViewIndex + 1) % presets.Length);
    }

    /// <summary>切换到上一个视角（循环）</summary>
    public void PreviousView()
    {
        SwitchToView((currentViewIndex - 1 + presets.Length) % presets.Length);
    }

    private void ApplyPreset(int index)
    {
        var p = presets[index];

        // 切换时清空地形/倾斜残留，避免一次性大偏移
        terrainHeightBoost = 0f;
        terrainDistPullback = 0f;
        currentLeanOffset = 0f;

        targetOffset = p.followOffset;
        targetXDamping = p.xDamping;
        targetYDamping = p.yDamping;
        targetZDamping = p.zDamping;
        targetYawDamping = p.yawDamping;
        targetFOV = p.fov;
        targetDeadZone = p.deadZone;
        targetBindingMode = p.bindingMode;

        if (transposer != null)
        {
            transposer.m_BindingMode = (CinemachineTransposer.BindingMode)p.bindingMode;
            transposer.m_FollowOffset = targetOffset;
        }

        if (composer != null)
        {
            composer.m_TrackedObjectOffset = p.trackedOffset;
        }
    }

    private void ApplyPresetImmediate(int index)
    {
        ApplyPreset(index);

        currentOffset = targetOffset;
        currentXDamping = targetXDamping;
        currentYDamping = targetYDamping;
        currentZDamping = targetZDamping;
        currentYawDamping = targetYawDamping;
        currentFOV = targetFOV;
        currentDeadZone = targetDeadZone;
    }

    // ============ 地形自适应 ============

    private void UpdateTerrainAdaptation()
    {
        float targetHeightBoost = 0f;
        float targetDistPullback = 0f;

        // 用水平朝向探测前方地形，避免滑板俯仰导致射线打进地面
        Vector3 flatForward = GetPlayerFlatForward();
        if (flatForward.sqrMagnitude > 0.0001f)
        {
            Vector3 origin = playerTransform.position + Vector3.up * 0.6f;

            if (Physics.Raycast(origin, flatForward, out RaycastHit hit, forwardRaycastDist, ~0, QueryTriggerInteraction.Ignore))
            {
                float deltaY = hit.point.y - playerTransform.position.y;
                float deltaXZ = Vector3.Distance(
                    new Vector3(hit.point.x, 0f, hit.point.z),
                    new Vector3(playerTransform.position.x, 0f, playerTransform.position.z));

                if (deltaXZ > 0.5f)
                {
                    // 负值表示下坡（前方地面更低）
                    float slopeAngle = Mathf.Atan2(-deltaY, deltaXZ) * Mathf.Rad2Deg;
                    if (slopeAngle > 5f)
                    {
                        float t = Mathf.Clamp01((slopeAngle - 5f) / 25f);
                        targetHeightBoost = t * maxHeightBoost;
                        targetDistPullback = t * maxDistPullback;
                    }
                }
            }
        }

        // 上下坡都平滑过渡，不会因为射线丢失而直接跳变
        terrainHeightBoost = Mathf.SmoothDamp(terrainHeightBoost, targetHeightBoost, ref terrainHeightVel, terrainSmoothTime);
        terrainDistPullback = Mathf.SmoothDamp(terrainDistPullback, targetDistPullback, ref terrainDistVel, terrainSmoothTime);
    }

    // ============ 转弯倾斜 ============

    private void UpdateTurnLean()
    {
        float lateral = 0f;

        if (playerRigidbody != null && rig != null)
        {
            // 相对"跟随节点"(水平朝向)的横向速度分量
            Vector3 localVelocity = Quaternion.Inverse(rig.rotation) * playerRigidbody.velocity;
            lateral = Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, lateralReference), -1f, 1f);
        }
        else if (carvingController != null)
        {
            lateral = Mathf.Clamp(carvingController.turnInput, -1f, 1f);
        }

        float targetLean = lateral * turnLeanAmount;
        currentLeanOffset = Mathf.SmoothDamp(currentLeanOffset, targetLean, ref leanVelocity, leanSmoothTime);
    }

    // ============ 应用值到 Cinemachine ============

    private void ApplyValues()
    {
        if (transposer != null)
        {
            transposer.m_FollowOffset = currentOffset;
            transposer.m_XDamping = currentXDamping;
            transposer.m_YDamping = currentYDamping;
            transposer.m_ZDamping = currentZDamping;
            transposer.m_YawDamping = currentYawDamping;
        }

        if (composer != null)
        {
            composer.m_DeadZoneWidth = currentDeadZone;
            composer.m_DeadZoneHeight = currentDeadZone;
        }

        if (vcam != null)
        {
            vcam.m_Lens.FieldOfView = currentFOV;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || playerTransform == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawRay(playerTransform.position + Vector3.up * 0.6f, GetPlayerFlatForward() * forwardRaycastDist);
        if (rig != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rig.position, 0.3f);
        }
    }
}
