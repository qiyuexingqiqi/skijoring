using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 滑雪音效管理器：滑行摩擦声 + 风阻声 + 下雨声。
/// 运行时自动创建，不需要在场景中挂载，也不会修改场景里的其它对象。
/// 摩擦声、风阻声的音量和音调随滑板当前速度变化；雨声跟随场景中 "Rain" 物体的开关淡入淡出。
/// </summary>
public sealed class SkiAudioManager : MonoBehaviour
{
    [Header("滑行摩擦声")]
    [Tooltip("速度达到 snowFullSpeed 时的最大音量")]
    public float snowMaxVolume = 0.7f;
    [Tooltip("音量、音调达到最大时的速度（m/s）")]
    public float snowFullSpeed = 12f;
    public float snowMinPitch = 0.9f;
    public float snowMaxPitch = 1.15f;

    [Header("风阻声")]
    [Tooltip("速度达到 windFullSpeed 时的最大音量")]
    public float windMaxVolume = 0.5f;
    [Tooltip("风阻音量、音调达到最大时的速度（m/s）")]
    public float windFullSpeed = 18f;
    public float windMinPitch = 0.9f;
    public float windMaxPitch = 1.3f;

    [Header("下雨声")]
    [Tooltip("下雨时的雨声音量")]
    public float rainVolume = 0.45f;
    [Tooltip("天气切换时雨声淡入淡出的时长（秒）")]
    public float rainFadeTime = 2f;

    [Header("音频资源路径（相对于 Resources 文件夹，不带扩展名）")]
    public string snowClipPath = "SkiAudio/ski";
    public string windClipPath = "SkiAudio/wind";
    public string rainClipPath = "SkiAudio/rain";

    private static SkiAudioManager instance;

    private AudioSource snowSource;
    private AudioSource windSource;
    private AudioSource rainSource;

    private Rigidbody playerRb;
    private GameObject rainObject;

    private float nextPlayerSearchTime;
    private float nextRainSearchTime;
    private const float PlayerSearchInterval = 0.5f;
    private const float RainSearchInterval = 2f;

    /// <summary>进入 Play 后自动创建管理器，工程里无需手动挂载。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        GameObject managerObject = new GameObject("[SkiAudioManager]");
        managerObject.AddComponent<SkiAudioManager>();
    }

    private void Awake()
    {
        // 切换场景或重新进入 Play 时避免出现重复实例
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        snowSource = CreateSource("SnowFriction", snowClipPath);
        windSource = CreateSource("Wind", windClipPath);
        rainSource = CreateSource("Rain", rainClipPath);

        SceneManager.sceneLoaded += OnSceneLoaded;

        ResolvePlayer();
        RefreshRainObject();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private AudioSource CreateSource(string sourceName, string clipPath)
    {
        AudioClip clip = Resources.Load<AudioClip>(clipPath);
        if (clip == null)
        {
            Debug.LogWarning($"[SkiAudio] 未找到音频资源：Resources/{clipPath}");
            return null;
        }

        GameObject sourceObject = new GameObject(sourceName);
        sourceObject.transform.SetParent(transform, false);

        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D 音效，不受位置影响
        source.volume = 0f;
        source.Play();

        return source;
    }

    private void Update()
    {
        // 单板、双板会互相切换，需要定期重新解析当前激活的玩家
        if (Time.unscaledTime >= nextPlayerSearchTime)
        {
            nextPlayerSearchTime = Time.unscaledTime + PlayerSearchInterval;

            if (playerRb == null || !playerRb.gameObject.activeInHierarchy)
            {
                ResolvePlayer();
            }
        }

        float speed = playerRb != null && playerRb.gameObject.activeInHierarchy ? playerRb.velocity.magnitude : 0f;

        UpdateSnowFriction(speed);
        UpdateWind(speed);
        UpdateRain();
    }

    private void UpdateSnowFriction(float speed)
    {
        if (snowSource == null) return;

        float ratio = Mathf.Clamp01(speed / Mathf.Max(0.01f, snowFullSpeed));
        if (speed < 0.3f) ratio = 0f; // 静止时完全静音

        snowSource.volume = snowMaxVolume * Mathf.Pow(ratio, 0.8f);
        snowSource.pitch = Mathf.Lerp(snowMinPitch, snowMaxPitch, ratio);
    }

    private void UpdateWind(float speed)
    {
        if (windSource == null) return;

        float ratio = Mathf.Clamp01(speed / Mathf.Max(0.01f, windFullSpeed));
        if (speed < 0.3f) ratio = 0f;

        // 高速时风阻声增长更明显
        windSource.volume = windMaxVolume * Mathf.Pow(ratio, 1.3f);
        windSource.pitch = Mathf.Lerp(windMinPitch, windMaxPitch, ratio);
    }

    private void UpdateRain()
    {
        if (rainSource == null) return;

        if (rainObject == null && Time.unscaledTime >= nextRainSearchTime)
        {
            nextRainSearchTime = Time.unscaledTime + RainSearchInterval;
            RefreshRainObject();
        }

        bool isRaining = rainObject != null && rainObject.activeInHierarchy;

        float targetVolume = isRaining ? rainVolume : 0f;
        float fadeStep = rainFadeTime > 0.01f
            ? rainVolume / rainFadeTime * Time.unscaledDeltaTime
            : rainVolume;

        rainSource.volume = Mathf.MoveTowards(rainSource.volume, targetVolume, fadeStep);
    }

    /// <summary>解析当前激活的滑板：优先双板 CarvingController，其次单板 PlayerController。</summary>
    private void ResolvePlayer()
    {
        playerRb = null;

        CarvingController carving = null;
        foreach (CarvingController candidate in FindObjectsOfType<CarvingController>(true))
        {
            if (candidate != null && candidate.gameObject.activeInHierarchy)
            {
                carving = candidate;
                break;
            }
        }

        if (carving != null)
        {
            playerRb = carving.GetComponent<Rigidbody>();
            return;
        }

        Snowboard.PlayerController lapiner = null;
        foreach (Snowboard.PlayerController candidate in FindObjectsOfType<Snowboard.PlayerController>(true))
        {
            if (candidate != null && candidate.gameObject.activeInHierarchy)
            {
                lapiner = candidate;
                break;
            }
        }

        if (lapiner != null)
        {
            playerRb = lapiner.GetComponent<Rigidbody>();
        }
    }

    /// <summary>
    /// 找到场景里由"下雨"按钮控制的 Rain 物体，并关闭它自带的 AudioSource，
    /// 避免原有雨声在激活瞬间满音量播放、和本脚本的淡入雨声重叠。
    /// </summary>
    private void RefreshRainObject()
    {
        var weather = FindObjectOfType<SkiWeatherController>();
        rainObject = weather != null ? weather.rainEffect : null;

        if (rainObject == null)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.name != "Rain") continue;
                if (!candidate.scene.IsValid() || !candidate.scene.isLoaded) continue;
                // Our own audio child is also named Rain; it is not the weather effect.
                if (candidate.transform.IsChildOf(transform) || candidate.GetComponent<ParticleSystem>() == null) continue;
                rainObject = candidate;
                break;
            }
        }
        if (rainObject == null) return;
        foreach (AudioSource source in rainObject.GetComponentsInChildren<AudioSource>(true))
        {
            source.playOnAwake = false;
            source.volume = 0f;
            source.enabled = false;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        playerRb = null;
        rainObject = null;
        nextPlayerSearchTime = 0f;
        nextRainSearchTime = 0f;
        ResolvePlayer();
        RefreshRainObject();
    }
}
