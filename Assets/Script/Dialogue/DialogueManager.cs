using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueManager : MonoBehaviour
{

    public TextMeshProUGUI nameText;
    public TextMeshProUGUI dialogueText;


    public Animator animator;


    private Queue<string> sentences;

    [SerializeField] private InputManager inputManager;
    



    // Start is called before the first frame update
    void Start()
    {
        sentences = new Queue<string>();

    }

    public void StartDialogue (Dialogue dialogue)
    {
        inputManager?.SetPlayerInputEnable(false);
        inputManager?.SetLookEnabled(false);

        animator.SetBool("isOpen", true);

        Debug.Log("Starting convesation with " + dialogue.name);

        nameText.text = dialogue.name;

        sentences.Clear();

        foreach (string sentence in dialogue.sentences)
        {
            sentences.Enqueue(sentence);
        }

        DisplayNextSentence();
    }


    public void DisplayNextSentence ()
    {
        if (sentences.Count == 0)
        {
            EndDialogue();
            return;
        }

        string sentence = sentences.Dequeue();
        dialogueText.text = sentence;
    }

    void EndDialogue()
    {
        inputManager?.SetPlayerInputEnable(true);
        inputManager?.SetLookEnabled(true);
        animator.SetBool("isOpen", false);
        Debug.Log("End of conversation");
    }
  
}