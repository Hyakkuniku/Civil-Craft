using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

// Restores the story captured in Docs/StoryDialogue_Bhan_to_Shopkeeper.md.
// The historical class/file name remains so the newer cargo rewrite cannot be
// reintroduced by its former automatic editor hook.
public static class BhanCargoDeliveryStorySetup
{
    private const string BhanId = "MainContractNPC";
    private const string ShopkeeperId = "ShopKeeper";

    private static readonly Dictionary<string, string[]> BhanLines = new Dictionary<string, string[]>
    {
        ["Greetings"] = new[]
        {
            "There you are, {PlayerName}",
            "Are you ready to learn the basics?",
            "Before you start building, I want you to understand what will cross your bridge.",
            "Follow me to the supply cart."
        },
        ["LiveLoad"] = new[] { "Inspect the supply cart." },
        ["DeadLoad"] = new[]
        {
            "Good work, {PlayerName}.",
            "Your bridge is standing, and it can carry the required live load.",
            "But remember, live load isn't the only load a bridge has to carry.",
            "Even without a vehicle or a person crossing it, the bridge itself still has weight.",
            "That weight is called dead load."
        },
        ["City"] = new[]
        {
            "Ahead of is Canyon Ville",
            "But before i let you explore the city",
            "The shop owner contacted us and needed our help.",
            "Follow me."
        },
        ["Go to Shopkeeper"] = new[] { "Go to the said Shopkeeper" }
    };

    private static readonly Dictionary<string, string[]> ShopkeeperLines = new Dictionary<string, string[]>
    {
        ["GiveShop"] = new[] { "Ah, there you are!", "I can see the bridge is finally complete." },
        ["MainCity"] = new[]
        {
            "Now the shop is connected directly to the Main City.",
            "People can finally reach us without having to take the long way around.",
            "Thank you, Engineer.",
            "You've done exactly what I needed."
        },
        ["GIVESGOP"] = new[]
        {
            "Since you're going to be working on more projects around Canyon Crossing, there's something else you should know.",
            "The work you complete will earn you Gold and other rewards.",
            "Those rewards can be used here at the Shop.",
            "You can use what you've earned to purchase items for your engineering journey.",
            "Some items can also be equipped to customize your appearance.",
            "You can come back anytime.",
            "Good luck with your next project, Engineer."
        }
    };

    [MenuItem("Tools/Civil Craft/Story/Restore Original Bhan to Shopkeeper Story")]
    public static void ApplyFromMenu() => ApplyOriginalStory();

    // Entry point for unattended verification/migration when the Editor is not open.
    public static void ApplyBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CanyonCrossing.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
        if (!ApplyOriginalStory())
            throw new InvalidOperationException("Could not locate Bhan and Grandma Susan in CanyonCrossing.");
    }

    private static bool ApplyOriginalStory()
    {
        NPCProgressionManager bhan = FindNpc(BhanId);
        NPCProgressionManager shopkeeper = FindNpc(ShopkeeperId);
        if (bhan == null || shopkeeper == null || bhan.gameObject.scene != shopkeeper.gameObject.scene)
            return false;

        var scene = bhan.gameObject.scene;
        bool changed = ApplyDialogue(bhan, BhanLines, "Professor Bhan");
        changed |= ApplyDialogue(shopkeeper, ShopkeeperLines, "Grandma Susan");
        changed |= ClearPhaseDialogue(bhan, "Beam", "Shopkeeper");
        changed |= RestorePhaseSequence(bhan);

        ContractSO first = LoadContract("Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 1.asset");
        ContractSO second = LoadContract("Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 2.asset");
        ContractSO shop = LoadContract("Assets/BridgeBuilder/Data/Contract/ShopKeeperContract.asset");
        changed |= RestoreFirstContract(first);
        changed |= RestoreSecondContract(second);
        changed |= RestoreShopContract(shop);
        changed |= RemoveCargoRewrite(scene, bhan, first, second, shop);
        changed |= AssignVehicles(scene, bhan, first, second, shop);

        NPCContractGiver grandmaGate = shopkeeper.GetComponent<NPCContractGiver>();
        if (grandmaGate != null && grandmaGate.requiredCargoDeliveryBeforeInteraction != shop)
        {
            Undo.RecordObject(grandmaGate, "Restore Grandma Susan completion gate");
            grandmaGate.requiredCargoDeliveryBeforeInteraction = shop;
            EditorUtility.SetDirty(grandmaGate);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Bhan Original Story] Restored and saved StoryDialogue_Bhan_to_Shopkeeper dialogue and sequences.", bhan);
        }
        return true;
    }

    private static bool RestorePhaseSequence(NPCProgressionManager bhan)
    {
        SerializedObject data = new SerializedObject(bhan);
        SerializedProperty liveLoad = FindPhase(data, "LiveLoad");
        bool changed = false;
        if (liveLoad != null)
        {
            Transform destination = FindTransform(bhan.gameObject.scene, "Bhan_LiveLoad_Destination");
            SerializedProperty target = liveLoad.FindPropertyRelative("targetLocation");
            if (destination != null && target.objectReferenceValue != destination)
            {
                target.objectReferenceValue = destination;
                changed = true;
            }
            changed |= SetBool(liveLoad, "playDialogueOnArrival", true);
            changed |= SetBool(liveLoad, "repeatDialogue", false);
            SerializedProperty move = liveLoad.FindPropertyRelative("onDialogueFinishedPhaseMove");
            if (move != null)
            {
                SerializedProperty npc = move.FindPropertyRelative("npc");
                SerializedProperty id = move.FindPropertyRelative("phaseId");
                if (npc.objectReferenceValue != null || !string.IsNullOrEmpty(id.stringValue))
                {
                    npc.objectReferenceValue = null;
                    id.stringValue = string.Empty;
                    changed = true;
                }
            }
        }
        foreach (string id in new[] { "DeadLoad", "City", "Go to Shopkeeper" })
        {
            SerializedProperty phase = FindPhase(data, id);
            if (phase != null) changed |= SetBool(phase, "playDialogueOnArrival", false);
        }
        if (!changed) return false;
        Undo.RecordObject(bhan, "Restore original Bhan phase sequence");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(bhan);
        return true;
    }

    private static bool AssignVehicles(UnityEngine.SceneManagement.Scene scene,
        NPCProgressionManager bhan, params ContractSO[] contracts)
    {
        List<NPCProgressionPhase> phases = GetPhases(bhan);
        List<LiveLoadVehicle> available = Resources.FindObjectsOfTypeAll<LiveLoadVehicle>()
            .Where(v => !EditorUtility.IsPersistent(v) && v.gameObject.scene == scene &&
                        v.startPoint != null && v.endPoint != null)
            .ToList();
        HashSet<LiveLoadVehicle> used = new HashSet<LiveLoadVehicle>();
        bool changed = false;

        foreach (ContractSO contract in contracts)
        {
            NPCProgressionPhase phase = phases.FirstOrDefault(p => p != null && p.contract == contract);
            if (phase == null || phase.targetBuildLocation == null) continue;
            LiveLoadVehicle vehicle = available
                .Where(v => !used.Contains(v) && (v.assignedContract == null || contracts.Contains(v.assignedContract)))
                // Tutorial 1 must use a vehicle that can display the inspection
                // lesson; distance remains the tie-breaker for all vehicles.
                .OrderBy(v => contract == contracts[0] && v.GetComponent<LessonTrigger>() == null ? 1 : 0)
                .ThenBy(v => Vector3.Distance(v.startPoint.position, phase.targetBuildLocation.transform.position))
                .FirstOrDefault();
            if (vehicle == null) continue;
            used.Add(vehicle);
            if (vehicle.assignedContract != contract)
            {
                Undo.RecordObject(vehicle, "Restore story live load assignment");
                vehicle.assignedContract = contract;
                EditorUtility.SetDirty(vehicle);
                changed = true;
            }
            if (contract == contracts[0]) changed |= ConfigureInspection(vehicle, bhan);
        }
        return changed;
    }

    private static bool ConfigureInspection(LiveLoadVehicle vehicle, NPCProgressionManager bhan)
    {
        LessonTrigger lesson = vehicle.GetComponent<LessonTrigger>();
        if (lesson == null) return false;
        SerializedObject lessonData = new SerializedObject(lesson);
        bool changed = SetObject(lessonData, "advanceNPC", bhan);
        changed |= SetInt(lessonData, "requiredNPCPhaseIndex", PhaseIndex(bhan, "LiveLoad"));
        changed |= SetInt(lessonData, "nextNPCPhaseIndex", PhaseIndex(bhan, "WorkBench"));
        changed |= SetString(lessonData, "completionSaveKey", "Bhan_LiveLoad_Inspection");
        if (changed)
        {
            Undo.RecordObject(lesson, "Restore supply-cart inspection progression");
            lessonData.ApplyModifiedProperties();
            EditorUtility.SetDirty(lesson);
        }

        NPCProgressionPhase phase = GetPhases(bhan).FirstOrDefault(p => p != null && p.phaseId == "LiveLoad");
        if (phase != null && phase.onDialogueFinished != null &&
            !HasListener(phase.onDialogueFinished, lesson, nameof(LessonTrigger.ArmNPCAdvance)))
        {
            Undo.RecordObject(bhan, "Arm supply-cart lesson after Bhan prompt");
            UnityEventTools.AddPersistentListener(phase.onDialogueFinished, lesson.ArmNPCAdvance);
            EditorUtility.SetDirty(bhan);
            changed = true;
        }
        return changed;
    }

    private static bool RemoveCargoRewrite(UnityEngine.SceneManagement.Scene scene,
        NPCProgressionManager bhan, params ContractSO[] contracts)
    {
        HashSet<ContractSO> set = new HashSet<ContractSO>(contracts);
        SerializedObject data = new SerializedObject(bhan);
        SerializedProperty phases = data.FindProperty("phases");
        bool changed = false;
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            ContractSO contract = phase.FindPropertyRelative("contract").objectReferenceValue as ContractSO;
            if (!set.Contains(contract)) continue;
            SerializedProperty cargo = phase.FindPropertyRelative("linkedCargo");
            if (cargo.objectReferenceValue != null) { cargo.objectReferenceValue = null; changed = true; }
            BuildLocation site = phase.FindPropertyRelative("targetBuildLocation").objectReferenceValue as BuildLocation;
            if (site != null && site.testCargo != null)
            {
                Undo.RecordObject(site, "Remove replaced story cargo");
                site.testCargo = null;
                EditorUtility.SetDirty(site);
                changed = true;
            }
        }
        if (changed)
        {
            Undo.RecordObject(bhan, "Remove cargo links from original story");
            data.ApplyModifiedProperties();
            EditorUtility.SetDirty(bhan);
        }

        foreach (CargoItem cargo in Resources.FindObjectsOfTypeAll<CargoItem>())
        {
            if (EditorUtility.IsPersistent(cargo) || cargo.gameObject.scene != scene || !cargo.storyCargo) continue;
            Undo.RecordObject(cargo, "Disable replaced story cargo");
            cargo.storyCargo = false;
            cargo.playerCargoContract = null;
            EditorUtility.SetDirty(cargo);
            changed = true;
        }
        foreach (CargoDropLocation drop in Resources.FindObjectsOfTypeAll<CargoDropLocation>())
        {
            if (!EditorUtility.IsPersistent(drop) && drop.gameObject.scene == scene && set.Contains(drop.assignedContract))
            {
                Undo.DestroyObjectImmediate(drop);
                changed = true;
            }
        }
        return changed;
    }

    private static bool RestoreFirstContract(ContractSO c) => SetContract(c,
        "Build the first crossing and make sure it can safely carry the cart.",
        new[]
        {
            "Now you know what the project requires, what you'll be carrying across the bridge, and which materials are available to you.",
            "You've also learned the basic tools you'll need to construct and test your bridge.",
            "I think you're ready for your first contract."
        },
        new[]
        {
            "Your first contract will be a simple bridge construction project.",
            "The crossing ahead needs a safe and reliable connection between both sides of the canyon.",
            "For this project, you'll be constructing a Beam Bridge.",
            "A beam bridge is a simple structure that uses horizontal members to carry the load toward its supports.",
            "Your job is to construct the bridge and make sure it can safely make the cart.",
            "Review the contract carefully, then decide how you'll approach the construction."
        },
        new[] { "You can now go to the construction site whenever you are ready..", "Enter the Blueprint and begin construction." },
        new[]
        {
            "Good work, {PlayerName}.", "Your first training bridge has passed its test.",
            "You now know how to accept a contract, use the blueprint, build a bridge, and test your design.",
            "But your work is not finished yet.", "an engineer should inspect the structure after construction.",
            "Open your Almanac and review your first entry"
        });

    private static bool RestoreSecondContract(ContractSO c) => SetContract(c,
        "Construct a truss bridge that safely carries several pedestrians.",
        new[] { "Enter the Build location so we can start" },
        new[]
        {
            "The bridge must safely carry several pedestrians crossing one after another.",
            "This time, the road alone will not be enough.",
            "You'll reinforce it using the Wood Beams you just learned about.",
            "When a truss carries a load, its members can experience different types of forces.",
            "Some members are pulled apart. This is called tension.", "Other members are pushed together. This is called compression.",
            "These forces act within the truss as the members work together to support the load.",
            "To keep the structure stable, the forces acting on it must remain balanced.",
            "This condition is called equilibrium.",
            "Pay attention to how the members respond when the pedestrians cross the bridge.",
            "Now that you've accepted the contract, follow the path to the Blueprint."
        },
        new[] { "What are you waiting for?" },
        new[]
        {
            "Excellent work, Engineer.", "You've now built your first truss bridge.",
            "You also learned how to select, copy, and paste structural components.",
            "Those tools will become useful when your designs become more complicated.",
            "But before you move on, there's one more building tool I want you to learn."
        });

    private static bool RestoreShopContract(ContractSO c) => SetContract(c,
        "Build a safe bridge connecting the shop to Canyonreach.",
        new[]
        {
            "A local shopkeeper has asked for our help.",
            "Her shop is separated from Canyonreach, making it difficult for her to transport supplies and reach customers in the city.",
            "She needs a bridge that will safely connect her shop to Canyonreach.",
            "The shopkeeper will meet us once the bridge is complete.",
            "Are you ready to take on this contract, Engineer?"
        },
        new[] { "The shopkeeper is depending on us to reconnect her shop with Canyonreach.", "Are you ready to accept the contract?" },
        new[] { "The bridge connecting the shop to Canyonreach still needs to be completed.", "Review the contract and begin construction when you are ready." },
        new[]
        {
            "Well done, Engineer. The bridge is complete.",
            "The shopkeeper can now safely transport her supplies and welcome customers from Canyonreach.",
            "She has arrived to see your work.", "Go and speak with her. She will give you access to the shop."
        });

    private static bool SetContract(ContractSO c, string description, string[] offer,
        string[] continuation, string[] reminder, string[] finished)
    {
        if (c == null) return false;
        bool changed = c.liveLoadMode != ContractSO.LiveLoadMode.Vehicle || c.allowVehicleCargo ||
                       c.minimumLoadedCargo != 0 || c.jobDescription != description ||
                       !Matches(c.offerDialogue, offer) || !Matches(c.continueOfferDialogue, continuation) ||
                       !Matches(c.reminderDialogue, reminder) || !Matches(c.finishedContractDialogue, finished);
        if (!changed) return false;
        Undo.RecordObject(c, "Restore original story contract");
        c.liveLoadMode = ContractSO.LiveLoadMode.Vehicle;
        c.allowVehicleCargo = false;
        c.minimumLoadedCargo = 0;
        c.jobDescription = description;
        Write(c.offerDialogue, offer);
        Write(c.continueOfferDialogue, continuation);
        Write(c.reminderDialogue, reminder);
        Write(c.finishedContractDialogue, finished);
        EditorUtility.SetDirty(c);
        return true;
    }

    private static bool ApplyDialogue(NPCProgressionManager npc, Dictionary<string, string[]> lines, string speaker)
    {
        SerializedObject data = new SerializedObject(npc);
        bool changed = false;
        foreach (var entry in lines)
        {
            SerializedProperty phase = FindPhase(data, entry.Key);
            if (phase != null) changed |= SetSerializedDialogue(phase.FindPropertyRelative("phaseDialogue"), speaker, entry.Value);
        }
        if (!changed) return false;
        Undo.RecordObject(npc, "Restore original story dialogue");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(npc);
        return true;
    }

    private static bool ClearPhaseDialogue(NPCProgressionManager npc, params string[] ids)
    {
        SerializedObject data = new SerializedObject(npc);
        bool changed = false;
        foreach (string id in ids)
        {
            SerializedProperty phase = FindPhase(data, id);
            if (phase != null) changed |= SetSerializedDialogue(phase.FindPropertyRelative("phaseDialogue"), string.Empty, Array.Empty<string>());
        }
        if (!changed) return false;
        Undo.RecordObject(npc, "Restore contract dialogue sequence");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(npc);
        return true;
    }

    private static bool SetSerializedDialogue(SerializedProperty dialogue, string speaker, string[] lines)
    {
        SerializedProperty name = dialogue.FindPropertyRelative("name");
        SerializedProperty sentences = dialogue.FindPropertyRelative("sentences");
        bool changed = name.stringValue != speaker || sentences.arraySize != lines.Length;
        if (!changed)
            for (int i = 0; i < lines.Length; i++)
                if (sentences.GetArrayElementAtIndex(i).stringValue != lines[i]) { changed = true; break; }
        if (!changed) return false;
        name.stringValue = speaker;
        sentences.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++) sentences.GetArrayElementAtIndex(i).stringValue = lines[i];
        return true;
    }

    private static NPCProgressionManager FindNpc(string id) =>
        Resources.FindObjectsOfTypeAll<NPCProgressionManager>().FirstOrDefault(n =>
            !EditorUtility.IsPersistent(n) && n.gameObject.scene.IsValid() && ProgressionId(n) == id);

    private static string ProgressionId(NPCProgressionManager npc)
    {
        SerializedProperty id = new SerializedObject(npc).FindProperty("progressionSaveId");
        return id != null ? id.stringValue : string.Empty;
    }

    private static List<NPCProgressionPhase> GetPhases(NPCProgressionManager npc)
    {
        FieldInfo field = typeof(NPCProgressionManager).GetField("phases", BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? field.GetValue(npc) as List<NPCProgressionPhase> : new List<NPCProgressionPhase>();
    }

    private static int PhaseIndex(NPCProgressionManager npc, string id)
    {
        List<NPCProgressionPhase> phases = GetPhases(npc);
        for (int i = 0; i < phases.Count; i++) if (phases[i] != null && phases[i].phaseId == id) return i;
        return -1;
    }

    private static SerializedProperty FindPhase(SerializedObject data, string id)
    {
        SerializedProperty phases = data.FindProperty("phases");
        if (phases == null) return null;
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            if (phase.FindPropertyRelative("phaseId").stringValue == id) return phase;
        }
        return null;
    }

    private static Transform FindTransform(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (found != null) return found;
        }
        return null;
    }

    private static bool HasListener(UnityEvent e, UnityEngine.Object target, string method)
    {
        for (int i = 0; i < e.GetPersistentEventCount(); i++)
            if (e.GetPersistentTarget(i) == target && e.GetPersistentMethodName(i) == method) return true;
        return false;
    }

    private static bool Matches(Dialogue d, string[] lines) => d != null && d.sentences != null && d.sentences.SequenceEqual(lines);
    private static void Write(Dialogue d, string[] lines) { d.name = "Professor Bhan"; d.sentences = lines; }
    private static ContractSO LoadContract(string path) => AssetDatabase.LoadAssetAtPath<ContractSO>(path);

    private static bool SetBool(SerializedProperty parent, string name, bool value)
    {
        SerializedProperty p = parent.FindPropertyRelative(name);
        if (p == null || p.boolValue == value) return false;
        p.boolValue = value; return true;
    }
    private static bool SetObject(SerializedObject data, string name, UnityEngine.Object value)
    {
        SerializedProperty p = data.FindProperty(name);
        if (p == null || p.objectReferenceValue == value) return false;
        p.objectReferenceValue = value; return true;
    }
    private static bool SetInt(SerializedObject data, string name, int value)
    {
        SerializedProperty p = data.FindProperty(name);
        if (p == null || p.intValue == value) return false;
        p.intValue = value; return true;
    }
    private static bool SetString(SerializedObject data, string name, string value)
    {
        SerializedProperty p = data.FindProperty(name);
        if (p == null || p.stringValue == value) return false;
        p.stringValue = value; return true;
    }
}
