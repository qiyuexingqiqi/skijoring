using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SnowboardTestController : MonoBehaviour
{
    [Header("Movement")]
    public float forwardForce = 20f;
    public float turnTorque = 18f;
    public float brakeForce = 10f;
    public float maxSpeed = 40f;

    [Header("Drag")]
    public float forwardDrag = 0.3f;
    public float lateralDrag = 5f;
    public float angularDragValue = 3f;

    [Header("Visual")]
    public Color boardColor = Color.red;

    private Rigidbody rb;
    private float currentSpeed;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = 75f;
        rb.drag = 0f;
        rb.angularDrag = angularDragValue;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = boardColor;
        }
    }

    void FixedUpdate()
    {
        currentSpeed = rb.velocity.magnitude;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            if (currentSpeed < maxSpeed)
            {
                rb.AddForce(transform.forward * forwardForce, ForceMode.Acceleration);
            }
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            if (currentSpeed > 0.1f)
            {
                rb.AddForce(-rb.velocity.normalized * brakeForce, ForceMode.Acceleration);
            }
        }

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            rb.AddTorque(Vector3.up * -turnTorque, ForceMode.Acceleration);
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            rb.AddTorque(Vector3.up * turnTorque, ForceMode.Acceleration);
        }

        ApplyDirectionalDrag();
    }

    void ApplyDirectionalDrag()
    {
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        localVel.z *= Mathf.Clamp01(1f - forwardDrag * Time.fixedDeltaTime);
        localVel.x *= Mathf.Clamp01(1f - lateralDrag * Time.fixedDeltaTime);
        rb.velocity = transform.TransformDirection(localVel);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 18;
        style.normal.textColor = Color.white;
        style.fontStyle = FontStyle.Bold;

        GUI.Label(new Rect(10, 10, 300, 25), $"Speed: {currentSpeed:F1} m/s", style);
        GUI.Label(new Rect(10, 40, 400, 25), "W/Up: Forward  S/Down: Brake", style);
        GUI.Label(new Rect(10, 65, 400, 25), "A/Left: Turn Left  D/Right: Turn Right", style);

        GUIStyle speedStyle = new GUIStyle();
        speedStyle.fontSize = 14;
        speedStyle.normal.textColor = Color.yellow;
        float cameraDist = -6.5f;
        if (currentSpeed > 0)
        {
            cameraDist = Mathf.Lerp(-6.5f, -12f, Mathf.Clamp01(currentSpeed / 30f));
        }
        GUI.Label(new Rect(10, 95, 400, 25), $"Camera Distance: {cameraDist:F1}", speedStyle);
    }
}
