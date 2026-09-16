using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button), typeof(Image))]
public class RunToggleButton : MonoBehaviour
{
    [SerializeField] private PlayerMotor playerMotor;
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private bool startEnabled;

    private bool runRequested;
    private bool wasLocked;

    private bool IsLocked => TutorialManager.Instance != null && TutorialManager.Instance.IsRunLocked;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (icon == null) icon = GetComponent<Image>();
        if (playerMotor == null) playerMotor = FindObjectOfType<PlayerMotor>();

        runRequested = startEnabled;
        ApplyToMotor();
        RefreshUI();
    }

    private void OnEnable()
    {
        if (button != null) button.onClick.AddListener(ToggleRun);
        RefreshUI();
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(ToggleRun);
    }

    private void Update()
    {
        if (playerMotor == null)
        {
            playerMotor = FindObjectOfType<PlayerMotor>();
            ApplyToMotor();
        }

        if (wasLocked != IsLocked) RefreshUI();
    }

    public void ToggleRun()
    {
        if (IsLocked) return;
        SetRunEnabled(!runRequested);
    }

    public void SetRunEnabled(bool enabled)
    {
        runRequested = enabled;
        ApplyToMotor();
        RefreshUI();
    }

    private void ApplyToMotor()
    {
        if (playerMotor != null) playerMotor.SetRunEnabled(runRequested);
    }

    private void RefreshUI()
    {
        wasLocked = IsLocked;
        if (button != null) button.interactable = !wasLocked;
        if (icon != null)
        {
            Sprite stateSprite = runRequested && !wasLocked ? onSprite : offSprite;
            if (stateSprite != null) icon.sprite = stateSprite;
        }
    }
}
