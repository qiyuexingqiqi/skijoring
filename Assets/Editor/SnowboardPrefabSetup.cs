#if UNITY_EDITOR
using UnityEngine;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SnowboardPrefabSetup
{
    [MenuItem("Tools/Skiing/Create Snowboard Prefab (with Camera)")]
    public static void CreateSnowboardPrefab()
    {
        string prefabDir = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabDir))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        GameObject root = new GameObject("SnowboardWithCamera");

        GameObject board = CreateSnowboardObject();
        board.transform.SetParent(root.transform);

        var rb = board.GetComponent<Rigidbody>();

        var skierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/character 1.prefab");
        if (skierPrefab != null)
        {
            var skier = (GameObject)PrefabUtility.InstantiatePrefab(skierPrefab);
            skier.name = "SkierModel";
            skier.transform.SetParent(board.transform);
            skier.transform.localPosition = new Vector3(0, 0.6f, 0);
            skier.transform.localRotation = Quaternion.identity;
            skier.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);

            var animator = skier.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
            }

            var renderer = skier.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = true;
        }
        else
        {
            Debug.LogWarning("[SnowboardPrefab] character 1.prefab not found at Assets/Animation/character 1.prefab. Add it manually.");
        }

        GameObject cameraObj = CreateFollowCamera(board.transform, rb);
        cameraObj.transform.SetParent(root.transform);

        string prefabPath = "Assets/Prefabs/SnowboardWithCamera.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

        Object.DestroyImmediate(root);

        Debug.Log($"[SnowboardPrefab] Prefab created at: {prefabPath}");
        Debug.Log("[SnowboardPrefab] Includes: CarvingController + B3_FollowCamera + CinemachineCollider + SpeedDependentCamera");
    }

    static GameObject CreateSnowboardObject()
    {
        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Snowboard";
        board.transform.localScale = new Vector3(0.8f, 0.15f, 2f);
        board.tag = "Player";

        var rb = board.AddComponent<Rigidbody>();
        rb.mass = 75f;
        rb.drag = 0f;
        rb.angularDrag = 2.5f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.centerOfMass = new Vector3(0, -0.05f, 0);

        var physMat = new PhysicMaterial("SnowPhysics");
        physMat.dynamicFriction = 0.05f;
        physMat.staticFriction = 0.08f;
        physMat.bounciness = 0f;
        physMat.frictionCombine = PhysicMaterialCombine.Average;
        physMat.bounceCombine = PhysicMaterialCombine.Minimum;
        board.GetComponent<Collider>().material = physMat;

        board.AddComponent<CarvingController>();

        var renderer = board.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.8f, 0.1f, 0.1f);
        }

        return board;
    }

    static GameObject CreateFollowCamera(Transform target, Rigidbody rb)
    {
        var cameraObj = new GameObject("B3_FollowCamera");
        var vcam = cameraObj.AddComponent<CinemachineVirtualCamera>();
        vcam.Follow = target;
        vcam.LookAt = target;
        vcam.m_Lens.FieldOfView = 65f;
        vcam.m_Lens.NearClipPlane = 0.1f;
        vcam.m_Lens.FarClipPlane = 1000f;

        var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
        transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetOnAssign;
        transposer.m_FollowOffset = new Vector3(0, 2.5f, -6.5f);
        transposer.m_XDamping = 1.5f;
        transposer.m_YDamping = 0.6f;
        transposer.m_ZDamping = 1f;
        transposer.m_YawDamping = 0.4f;

        var composer = vcam.AddCinemachineComponent<CinemachineComposer>();
        composer.m_TrackedObjectOffset = new Vector3(0, 0.5f, 0);
        composer.m_LookaheadTime = 0.15f;
        composer.m_LookaheadSmoothing = 0.5f;
        composer.m_LookaheadIgnoreY = true;
        composer.m_HorizontalDamping = 0.3f;
        composer.m_VerticalDamping = 0.3f;
        composer.m_ScreenX = 0.5f;
        composer.m_ScreenY = 0.45f;
        composer.m_DeadZoneWidth = 0.08f;
        composer.m_DeadZoneHeight = 0.08f;
        composer.m_SoftZoneWidth = 0.5f;
        composer.m_SoftZoneHeight = 0.5f;

        var collider = cameraObj.AddComponent<CinemachineCollider>();
        collider.m_CollideAgainst = 1;
        collider.m_IgnoreTag = "Player";
        collider.m_MinimumDistanceFromTarget = 0.5f;
        collider.m_AvoidObstacles = true;
        collider.m_CameraRadius = 0.2f;
        collider.m_MaximumEffort = 4;
        collider.m_SmoothingTime = 0.4f;
        collider.m_Damping = 0.4f;

        var speedCam = cameraObj.AddComponent<SpeedDependentCamera>();
        speedCam.targetRigidbody = rb;

        return cameraObj;
    }
}
#endif
