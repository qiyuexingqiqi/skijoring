using UnityEngine;
using Cinemachine;

[RequireComponent(typeof(CinemachineVirtualCamera))]
public class SpeedDependentCamera : MonoBehaviour
{
    [Header("Target")]
    public Rigidbody targetRigidbody;

    [Header("Cinemachine Damping")]
    public float xDamping = 2f;
    public float yDamping = 2f;
    public float zDamping = 2f;

    [Header("Camera Distance")]
    public float baseDistance = -4.5f;
    public float turnDistanceBoost = 0.5f;
    public float distanceSmoothTime = 0.8f;

    [Header("Height")]
    public float baseHeight = 3.2f;
    public float heightSmoothTime = 0.6f;

    [Header("Field of View")]
    public float minFOV = 60f;
    public float maxFOV = 75f;
    public float maxSpeed = 22f;
    public float fovSpeed = 4f;

    private CinemachineVirtualCamera vcam;
    private CinemachineTransposer transposer;
    private float currentDistance;
    private float distanceVelocity;
    private float currentHeight;
    private float heightVelocity;

    void Awake()
    {
        vcam = GetComponent<CinemachineVirtualCamera>();
        transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer != null)
        {
            transposer.m_XDamping = xDamping;
            transposer.m_YDamping = yDamping;
            transposer.m_ZDamping = zDamping;
            currentDistance = baseDistance;
            currentHeight = baseHeight;
        }
    }

    void LateUpdate()
    {
        if (transposer == null || targetRigidbody == null) return;

        float speed = targetRigidbody.velocity.magnitude;
        float t = Mathf.Clamp01(speed / maxSpeed);

        float targetDistance = baseDistance;
        if (Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f && speed > 5f)
        {
            targetDistance -= turnDistanceBoost;
        }

        currentDistance = Mathf.SmoothDamp(
            currentDistance,
            targetDistance,
            ref distanceVelocity,
            distanceSmoothTime
        );

        currentHeight = Mathf.SmoothDamp(
            currentHeight,
            baseHeight,
            ref heightVelocity,
            heightSmoothTime
        );

        Vector3 offset = transposer.m_FollowOffset;
        offset.z = currentDistance;
        offset.y = currentHeight;
        transposer.m_FollowOffset = offset;

        float targetFOV = Mathf.Lerp(minFOV, maxFOV, t);
        vcam.m_Lens.FieldOfView = Mathf.Lerp(
            vcam.m_Lens.FieldOfView,
            targetFOV,
            Time.deltaTime * fovSpeed
        );
    }
}