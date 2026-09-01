using UnityEngine;
using UnityEngine.UI;

public class SettingsTabController : MonoBehaviour
{
    [Header("Tab Setup")]
    [Tooltip("Drag your content panels here (GraphicsPanel, AudioPanel, etc.)")]
    public GameObject[] tabPanels;
    
    [Tooltip("Drag the corresponding buttons here in the exact same order.")]
    public Button[] tabButtons;

    [Header("Visual Feedback")]
    public Color activeTabColor = Color.white;
    public Color inactiveTabColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private void Start()
    {
        // Automatically hook up all the buttons so you don't have to do it in the Inspector
        int buttonCount = tabButtons != null ? tabButtons.Length : 0;
        for (int i = 0; i < buttonCount; i++)
        {
            int index = i; // Required for the closure in the listener
            if (tabButtons[i] != null)
                tabButtons[i].onClick.AddListener(() => SwitchTab(index));
        }

        // Open the first tab by default
        if (tabPanels != null && tabPanels.Length > 0)
        {
            SwitchTab(0);
        }
    }

    public void SwitchTab(int tabIndex)
    {
        if (tabPanels == null || tabIndex < 0 || tabIndex >= tabPanels.Length) return;

        for (int i = 0; i < tabPanels.Length; i++)
        {
            bool isActive = (i == tabIndex);
            
            // Turn the panel on or off
            if (tabPanels[i] != null) 
                tabPanels[i].SetActive(isActive);
            
            // Highlight the active button
            if (tabButtons != null && i < tabButtons.Length && tabButtons[i] != null)
            {
                Image btnImage = tabButtons[i].GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.color = isActive ? activeTabColor : inactiveTabColor;
                }
            }
        }
    }
}
