#if UNITY_EDITOR
using UnityEngine;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class CameraTestSceneSetup
{
    [MenuItem("Tools/Skiing/Setup Camera Test Scene")]
    public static void SetupScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateMainCamera();
        CreateSnowGround();
        CreateSlope();
        CreateSnowboard(out GameObject snowboard, out Rigidbody rb);
        CreateCamera(snowboard, rb);
        CreateObstacles();
        CreateDirectionalLight();
        CreateBoundaryWalls();

        string path = "Assets/Scenes/CameraTestScene.unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log($"[CameraTestScene] Test scene created at: {path}");
        Debug.Log("[CameraTestScene] Controls: W/Up=Forward, S/Down=Brake, A/Left=Turn Left, D/Right=Turn Right");
        Debug.Log("[CameraTestScene] Open the scene and press Play to test!");
    }

    static void CreateMainCamera()
    {
        var cameraObj = new GameObject("Main Camera");
        cameraObj.tag = "MainCamera";
        var cam = cameraObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        cameraObj.AddComponent<AudioListener>();
        var brain = cameraObj.AddComponent<CinemachineBrain>();
        brain.m_DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Style.EaseInOut, 0.5f
        );
        cameraObj.transform.position = new Vector3(0, 5, -10);
        cameraObj.transform.LookAt(Vector3.zero);
    }

    static void CreateSnowGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "SnowGround";
        ground.transform.localScale = new Vector3(5, 1, 10);
        ground.transform.position = new Vector3(0, 0, 0);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.92f, 0.95f, 1f);
        mat.SetFloat("_Glossiness", 0.85f);
        mat.SetFloat("_Metallic", 0f);
        ground.GetComponent<Renderer>().material = mat;
    }

    static void CreateSlope()
    {
        var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slope.name = "Slope";
        slope.transform.localScale = new Vector3(50, 0.5f, 80);
        slope.transform.position = new Vector3(0, -8f, -40f);
        slope.transform.rotation = Quaternion.Euler(-12f, 0, 0);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.88f, 0.92f, 0.97f);
        mat.SetFloat("_Glossiness", 0.9f);
        mat.SetFloat("_Metallic", 0f);
        slope.GetComponent<Renderer>().material = mat;
    }

    static void CreateSnowboard(out GameObject snowboard, out Rigidbody rb)
    {
        snowboard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        snowboard.name = "Snowboard";
        snowboard.transform.localScale = new Vector3(0.8f, 0.15f, 2f);
        snowboard.transform.position = new Vector3(0, 1f, 0);
        snowboard.tag = "Player";

        rb = snowboard.AddComponent<Rigidbody>();
        rb.mass = 75f;
        rb.drag = 0f;
        rb.angularDrag = 3f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        snowboard.AddComponent<CarvingController>();

        var skierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Animation/character 1.prefab");
        if (skierPrefab != null)
        {
            var skier = (GameObject)PrefabUtility.InstantiatePrefab(skierPrefab);
            skier.name = "SkierModel";
            skier.transform.SetParent(snowboard.transform);
            skier.transform.localPosition = new Vector3(0, 0.6f, 0);
            skier.transform.localRotation = Quaternion.identity;
            skier.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);

            var animator = skier.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                Debug.Log("[CameraTestScene] Skier attached. Animator=" + (animator.runtimeAnimatorController?.name ?? "NULL") + " Avatar=" + (animator.avatar?.isHuman.ToString() ?? "NONE"));
            }
            else
            {
                Debug.LogWarning("[CameraTestScene] Skier has NO Animator component!");
            }

            var renderer = skier.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = true;

            Debug.Log("[CameraTestScene] Skier model attached as child of Snowboard at (0, 0.6, 0).");
        }
        else
        {
            Debug.LogWarning("[CameraTestScene] character 1.prefab not found at Assets/Animation/character 1.prefab. Please import it manually.");
        }
    }

    static void CreateCamera(GameObject snowboard, Rigidbody rb)
    {
        var cameraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/B3_FollowCamera.prefab");
        GameObject cameraObj;

        if (cameraPrefab != null)
        {
            cameraObj = (GameObject)PrefabUtility.InstantiatePrefab(cameraPrefab);
            var vcam = cameraObj.GetComponent<CinemachineVirtualCamera>();
            if (vcam != null)
            {
                vcam.Follow = snowboard.transform;
                vcam.LookAt = snowboard.transform;
            }
            var speedCam = cameraObj.AddComponent<SpeedDependentCamera>();
            speedCam.targetRigidbody = rb;
            Debug.Log("[CameraTestScene] Using B3_FollowCamera prefab with CinemachineCollider.");
        }
        else
        {
            cameraObj = CreateVirtualCameraFromScratch(snowboard.transform, rb);
            Debug.Log("[CameraTestScene] B3_FollowCamera prefab not found, created virtual camera from scratch.");
        }
    }

    static GameObject CreateVirtualCameraFromScratch(Transform target, Rigidbody rb)
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

    static void CreateObstacles()
    {
        CreateObstacle("Obstacle_Tall", new Vector3(6, 2, -15), new Vector3(2, 4, 2), new Color(0.5f, 0.5f, 0.55f));
        CreateObstacle("Obstacle_Wide", new Vector3(-5, 1.5f, -25), new Vector3(4, 3, 1.5f), new Color(0.55f, 0.5f, 0.5f));
        CreateObstacle("Obstacle_Thin", new Vector3(3, 3, -35), new Vector3(1, 6, 1), new Color(0.5f, 0.55f, 0.5f));
        CreateObstacle("Obstacle_Low", new Vector3(-3, 0.75f, -10), new Vector3(3, 1.5f, 3), new Color(0.6f, 0.6f, 0.6f));
    }

    static void CreateObstacle(string name, Vector3 position, Vector3 scale, Color color)
    {
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = name;
        obstacle.transform.position = position;
        obstacle.transform.localScale = scale;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        obstacle.GetComponent<Renderer>().material = mat;
    }

    static void CreateBoundaryWalls()
    {
        CreateWall("Wall_Left", new Vector3(-25, 3, -20), new Vector3(1, 6, 80));
        CreateWall("Wall_Right", new Vector3(25, 3, -20), new Vector3(1, 6, 80));
    }

    static void CreateWall(string name, Vector3 position, Vector3 scale)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.7f, 0.75f, 0.8f);
        wall.GetComponent<Renderer>().material = mat;
    }

    static void CreateDirectionalLight()
    {
        var lightObj = new GameObject("Directional Light");
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.956f, 0.839f);
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }
}
#endif
