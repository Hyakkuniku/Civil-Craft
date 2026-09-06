using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public bool useEvents;


    // message that will be displayed on the player when the player is looking at an interactable

    [SerializeField]
    public string promptMessage;

    /// <summary>
    /// Allows an interactable to disappear from the generated mobile interaction
    /// buttons without disabling the component and its unrelated update logic.
    /// </summary>
    public virtual bool IsInteractionAvailable => isActiveAndEnabled;

    //this finction will be called from out player
    public void BaseInteract()
    {
        if (!IsInteractionAvailable) return;

        if (useEvents)
        {
            GetComponent<InteractionEvent>().OnInteract.Invoke();
        }
        Intract();
    }

    protected virtual void Intract()
    {
        // no code, this will be overridden by our subclasses
    }

}
