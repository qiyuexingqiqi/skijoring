using UnityEngine;

/// <summary>
/// Adds readable snowboard poses to an animated character.
/// W/no direction: normal glide. S: brake. S+A or S+D: side slip.
/// A/D without S keeps the original left/right carving pose.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class SnowboardCarvePose : MonoBehaviour
{
    [SerializeField, Range(0f, 35f)] private float edgeAngle = 22f;
    [SerializeField, Range(0f, 20f)] private float boardPitch = 7f;
    [SerializeField, Range(0f, 0.5f)] private float compression = 0.12f;
    [SerializeField, Range(1f, 20f)] private float smoothing = 10f;
    [SerializeField] private bool keyboardFallback = true;

    [Header("Glide / Brake / Side Slip Poses")]
    [SerializeField, Range(0f, 20f)] private float glideCrouch = 3f;
    [SerializeField, Range(0f, 35f)] private float brakeLean = 20f;
    [SerializeField, Range(0f, 0.5f)] private float brakeCompression = 0.20f;
    [SerializeField, Range(0f, 60f)] private float sideSlipAngle = 38f;
    [SerializeField, Range(0f, 35f)] private float sideSlipLean = 16f;
    [SerializeField, Range(0f, 0.5f)] private float sideSlipCompression = 0.16f;

    private const string TurnAmount = "turnInput";
    private Animator animator;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private float carveAmount;
    private float brakeAmount;
    private float sideSlipAmount;
    private bool hasTurnAmount;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        hasTurnAmount = HasParameter(TurnAmount, AnimatorControllerParameterType.Float);
    }

    private void Update()
    {
        if (animator == null || !hasTurnAmount)
            return;

        // This only previews the A/D input when no other controller sets it.
        if (keyboardFallback)
        {
            float input = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(input) > 0.01f)
                animator.SetFloat(TurnAmount, input);
        }

        float target = Mathf.Clamp(animator.GetFloat(TurnAmount), -1f, 1f);
        carveAmount = Mathf.MoveTowards(carveAmount, target, smoothing * Time.deltaTime);

        bool braking = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool sideSlipping = braking && Mathf.Abs(target) > 0.01f;
        brakeAmount = Mathf.MoveTowards(brakeAmount, braking ? 1f : 0f, smoothing * Time.deltaTime);
        sideSlipAmount = Mathf.MoveTowards(sideSlipAmount, sideSlipping ? 1f : 0f, smoothing * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (animator == null || !hasTurnAmount)
            return;

        float pressure = Mathf.Abs(carveAmount);
        float brakePose = brakeAmount * (1f - sideSlipAmount);
        float side = Mathf.Sign(carveAmount == 0f ? 1f : carveAmount);

        float pitch = glideCrouch + pressure * boardPitch + brakePose * brakeLean + sideSlipAmount * sideSlipLean;
        float yaw = sideSlipAmount * side * sideSlipAngle;
        float roll = -carveAmount * edgeAngle * (1f - sideSlipAmount) - side * sideSlipAmount * 10f;
        float drop = pressure * compression + brakePose * brakeCompression + sideSlipAmount * sideSlipCompression;

        transform.localRotation = baseRotation * Quaternion.Euler(pitch, yaw, roll);
        transform.localPosition = basePosition + Vector3.down * drop;
    }

    private bool HasParameter(string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == name && parameter.type == type)
                return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AddCarvePoseToSceneAnimators()
    {
        foreach (Animator sceneAnimator in FindObjectsByType<Animator>(FindObjectsSortMode.None))
        {
            if (sceneAnimator.GetComponent<SnowboardCarvePose>() == null)
                sceneAnimator.gameObject.AddComponent<SnowboardCarvePose>();
        }
    }
}
