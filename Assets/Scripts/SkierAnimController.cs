using UnityEngine;

[RequireComponent(typeof(Animator))]
public class SkierAnimController : MonoBehaviour
{
    private const string TURN_PARAM = "turnInput";

    private Animator animator;
    private bool isInitialized;
    private float currentTurnValue;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[SkierAnimController] No Animator on " + gameObject.name);
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("[SkierAnimController] No Controller assigned on Animator of " + gameObject.name +
                " — please drag SkierAnimator_Fixed.controller to the Controller field!");
            return;
        }

        if (animator.avatar == null)
        {
            Debug.LogError("[SkierAnimController] No Avatar on Animator of " + gameObject.name +
                " — please configure the Avatar in the Animator component!");
            return;
        }

        // 验证 turnInput 参数
        bool found = false;
        foreach (var p in animator.parameters)
        {
            if (p.name == TURN_PARAM) { found = true; break; }
        }

        if (!found)
        {
            Debug.LogError("[SkierAnimController] Parameter '" + TURN_PARAM + "' not found in Animator Controller!");
            return;
        }

        isInitialized = true;
        Debug.Log("[SkierAnimController] OK — ready on " + gameObject.name +
            " | controller=" + animator.runtimeAnimatorController.name +
            " | avatar=" + animator.avatar.name);
    }

    public void SetTurnAmount(float value)
    {
        if (!isInitialized || animator == null) return;

        float clamped = Mathf.Clamp(value, -1f, 1f);
        currentTurnValue = clamped;
        animator.SetFloat(TURN_PARAM, clamped);
    }

    public float GetCurrentTurnValue() => currentTurnValue;
}