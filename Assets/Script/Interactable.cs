using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class Interactable : MonoBehaviour
{

    // message that will be displayed on the player when the player is looking at an interactable
    public string promptMessage;

    //this finction will be called from out player
    public void BaseInteract()
    {
        Intract();
    }

    protected virtual void Intract()
    {
        // no code, this will be overridden by our subclasses
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
