using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Input-only feedback that survives UI scripts clearing their UnityEvent listeners.
/// Existing action sounds always take priority over the fallback click-pop.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIActionAudio : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerClickHandler, IUpdateSelectedHandler, ISubmitHandler, IMoveHandler
{
    private Selectable control;
    private float previousValue;
    private float pressedValue;
    private bool pressEligible;
    private bool releaseEligible;
    private int pointerId;
    private int selectedFrame = -1;
    private bool selectedEligible;
    private float selectedValue;
    private AudioManager beforeAction;
    private ulong beforeRevision;

    public static UIActionAudio EnsureFeedback(Selectable candidate, AlmanacManager almanac = null)
    {
        UIActionAudio feedback = candidate.GetComponent<UIActionAudio>();
        if (feedback == null && SupportsFallback(candidate, almanac))
            feedback = candidate.gameObject.AddComponent<UIActionAudio>();
        if (feedback != null) feedback.CaptureState();
        return feedback;
    }

    public static bool SupportsFallback(Selectable candidate, AlmanacManager almanac = null)
    {
        if (!(candidate is Button || candidate is Toggle || candidate is Slider ||
              candidate is Dropdown || candidate is TMP_Dropdown)) return false;
        // These play material/tool/jump sounds only when their action succeeds.
        if (candidate.GetComponent<MaterialButtonTrigger>() != null ||
            candidate.GetComponent<BuildModeIconFeedback>() != null) return false;
        if (almanac != null)
        {
            if (candidate == almanac.prevButton || candidate == almanac.nextButton) return false;
            if (almanac.categories != null)
                foreach (AlmanacCategory category in almanac.categories)
                    if (category != null && category.tabButton == candidate) return false;
        }
        if (candidate is Button button)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentListenerState(i) == UnityEngine.Events.UnityEventCallState.Off) continue;
                Object target = button.onClick.GetPersistentTarget(i);
                string method = button.onClick.GetPersistentMethodName(i);
                if (target is PlayerMotor && method == nameof(PlayerMotor.Jump)) return false;
                if (target is AlmanacManager && (method == nameof(AlmanacManager.OpenAlmanac) ||
                    method == nameof(AlmanacManager.CloseAlmanac))) return false;
            }
        }
        return true;
    }

    private void Awake() => CaptureState();
    public void CaptureState()
    {
        if (control == null) control = GetComponent<Selectable>();
        previousValue = Value;
    }

    private bool Usable => SceneInteractionAudio.IsSupportedScene && isActiveAndEnabled &&
        control != null && control.IsActive() && control.IsInteractable();
    private float Value => control is Toggle toggle ? (toggle.isOn ? 1f : 0f) :
        control is Slider slider ? slider.value : 0f;

    private void RecordAudioState()
    {
        beforeAction = AudioManager.Instance;
        beforeRevision = beforeAction != null ? beforeAction.SfxPlaybackRevision : 0;
    }

    private void PlayFallback()
    {
        AudioManager audio = AudioManager.Instance;
        if (!SceneInteractionAudio.IsSupportedScene || audio == null) return;
        if (audio == beforeAction && audio.SfxPlaybackRevision != beforeRevision) return;
        audio.PlaySFX("Interact", .72f);
    }

    public void OnPointerDown(PointerEventData data)
    {
        pressEligible = data.button == PointerEventData.InputButton.Left && Usable;
        releaseEligible = false;
        pointerId = data.pointerId;
        // Slider.OnPointerDown may already have updated its value. The prior
        // LateUpdate snapshot preserves the value before this user gesture.
        pressedValue = previousValue;
    }

    public void OnPointerUp(PointerEventData data)
    {
        releaseEligible = pressEligible && pointerId == data.pointerId &&
            data.button == PointerEventData.InputButton.Left && Usable;
        RecordAudioState();
        if (control is Slider && releaseEligible && !Mathf.Approximately(pressedValue, Value))
            PlayFallback(); // One preview on release, never a stream while dragging.
    }

    public void OnPointerClick(PointerEventData data)
    {
        if (!releaseEligible || pointerId != data.pointerId || data.dragging ||
            data.button != PointerEventData.InputButton.Left || control is Slider) return;
        releaseEligible = false;
        if (!(control is Toggle) || !Mathf.Approximately(pressedValue, Value)) PlayFallback();
    }

    // EventSystem sends this before keyboard/controller move and submit events,
    // while a valid button is still enabled (its action may close its own panel).
    public void OnUpdateSelected(BaseEventData data)
    {
        selectedFrame = Time.frameCount;
        selectedEligible = Usable;
        selectedValue = Value;
        RecordAudioState();
    }

    public void OnSubmit(BaseEventData data)
    {
        if (selectedFrame != Time.frameCount || !selectedEligible || control is Slider) return;
        selectedEligible = false;
        if (!(control is Toggle) || !Mathf.Approximately(selectedValue, Value)) PlayFallback();
    }

    public void OnMove(AxisEventData data)
    {
        if (selectedFrame == Time.frameCount && selectedEligible && control is Slider &&
            !Mathf.Approximately(selectedValue, Value)) PlayFallback();
    }
}
