using UnityEngine;
using UnityEngine.SceneManagement;

// Scene adapter only: the source package remains responsible for motion and animation.
public sealed class SnowboardSceneAdapter : MonoBehaviour
{
    public GameObject editorPreview;
    void Awake()
    {
        if (editorPreview != null) Destroy(editorPreview);
        Time.timeScale = 1f;
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && FindObjectOfType<SkiRaceManager>() == null)
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
