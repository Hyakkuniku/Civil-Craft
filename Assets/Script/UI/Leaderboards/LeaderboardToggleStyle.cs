using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public sealed class LeaderboardToggleStyle : MonoBehaviour
{
    [SerializeField] private Image checkbox;
    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(Refresh);
        Refresh(toggle.isOn);
    }

    private void OnDestroy()
    {
        if (toggle != null) toggle.onValueChanged.RemoveListener(Refresh);
    }

    private void Refresh(bool selected)
    {
        if (checkbox != null)
            checkbox.color = selected
                ? new Color32(239, 166, 49, 255)
                : new Color32(255, 249, 238, 255);
    }
}
