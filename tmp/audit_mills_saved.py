from pathlib import Path
import re

scene = Path('Assets/Scenes/CanyonCrossing.unity').read_text(encoding='utf-8-sig')
docs = {m[1]: m[2] for m in re.finditer(r'^--- !u!\d+ &(\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', scene, re.M | re.S)}
prefabs = [body for body in docs.values() if body.startswith('PrefabInstance:') and re.search(r'propertyPath: m_Name\s+value: Mills\s+objectReference:', body)]
assert len(prefabs) == 1, 'Expected exact Mills prefab instance'
body = prefabs[0]
props = {m[1].strip(): (m[2].strip(), m[3]) for m in re.finditer(r'propertyPath: ([^\n]+)\n      value:([^\n]*)\n      objectReference: (\{[^}]+\})', body)}

def scene_ref(key):
    assert key in props, key
    ref = props[key][1]
    assert 'guid:' not in ref, (key, ref)
    value = re.search(r'fileID: (\d+)', ref)[1]
    assert value != '0' and value in docs, (key, value)
    return value

phase_ids = ['MeetingMills', 'MillsExplainSituation', 'MillsFailureLesson', 'MillsProjectBriefing', 'MillsFailureReview', 'MillsHeavyEquipmentComplete']
count = int(props.get('phases.Array.size', ('6',))[0])
has_workers = count >= 11
has_final = count == 15
if has_workers:
    phase_ids += ['MillsWorkersExplain', 'MillsWorkersReview', 'MillsWorkersBriefing', 'MillsWorkersQuiz', 'MillsWorkersComplete']
if has_final:
    phase_ids += ['MillsFinalExplain', 'MillsFinalReview', 'MillsFinalBriefing', 'MillsResolved']
assert count == len(phase_ids)
for i, phase in enumerate(phase_ids):
    assert props[f'phases.Array.data[{i}].phaseId'][0] == phase
targets = [scene_ref(f'phases.Array.data[{i}].targetLocation') for i in range(len(phase_ids))]
assert targets[0] != targets[1] and len(set(targets[1:5])) == 1
if has_workers:
    assert len(set(targets[5:10 if has_final else 11])) == 1 and targets[5] != targets[1]
    assert props['phases.Array.data[5].teleportToTargetLocation'][0] == '1'
    assert '25c1c75517e6409eb94639ac144aee37' in props['phases.Array.data[8].contract'][1]
    scene_ref('phases.Array.data[8].targetBuildLocation')
    assert props['phases.Array.data[7].optionalDialogueSequence.Array.data[1].stepType'][0] == '5'
    assert props['phases.Array.data[9].optionalDialogueSequence.Array.data[0].stepType'][0] == '5'
    assert 'aeb132414cc142e6bba68927ecff0bc9' in body
if has_final:
    assert len(set(targets[10:])) == 1 and targets[10] not in targets[:10]
    assert props['phases.Array.data[10].teleportToTargetLocation'][0] == '1'
    assert 'df1ab9e026d64c41ad7e9c65f01ee3b9' in props['phases.Array.data[13].contract'][1]
    scene_ref('phases.Array.data[13].targetBuildLocation')
    assert props['phases.Array.data[13].runOptionalSequenceBeforeTurnIn'][0] == '1'
    assert props['phases.Array.data[13].optionalDialogueSequence.Array.data[1].stepType'][0] == '5'
    assert props['phases.Array.data[13].optionalDialogueSequence.Array.data[3].stepType'][0] == '2'
    assert '2de8e43ef0454b109b6c94206637f742' in props['phases.Array.data[13].optionalDialogueSequence.Array.data[3].quiz'][1]
    assert '9b56c36c93184dd3b04425d20acaa37b' in scene
    for i, expected in [(10, 'MillsFinalExplain'), (12, 'MillsFinalBriefing')]:
        scene_ref(f'phases.Array.data[{i}].onDialogueFinishedPhaseMove.npc')
        assert props[f'phases.Array.data[{i}].onDialogueFinishedPhaseMove.phaseId'][0] == expected
    scene_ref('phases.Array.data[11].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target')
    assert 'm_Name: Mills Final Temporary Vehicle' in scene and 'm_Name: workbenchMills3' in scene
    final_contract = Path('Assets/BridgeBuilder/Data/Contract/IndustrialZone/Mills Reconnecting Arcadia Contract.asset').read_text(encoding='utf-8-sig')
    for expected in ['contractID: MillsReconnectingArcadia', 'autoCollectReward: 0', 'bridgeSpan: 150', 'liveLoadWeight: 12000', 'twoWayRoadWidth: 16']:
        assert expected in final_contract, expected
assert scene_ref('authoredSpawnLocation') == targets[0]
assert props['progressionSaveId'][0] == 'Mills'
prefab = Path('Assets/Elements/Characters/NPCS/NPC_TEMPLATE.prefab').read_text(encoding='utf-8-sig')
for key in ['contractGiver', 'navMeshAgent']:
    if key in props:
        scene_ref(key)
    else:
        inherited = re.search(r'^  ' + key + r': \{fileID: (\d+)\}', prefab, re.M)
        assert inherited and inherited[1] != '0' and '&' + inherited[1] in prefab, key
for key in ['animator', 'dialogueManager', 'phases.Array.data[3].targetBuildLocation']:
    scene_ref(key)
for i in [0, 2, 4]:
    scene_ref(f'phases.Array.data[{i}].onDialogueFinishedPhaseMove.npc')
    assert props[f'phases.Array.data[{i}].onDialogueFinishedPhaseMove.phaseId'][0] == phase_ids[i + 1]
for key in ['phases.Array.data[0].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target', 'phases.Array.data[1].onNPCInteracted.m_PersistentCalls.m_Calls.Array.data[0].m_Target', 'phases.Array.data[1].onDialogueFinished.m_PersistentCalls.m_Calls.Array.data[0].m_Target']:
    scene_ref(key)
assert '51a23ae6a4b544e98c4d3d234401debe' in props['phases.Array.data[3].contract'][1]
assert '8ce1d3e6dab942a2973965e3af43d606' in body
assert '8a4b6404e91b4d35b1529416b2fc139e' in body
if not has_workers:
    assert props['m_IsActive'][0] == '0'
else:
    # Keep the author's current preview visibility; the saved spawn condition
    # still gates gameplay, and this task must not overwrite that preview edit.
    assert props['m_IsActive'][0] in ('0', '1')
assert 'm_Name: Mills Heavy Equipment Live Load' in scene
assert 'm_Name: workbenchMills1' in scene
assert 'm_Name: Clark Offset Road Story' in scene
print('PASS: saved Mills prefab has all canonical targets, spawn, actor bindings, phase calls, follow/cinematic callbacks, exact contracts and quizzes. Crane rig and separate workbenches are saved. Workers Crossing review-only steps and teleport verified; authored preview visibility preserved.' if has_workers else 'PASS: saved Mills first-story references.')
