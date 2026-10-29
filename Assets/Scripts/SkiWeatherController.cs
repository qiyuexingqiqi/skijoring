using UnityEngine;

public enum SkiWeather { Sunny, Snow, Rain }

/// <summary>One mutually exclusive weather state, shared by both athletes and all camera views.</summary>
[DefaultExecutionOrder(4000)]
public sealed class SkiWeatherController : MonoBehaviour
{
    public GameObject rainEffect;
    public GameObject snowEffect;
    public Transform effectsAnchor;
    public Camera followCamera;
    public Light sunLight;
    public SkiWeather initialWeather = SkiWeather.Sunny;
    public SkiWeather CurrentWeather { get; private set; }

    private float sunnyIntensity;
    private Color sunnyLightColor;
    private static SkiWeather? sessionWeather;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { sessionWeather = null; }

    private void Awake()
    {
        if (followCamera == null) followCamera = Camera.main;
        if (sunLight != null) { sunnyIntensity = sunLight.intensity; sunnyLightColor = sunLight.color; }
        FollowView();
        SetWeather(sessionWeather ?? initialWeather);
    }

    public void SelectSunny() { SetWeather(SkiWeather.Sunny); }
    public void SelectSnow() { SetWeather(SkiWeather.Snow); }
    public void SelectRain() { SetWeather(SkiWeather.Rain); }

    public void SetWeather(SkiWeather weather)
    {
        if (weather < SkiWeather.Sunny || weather > SkiWeather.Rain) return;
        bool unchanged = CurrentWeather == weather &&
            (rainEffect == null || rainEffect.activeSelf == (weather == SkiWeather.Rain)) &&
            (snowEffect == null || snowEffect.activeSelf == (weather == SkiWeather.Snow));
        CurrentWeather = weather;
        sessionWeather = weather;
        FollowView();
        if (!unchanged)
        {
            SetEffect(rainEffect, weather == SkiWeather.Rain);
            SetEffect(snowEffect, weather == SkiWeather.Snow);
        }
        if (sunLight != null)
        {
            sunLight.intensity = sunnyIntensity * (weather == SkiWeather.Rain ? .78f : weather == SkiWeather.Snow ? .9f : 1f);
            sunLight.color = weather == SkiWeather.Sunny ? sunnyLightColor :
                Color.Lerp(sunnyLightColor, new Color(.73f, .83f, 1f), weather == SkiWeather.Rain ? .35f : .15f);
        }
    }

    private static void SetEffect(GameObject effect, bool active)
    {
        if (effect == null) return;
        if (!active)
        {
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.SetActive(false);
        }
        else
        {
            effect.SetActive(true);
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(false);
        }
    }

    private void LateUpdate() { FollowView(); }

    private void FollowView()
    {
        if (followCamera == null) followCamera = Camera.main;
        if (effectsAnchor != null && followCamera != null)
            effectsAnchor.SetPositionAndRotation(followCamera.transform.position, Quaternion.identity);
    }
}
