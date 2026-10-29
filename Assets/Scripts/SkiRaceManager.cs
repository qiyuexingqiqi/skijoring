using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shared race clock and finish flow for both equipment types.</summary>
public sealed class SkiRaceManager : MonoBehaviour
{
    public GearSelect gearSelection;
    public GameObject timerPanel;
    public Text timerText;
    public Text equipmentText;
    public GameObject completionPanel;
    public Text resultTimeText;
    public Text resultEquipmentText;
    public Button retryButton;

    public bool IsRunning { get; private set; }
    public bool IsFinished { get; private set; }
    public GameObject ActivePlayer { get; private set; }
    public float FinishSpeed { get; private set; }
    public double ElapsedSeconds => IsRunning ? Math.Max(0d, Time.timeAsDouble - startedAt) : elapsed;
    public string EquipmentName => doubleBoard ? "双板滑雪" : "单板滑雪";

    private double startedAt;
    private double elapsed;
    private bool doubleBoard;
    private static int replayEquipment = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { replayEquipment = -1; }

    private void Awake()
    {
        if (timerPanel != null) timerPanel.SetActive(false);
        if (completionPanel != null) completionPanel.SetActive(false);
    }

    private void Start()
    {
        int replay = replayEquipment;
        replayEquipment = -1;
        if (gearSelection == null || replay < 0) return;
        if (replay == 1) gearSelection.OnClickDouble();
        else gearSelection.OnClickSingle();
    }

    public void BeginRun(GameObject player, bool useDoubleBoard)
    {
        if (player == null || IsFinished) return;
        if (IsRunning && ActivePlayer == player) return;
        ActivePlayer = player;
        doubleBoard = useDoubleBoard;
        elapsed = 0d;
        startedAt = Time.timeAsDouble;
        IsRunning = true;
        IsFinished = false;
        if (timerPanel != null) timerPanel.SetActive(true);
        if (completionPanel != null) completionPanel.SetActive(false);
        if (equipmentText != null) equipmentText.text = EquipmentName + " · 滑行计时";
        RefreshClock();
    }

    private void Update()
    {
        if (IsRunning) RefreshClock();
        if ((IsRunning || IsFinished) && Input.GetKeyDown(KeyCode.R)) ReturnToSelection();
    }

    private void RefreshClock()
    {
        if (timerText != null) timerText.text = FormatTime(ElapsedSeconds);
    }

    public bool TryFinish(Collider entrant)
    {
        if (!IsRunning || ActivePlayer == null || entrant == null) return false;
        if (entrant.transform != ActivePlayer.transform && !entrant.transform.IsChildOf(ActivePlayer.transform)) return false;
        var body = ActivePlayer.GetComponent<Rigidbody>();
        if (body == null) return false;

        elapsed = ElapsedSeconds;
        IsRunning = false;
        IsFinished = true;
        FinishSpeed = body.velocity.magnitude;
        // Leave the weather and UI running; stop only the athlete's motion/input.
        var snowboard = ActivePlayer.GetComponent<Snowboard.PlayerController>();
        if (snowboard != null) snowboard.enabled = false;
        var skis = ActivePlayer.GetComponent<CarvingController>();
        if (skis != null) skis.enabled = false;
        foreach (var athleteBody in ActivePlayer.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!athleteBody.isKinematic)
            {
                athleteBody.velocity = Vector3.zero;
                athleteBody.angularVelocity = Vector3.zero;
            }
            athleteBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            athleteBody.isKinematic = true;
        }
        RefreshClock();
        if (timerPanel != null) timerPanel.SetActive(false);
        if (resultTimeText != null) resultTimeText.text = FormatTime(elapsed);
        if (resultEquipmentText != null) resultEquipmentText.text = EquipmentName + "  /  本次用时";
        if (completionPanel != null) completionPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (EventSystem.current != null && retryButton != null)
            EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
        return true;
    }

    public void Retry()
    {
        replayEquipment = doubleBoard ? 1 : 0;
        Reload();
    }

    public void ReturnToSelection()
    {
        replayEquipment = -1;
        Reload();
    }

    private void Reload()
    {
        Time.timeScale = 1f;
        var scene = gameObject.scene;
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    public static string FormatTime(double seconds)
    {
        long centiseconds = (long)Math.Floor(Math.Max(0d, seconds) * 100d);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:00}",
            centiseconds / 6000, centiseconds / 100 % 60, centiseconds % 100);
    }
}
