using UnityEditor;

[CustomEditor(typeof(Interactable), true)]
public class InteractableEditor : Editor
{
    public override void OnInspectorGUI()
    {
        Interactable interactable = (Interactable)target;

        if (target.GetType() == typeof(EventOnlyInteractable))
        {
            interactable.promptMessage =
                EditorGUILayout.TextField("Prompt Message", interactable.promptMessage);

            EditorGUILayout.HelpBox(
                "EventOnlyInteractable can ONLY use UnityEvents.",
                MessageType.Info
            );

            interactable.useEvents = true;

            if (interactable.GetComponent<InteractionEvent>() == null)
            {
                interactable.gameObject.AddComponent<InteractionEvent>();
            }
        }
        else
        {
            base.OnInspectorGUI();

            if (interactable.useEvents)
            {
                if (interactable.GetComponent<InteractionEvent>() == null)
                    interactable.gameObject.AddComponent<InteractionEvent>();
            }
            else
            {
                InteractionEvent evt = interactable.GetComponent<InteractionEvent>();
                if (evt != null)
                    DestroyImmediate(evt);
            }
        }
    }
}
