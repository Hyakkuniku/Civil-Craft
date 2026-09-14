using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Keeps the original phase IDs and scene destinations, but turns Bhan's opening
// route into one continuous player-carried delivery to Grandma Susan.
[InitializeOnLoad]
public static class BhanCargoDeliveryStorySetup
{
    private const string BhanSaveId = "MainContractNPC";
    private const string ShopkeeperSaveId = "ShopKeeper";

    private static readonly Dictionary<string, string[]> BhanDialogue = new Dictionary<string, string[]>
    {
        ["Greetings"] = new[]
        {
            "There you are, {PlayerName}. I need an engineer, and Grandma Susan needs this delivery.",
            "Her shop is running low on essential tools and supplies. The canyon road collapsed before I could bring this crate through.",
            "We will rebuild the route one crossing at a time, and you will carry the same cargo safely to her shop.",
            "Come with me. Our delivery is waiting."
        },
        ["LiveLoad"] = new[]
        {
            "This is Grandma Susan's supply crate. From now on, it is the load every bridge must protect.",
            "A bridge is not finished because it looks complete. It is finished when its real cargo reaches the other side safely.",
            "Keep the crate with us. The first washed-out crossing is just ahead."
        },
        ["DeadLoad"] = new[]
        {
            "The cargo made it across, but notice what held it up before you even stepped onto the bridge.",
            "The bridge's own road, beams, and supports create dead load. Your body and the crate add a changing live load.",
            "A strong design must carry both. The next canyon will demand more than a flat road."
        },
        ["Beam"] = new[]
        {
            "The next span is wider. We will reinforce the road with a truss.",
            "Triangles direct forces through the structure and toward the supports. Watch the stress display while you test it.",
            "Grandma Susan is still waiting, so build for the cargo you actually have to carry."
        },
        ["City"] = new[]
        {
            "Canyonreach at last. You can almost see Grandma Susan's shop from here.",
            "The crate has crossed two bridges, but the final approach is still broken.",
            "Let us finish the connection and put these supplies where they belong."
        },
        ["Go to Shopkeeper"] = new[]
        {
            "You did it, {PlayerName}. Three crossings, one unbroken delivery.",
            "Grandma Susan is waiting beside the shop. Take a look at the route you restored, then go speak with her."
        }
    };

    private static readonly Dictionary<string, string[]> ShopkeeperDialogue = new Dictionary<string, string[]>
    {
        ["GiveShop"] = new[]
        {
            "That crate! I was beginning to think the canyon had swallowed it for good.",
            "Professor Bhan told me you rebuilt the whole route and carried these supplies across yourself.",
            "You did more than deliver a box, dear. You connected my shop to Canyonreach again."
        },
        ["MainCity"] = new[]
        {
            "People can reach the shop again, and I can finally send supplies back into town.",
            "Every bridge on that route tells the same story: you saw a problem, built a solution, and tested it with something that mattered.",
            "Thank you, Engineer."
        },
        ["GIVESGOP"] = new[]
        {
            "A delivery like that deserves more than a thank-you.",
            "My shop is open to you now. Spend your earnings on useful supplies and make the place feel like your own.",
            "Come back whenever the next job leaves dust on your boots."
        }
    };

    static BhanCargoDeliveryStorySetup()
    {
        EditorApplication.delayCall += ApplyToLoadedScene;
        EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += ApplyToLoadedScene;
    }

    [MenuItem("Tools/Civil Craft/Story/Apply Bhan Cargo Delivery Setup")]
    public static void ApplyToLoadedScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        NPCProgressionManager bhan = FindProgression(BhanSaveId);
        NPCProgressionManager shopkeeper = FindProgression(ShopkeeperSaveId);
        if (bhan == null || shopkeeper == null) return;
        var scene = bhan.gameObject.scene;
        // Unity can restore an unsaved CanyonCrossing edit session as a numbered
        // Temp/__Backupscenes scene. Identifying its two story progression IDs is
        // safer than rejecting that recovered scene by filename.
        if (!scene.IsValid() || !scene.isLoaded || shopkeeper.gameObject.scene != scene) return;

        bool changed = false;
        changed |= ApplyPhaseDialogue(bhan, BhanDialogue);
        changed |= ApplyPhaseDialogue(shopkeeper, ShopkeeperDialogue);
        changed |= EnableArrivalDialogue(bhan, "DeadLoad", "City", "Go to Shopkeeper");

        ContractSO shopContract = AssetDatabase.LoadAssetAtPath<ContractSO>(
            "Assets/BridgeBuilder/Data/Contract/ShopKeeperContract.asset");
        NPCContractGiver shopkeeperInteraction = shopkeeper.GetComponent<NPCContractGiver>();
        if (shopkeeperInteraction != null &&
            shopkeeperInteraction.requiredCargoDeliveryBeforeInteraction != shopContract)
        {
            Undo.RecordObject(shopkeeperInteraction, "Require Grandma Susan cargo delivery");
            shopkeeperInteraction.requiredCargoDeliveryBeforeInteraction = shopContract;
            EditorUtility.SetDirty(shopkeeperInteraction);
            changed = true;
        }

        NPCProgressionPhase firstContract = FindPhase(bhan, "Give Contract Build Location 1");
        CargoItem deliveryCargo = firstContract != null && firstContract.targetBuildLocation != null
            ? firstContract.targetBuildLocation.testCargo
            : null;
        if (deliveryCargo == null)
        {
            foreach (CargoItem candidate in Resources.FindObjectsOfTypeAll<CargoItem>())
            {
                if (!EditorUtility.IsPersistent(candidate) && candidate.gameObject.scene == scene)
                {
                    deliveryCargo = candidate;
                    break;
                }
            }
        }

        if (deliveryCargo != null)
        {
            if (!deliveryCargo.storyCargo)
            {
                Undo.RecordObject(deliveryCargo, "Mark Bhan delivery as story cargo");
                deliveryCargo.storyCargo = true;
                EditorUtility.SetDirty(deliveryCargo);
                changed = true;
            }
            changed |= LinkCargo(bhan, deliveryCargo, "Give Contract Build Location 1", "Build Tutorial 2", "Shopkeeper");
            changed |= CreateCargoDropLocations(scene, deliveryCargo);
        }
        else
        {
            Debug.LogWarning("[Bhan Delivery Story] No scene CargoItem was found. Assign the delivery crate to Bhan's first contract Build Location, then run the setup menu again.", bhan);
        }

        // Bhan no longer teaches vehicle inspection. Leave the vehicle available for
        // a later NPC by removing the old tutorial-contract binding.
        ContractSO first = AssetDatabase.LoadAssetAtPath<ContractSO>("Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 1.asset");
        foreach (LiveLoadVehicle vehicle in Resources.FindObjectsOfTypeAll<LiveLoadVehicle>())
        {
            if (vehicle.gameObject.scene != scene || vehicle.assignedContract != first) continue;
            Undo.RecordObject(vehicle, "Disconnect Bhan live-load inspection");
            vehicle.assignedContract = null;
            EditorUtility.SetDirty(vehicle);
            changed = true;
        }

        if (!changed) return;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Bhan Delivery Story] Applied the cargo-delivery dialogue and trigger setup. Save CanyonCrossing to keep the scene changes.", bhan);
    }

    private static NPCProgressionManager FindProgression(string saveId)
    {
        foreach (NPCProgressionManager npc in Resources.FindObjectsOfTypeAll<NPCProgressionManager>())
        {
            if (EditorUtility.IsPersistent(npc) || !npc.gameObject.scene.IsValid()) continue;
            SerializedProperty id = new SerializedObject(npc).FindProperty("progressionSaveId");
            if (id != null && id.stringValue == saveId) return npc;
        }
        return null;
    }

    private static NPCProgressionPhase FindPhase(NPCProgressionManager npc, string phaseId)
    {
        SerializedProperty phases = new SerializedObject(npc).FindProperty("phases");
        if (phases == null) return null;
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            if (phase.FindPropertyRelative("phaseId").stringValue != phaseId) continue;
            // Runtime object access is needed for component references; match it in the
            // manager's serialized list through reflection-free SerializedProperty data below.
            var target = phase.FindPropertyRelative("targetBuildLocation").objectReferenceValue as BuildLocation;
            var contract = phase.FindPropertyRelative("contract").objectReferenceValue as ContractSO;
            return new NPCProgressionPhase { phaseId = phaseId, targetBuildLocation = target, contract = contract };
        }
        return null;
    }

    private static bool ApplyPhaseDialogue(NPCProgressionManager npc, Dictionary<string, string[]> replacements)
    {
        SerializedObject data = new SerializedObject(npc);
        SerializedProperty phases = data.FindProperty("phases");
        bool changed = false;
        string speaker = GetProgressionId(npc) == ShopkeeperSaveId ? "Grandma Susan" : "Professor Bhan";
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            string phaseId = phase.FindPropertyRelative("phaseId").stringValue;
            if (!replacements.TryGetValue(phaseId, out string[] lines)) continue;
            changed |= SetDialogue(phase.FindPropertyRelative("phaseDialogue"), speaker, lines);
        }
        if (!changed) return false;
        Undo.RecordObject(npc, "Rewrite cargo delivery story");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(npc);
        return true;
    }

    private static string GetProgressionId(NPCProgressionManager npc)
    {
        SerializedProperty id = new SerializedObject(npc).FindProperty("progressionSaveId");
        return id != null ? id.stringValue : string.Empty;
    }

    private static bool LinkCargo(NPCProgressionManager npc, CargoItem cargo, params string[] phaseIds)
    {
        var wanted = new HashSet<string>(phaseIds, StringComparer.Ordinal);
        SerializedObject data = new SerializedObject(npc);
        SerializedProperty phases = data.FindProperty("phases");
        bool changed = false;
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            if (!wanted.Contains(phase.FindPropertyRelative("phaseId").stringValue)) continue;
            SerializedProperty cargoProperty = phase.FindPropertyRelative("linkedCargo");
            SerializedProperty siteProperty = phase.FindPropertyRelative("targetBuildLocation");
            if (cargoProperty.objectReferenceValue != cargo)
            {
                cargoProperty.objectReferenceValue = cargo;
                changed = true;
            }
            if (siteProperty.objectReferenceValue is BuildLocation site && site.testCargo != cargo)
            {
                Undo.RecordObject(site, "Assign story delivery cargo");
                site.testCargo = cargo;
                EditorUtility.SetDirty(site);
                changed = true;
            }
        }
        if (!changed) return false;
        Undo.RecordObject(npc, "Link story delivery cargo");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(npc);
        return true;
    }

    private static bool EnableArrivalDialogue(NPCProgressionManager npc, params string[] phaseIds)
    {
        var wanted = new HashSet<string>(phaseIds, StringComparer.Ordinal);
        SerializedObject data = new SerializedObject(npc);
        SerializedProperty phases = data.FindProperty("phases");
        bool changed = false;
        for (int i = 0; i < phases.arraySize; i++)
        {
            SerializedProperty phase = phases.GetArrayElementAtIndex(i);
            if (!wanted.Contains(phase.FindPropertyRelative("phaseId").stringValue)) continue;
            SerializedProperty playOnArrival = phase.FindPropertyRelative("playDialogueOnArrival");
            if (playOnArrival.boolValue) continue;
            playOnArrival.boolValue = true;
            changed = true;
        }
        if (!changed) return false;
        Undo.RecordObject(npc, "Enable cargo destination dialogue");
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(npc);
        return true;
    }

    private static bool CreateCargoDropLocations(UnityEngine.SceneManagement.Scene scene, CargoItem cargo)
    {
        var contracts = new HashSet<ContractSO>
        {
            AssetDatabase.LoadAssetAtPath<ContractSO>("Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 1.asset"),
            AssetDatabase.LoadAssetAtPath<ContractSO>("Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 2.asset"),
            AssetDatabase.LoadAssetAtPath<ContractSO>("Assets/BridgeBuilder/Data/Contract/ShopKeeperContract.asset")
        };
        bool changed = false;
        foreach (FinishLineTrigger finish in Resources.FindObjectsOfTypeAll<FinishLineTrigger>())
        {
            if (finish.gameObject.scene != scene || !contracts.Contains(finish.assignedContract)) continue;
            CargoDropLocation drop = finish.GetComponent<CargoDropLocation>();
            if (drop == null)
            {
                drop = Undo.AddComponent<CargoDropLocation>(finish.gameObject);
                changed = true;
            }
            Undo.RecordObject(drop, "Configure cargo delivery point");
            if (drop.assignedContract != finish.assignedContract)
            {
                drop.assignedContract = finish.assignedContract;
                changed = true;
            }
            if (drop.dropSocket == null || drop.dropSocket == drop.transform)
            {
                Transform socket = finish.transform.Find("Cargo Drop Socket");
                if (socket == null)
                {
                    GameObject socketObject = new GameObject("Cargo Drop Socket");
                    Undo.RegisterCreatedObjectUndo(socketObject, "Create cargo delivery socket");
                    socket = socketObject.transform;
                    socket.SetParent(finish.transform, false);
                }
                PositionSocketAtZoneFloor(socket, finish.GetComponent<BoxCollider>(), cargo);
                drop.dropSocket = socket;
                changed = true;
            }
            SerializedObject dropData = new SerializedObject(drop);
            SerializedProperty id = dropData.FindProperty("persistentDropLocationId");
            if (id != null && string.IsNullOrWhiteSpace(id.stringValue))
            {
                id.stringValue = Guid.NewGuid().ToString("N");
                dropData.ApplyModifiedProperties();
                changed = true;
            }
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer >= 0 && finish.gameObject.layer != interactableLayer)
            {
                Undo.RecordObject(finish.gameObject, "Set cargo delivery layer");
                finish.gameObject.layer = interactableLayer;
                changed = true;
            }
            EditorUtility.SetDirty(drop);
        }
        return changed;
    }

    private static void PositionSocketAtZoneFloor(Transform socket, BoxCollider zone, CargoItem cargo)
    {
        if (zone == null) return;
        Vector3 floor = zone.transform.TransformPoint(zone.center - Vector3.up * zone.size.y * 0.5f);
        float pivotToBottom = 0f;
        Renderer[] renderers = cargo.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            pivotToBottom = cargo.transform.position.y - bounds.min.y;
        }
        socket.position = floor + Vector3.up * pivotToBottom;
        socket.rotation = Quaternion.Euler(0f, zone.transform.eulerAngles.y, 0f);
    }

    private static bool SetDialogue(SerializedProperty dialogue, string speaker, string[] lines)
    {
        if (dialogue == null) return false;
        SerializedProperty name = dialogue.FindPropertyRelative("name");
        SerializedProperty sentences = dialogue.FindPropertyRelative("sentences");
        bool different = name.stringValue != speaker || sentences.arraySize != lines.Length;
        if (!different)
        {
            for (int i = 0; i < lines.Length; i++)
                if (sentences.GetArrayElementAtIndex(i).stringValue != lines[i]) { different = true; break; }
        }
        if (!different) return false;
        name.stringValue = speaker;
        sentences.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++) sentences.GetArrayElementAtIndex(i).stringValue = lines[i];
        return true;
    }
}
