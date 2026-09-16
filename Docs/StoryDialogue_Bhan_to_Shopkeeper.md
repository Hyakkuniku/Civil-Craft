# Full in-game story dialogue — Professor Bhan to Grandma Susan and the Reed handoff

Updated 16 September 2026 from the saved `Assets/Scenes/CanyonCrossing.unity` scene and its three opening contract assets.

This is the opening-story transcript, through the Shop unlock, map introduction, and handoff to Reed. Authored wording, including unfinished lines, is preserved. TextMesh Pro formatting tags and trailing blank lines are omitted for readability; `{PlayerName}` remains the runtime player-name placeholder. Contract branches are conditional, not one uninterrupted conversation.

The current scene uses **vehicle live loads** for all three opening contracts. Bhan teaches the player to build convenient direct crossings; this transcript does not introduce destroyed bridges or a collapsed delivery route. The cargo-story documents in this folder are separate earlier proposals, not the source for this scene transcript.

## Phase and trigger reference

Indices are zero-based and describe the current scene order. Use the stable Phase ID when wiring a phase action.

| NPC / index | Phase ID | Main action and outgoing trigger |
| --- | --- | --- |
| Bhan / 0 | `Greetings` | Finish dialogue → start supply-cart guide and move to `LiveLoad`. |
| Bhan / 1 | `LiveLoad` | Arrival dialogue → `WorkBench`. Inspection is not the outgoing phase gate here. |
| Bhan / 2 | `WorkBench` | Ordered dialogue/material introductions → `Give Contract Build Location 1` and follow guide. |
| Bhan / 3 | `Give Contract Build Location 1` | Offer `TUT_CONTRACT1`; assigned cinematic through offer flow; contract completion advances progression. |
| Bhan / 4 | `DeadLoad` | Finish dialogue → `Beam` and assigned lesson trigger. |
| Bhan / 5 | `Beam` | Dialogue and Wood Beam introduction → `Build Tutorial 2`. |
| Bhan / 6 | `Build Tutorial 2` | Offer `TUT_CONTRACT2`; contract completion advances progression. |
| Bhan / 7 | `Follow` | Arrival dialogue → follow guide and `City`. |
| Bhan / 8 | `City` | Interacting skips active follow guide; finish dialogue → `Shopkeeper`, cinematic, and follow guide again. |
| Bhan / 9 | `Shopkeeper` | Interacting skips active follow guide. Offer `ShopKeeper`; contract completion advances progression. |
| Bhan / 10 | `Go to Shopkeeper` | Arrival/repeat dialogue → Meet Shopkeeper guide. |
| Susan / 0 | `GiveShop` | Finish dialogue → Susan moves to `MainCity`. |
| Susan / 1 | `MainCity` | Arrival dialogue → `GIVESGOP`. |
| Susan / 2 | `GIVESGOP` | Arrival dialogue → unlock `shop` with collect popup and move to `RepeatDIal`. |
| Susan / 3 | `RepeatDIal` | Arrival/repeat dialogue → return-to-Bhan guide and move Bhan to `CollectMap`. |
| Bhan / 11 | `CollectMap` | Interacting skips return guide; finish dialogue → unlock `minimap` with collect popup and move to `Farewell`. |
| Bhan / 12 | `Farewell` | Arrival dialogue → Find Reed tutorial, assigned NPC spawner, and `Find Reed`. |
| Bhan / 13 | `Find Reed` | Repeat reminder → `TryStartTutorial` for Find Reed guide. |
| Bhan / 14 | `Delete` | Extra authored phase with repeated reminder and incomplete arrival event; not a documented normal-story transition. |

Susan's interaction gate references `ShopKeeperContract`. With its current Vehicle mode, the code permits interaction once that contract has a saved bridge or is completed. It does not require a player-cargo delivery in this mode.

## 1. Professor Bhan — Greetings

Phase ID: `Greetings`.

- There you are, {PlayerName}
- Are you ready to learn the basics?
- Before you start building, I want you to understand what will cross your bridge.
- Follow me to the Vehicle.

On dialogue completion, call `StartTutorial()` on the supply-cart guide and move Bhan to `LiveLoad`.

Guide text: “Go to the supply cart.” Its lesson ID is `Go to the supply cart.` and its waypoint advances the tutorial when reached.

## 2. Professor Bhan — Live Load

Phase ID: `LiveLoad`. Dialogue starts on arrival and is marked repeatable.

- Inspect the supply cart.

The current outgoing phase event is **dialogue finished → WorkBench**. This phase does not wait for the inspection window to close. Vehicle inspection and lesson UI are separate from NPC dialogue.

## 3. Professor Bhan — Workbench and materials

Phase ID: `WorkBench`. This uses an ordered dialogue/material sequence.

### Opening dialogue

- Here we are, {PlayerName}.
- This is where you'll build your first bridge.
- The Blueprint shows the area that needs to be connected.
- Before you begin, let's take a look at the materials available for this project.

### Material introductions

The sequence presents **Wood Road**, then the assigned **Wood Supports** material.

The following lines are stored in the Wood Road material step's dialogue field:

- First, you'll be using a Wood Road for the bridge deck.
- Used as the roadway of the bridge.
- Vehicles and other loads travel across this component.

The Wood Supports material step contains:

- You'll also need Wood Supports to hold up the bridge.

**Runtime distinction:** the current `MaterialUnlock` handler shows the material introduction but does not play that step's embedded dialogue. These four lines are authored data, not spoken dialogue in this sequence. They need separate Dialogue steps to be heard.

### Closing dialogue

- Supports the bridge structure and transfers loads toward the ground.
- These are the materials available for this project.
- Use them carefully and make sure your bridge can safely carry the required load.
- When you're ready,follow me to introduce the contract.

Completion moves Bhan to `Give Contract Build Location 1` and calls `StartTutorial()` on `FollowProfessorBhan`.

## 4. Bhan Tutorial 1 — Beam Bridge contract

Phase ID: `Give Contract Build Location 1`. Asset: `Bhan Tutorial 1.asset`; contract ID: `TUT_CONTRACT1`.

### Offer

- Now you know what the project requires, what you'll be carrying across the bridge, and which materials are available to you.
- You've also learned the basic tools you'll need to construct and test your bridge.
- I think you're ready for your first contract.

### After accepting

- Your first contract will be a simple bridge construction project.
- The crossing ahead needs a safe and reliable connection between both sides of the canyon.
- For this project, you'll be constructing a Beam Bridge.
- A beam bridge is a simple structure that uses horizontal members to carry the load toward its supports.
- Your job is to construct the bridge and make sure it can safely make the Vehicle.
- Review the contract carefully, then decide how you'll approach the construction.

### Reminder while unfinished

- You can now go to the construction site whenever you are ready..
- Enter Build Mode and begin construction.

### Completion / turn-in dialogue

- Good work, {PlayerName}.
- Your first training bridge has passed its test.
- You now know how to accept a contract, use the blueprint, build a bridge, and test your design.
- But your work is not finished yet.
- An engineer should inspect the structure after construction.
- Open your Almanac and review your first entry.

The phase also contains the optional placeholder line `map`. The contract asset supplies the branches above; the placeholder is not a replacement for them.

## 5. Professor Bhan — Dead Load

Phase ID: `DeadLoad`.

- Good work, {PlayerName}.
- Your bridge is standing, and it can carry the required live load.
- But remember, live load isn't the only load a bridge has to carry.
- Even without a vehicle or a person crossing it, the bridge itself still has weight.
- That weight is called dead load.

Finishing this dialogue invokes the phase action for `Beam` and the assigned lesson's `ShowLesson()`.

## 6. Professor Bhan — Introducing Wood Beams

Phase ID: `Beam`.

- Well done, {PlayerName}.
- You've completed your first bridge using a simple beam bridge.
- You also learned that the bridge itself has weight, while the vehicle introduces a live load.
- Now, we're going to make the structure a little more interesting.
- This time, you'll learn how structural members can work together.
- Before we begin, there's new material you'll need for this project.

The Wood Beam material introduction follows. Completing the ordered sequence moves Bhan to `Build Tutorial 2`.

## 7. Bhan Tutorial 2 — Truss Bridge contract

Phase ID: `Build Tutorial 2`. Asset: `Bhan Tutorial 2.asset`; contract ID: `TUT_CONTRACT2`.

### Offer

- Now that you've seen the new material, let's look at today's contract.

### After accepting

- The bridge must safely carry the Vehicle crossing one after another.
- This time, the road alone will not be enough.
- You'll reinforce it using the Wood Beams you just learned about.
- A truss is made of connected structural members that work together to support the load.
- For the structure to remain stable, those members need to work together as forces act on the bridge.
- During the simulation, pay attention to how the connected members respond while the Vehicle cross.
- Pay attention to how the members respond when the pedestrians cross the bridge.
- Now that you've accepted the contract, follow the path to the Build Mode.

### Reminder while unfinished

- What are you waiting for?

### Completion / turn-in dialogue

- Excellent work, Engineer.
- You've now built your first truss bridge.
- You also learned how to erase. select, copy, and paste structural components.
- Those tools will become useful when your designs become more complicated.

The phase's optional dialogue field still contains `fdsadsdsdasd`, an unfinished placeholder separate from the contract dialogue.

## 8. Professor Bhan — Follow

Phase ID: `Follow`. Dialogue starts on arrival.

- Follow me

Finishing it starts `FollowProfessorBhan` again and moves Bhan to `City`.

## 9. Professor Bhan — Entering Canyon Ville

Phase ID: `City`.

- Ahead is Canyon Ville.
- But before I let you explore the city...
- The shop owner contacted us and needs our help.
- Come with me.

Interacting with Bhan calls `Skip()` on the active follow sequence. Finishing the conversation moves him to `Shopkeeper`, plays the assigned cinematic, and starts the follow sequence again.

## 10. Shopkeeper contract — The first independent project

Phase ID: `Shopkeeper`. Asset: `ShopKeeperContract.asset`; contract ID: `ShopKeeper`. Professor Bhan gives this contract.

### Offer

- The road ends here because the canyon separates the settlement from the land on the other side.
- That shop belongs to one of the local merchants.
- Right now, people have to take a long route around the canyon just to reach it.
- The Shopkeeper commissioned a bridge to create a direct connection to Central Canyonville.
- This will be your first real project.
- Remember what you've learned.
- Consider the structure, the loads it will carry, and how your design will support them.
- During training, I showed you exactly what to build.
- This time, the decision is yours.
- You may use a beam or truss design.
- Study the site, review the required load, and choose the structure you believe is appropriate.

### After accepting

The continuation dialogue is currently empty. There is no additional authored acceptance speech.

### Reminder while unfinished

- This is your first real contract, {PlayerName}.
- You already know the basics from your training.
- Now it's time to apply what you've learned.
- You may choose the bridge design you believe is most suitable for the site.
- Just make sure your bridge connects both sides and meets the contract requirements.
- The construction site is just ahead.
- Once you're ready, head to the Blueprint.
- That's where you'll begin construction.

### Completion / turn-in dialogue

- Well done.
- You successfully connected the shop to Central Canyonville.
- More importantly, you made the decision yourself.
- You chose your approach, built the structure, and tested your design.

## 11. Professor Bhan — Go to the Shopkeeper

Phase ID: `Go to Shopkeeper`. Dialogue starts on arrival and is repeatable.

- Go to the Shopkeeper

Finishing it calls `StartTutorial()` on the sequence displaying “Meet Shopkeeper.” Its lesson ID is `ProceedToShopKeeper`, and its waypoint advances on arrival at Susan.

## 12. Grandma Susan — Shopkeeper arrival

Phase ID: `GiveShop`.

- Ah, there you are!
- I can see the bridge is finally complete.

Finishing this conversation moves Susan to `MainCity`.

## 13. Grandma Susan — Central Canyonville

Phase ID: `MainCity`. Dialogue starts on arrival.

- Now the shop is connected directly to Central Canyonville.
- People can finally reach us without having to take the long way around.
- Thank you, {PlayerName}.
- You've done exactly what I needed.

Finishing this conversation moves Susan to `GIVESGOP`.

## 14. Grandma Susan — Shop unlocked

Phase ID: `GIVESGOP`. Dialogue starts on arrival.

- Since you're going to be working on more projects around Canyon Crossing, there's something else you should know.
- The work you complete around Arcadia will earn you Gold and other rewards.
- You can spend your Gold here on items for your journey.
- You can use what you've earned to purchase items for your engineering journey.
- Some items can also be equipped to customize your appearance.

After this dialogue, the phase unlocks permanent feature `shop` with a collect popup and moves Susan to `RepeatDIal`. The Shop Manager has an on-open tutorial assigned; that tutorial is triggered through opening the Shop.

## 15. Grandma Susan — Repeat conversation and return to Bhan

Phase ID: `RepeatDIal`. Dialogue starts on arrival and remains repeatable.

- You can come back anytime.
- Good luck with your next project, {PlayerName}.
- You can spend your Gold here on items for your journey.
- You can use what you've earned to purchase items for your engineering journey.
- Some items can also be equipped to customize your appearance.

The phase also retains the `shop` unlock fields. Finishing its dialogue calls `StartTutorial()` on `GoBackToProfBhan` and moves Bhan to `CollectMap`. These outgoing actions are also wired on later repeats.

Guide text: “Go back to Professor Bhan”. Its waypoint targets Bhan and advances the guide on arrival.

## 16. Professor Bhan — Collect the map

Phase ID: `CollectMap`.

- Welcome back, {PlayerName}.
- I see you completed the bridge.
- Well done.
- You successfully connected the shop to Central Canyonville.
- More importantly, you made the decision yourself.
- You chose your approach, built the structure, and tested your design.
- That's what engineering is about.
- You won't always be given the answer.
- Sometimes, you'll have to study the problem and decide which solution works best.
- There's one more thing you need before you continue.
- Arcadia is much larger than Canyon Crossing.
- There are projects waiting for engineers in other regions as well.
- That world map shows the three regions of Arcadia.
- You've already started here in Canyon Crossing.
- Beyond it is the Town River, where the waterways create new challenges.
- And farther ahead is the Industrial Zone,  where you'll face more demanding projects.

Interacting calls `Skip()` on `GoBackToProfBhan`. Finishing the dialogue unlocks `minimap` (display name `Minimap`) with a collect popup and invokes the phase action for `Farewell`.

## 17. Professor Bhan — Farewell and introduction to Reed

Phase ID: `Farewell`. Dialogue starts on arrival.

- Keep that world map with you.
- It will help you find the projects waiting for you.
- As you complete your work, more areas will become available.
- From here on, you won't always have me beside you.
- You'll begin working with contractors throughout Arcadia, each with projects and problems of their own.
- There's someone here in Canyon Crossing I'd like you to meet first.
- His name is Reed.
- He grew up here and has spent years maintaining the roads, crossings, and supply routes around the canyon.
- When a route becomes difficult to use, he's usually one of the first people the locals turn to.
- I've already told him about the work you've done here.
- He has a project that could use an Engineer.
- Listen to what he needs, study the site carefully, and decide how you'll approach the problem.
- Most importantly, test your bridge before you call the work complete.
- You already have the knowledge to begin.
- Now it's time to gain the experience to use it.
- I'll still be here if you need guidance.
- But your next project is yours to handle.
- Go find Reed. He'll tell you what he needs.

On completion, the phase starts the Find Reed sequence, calls the assigned `NPCSpawnerCondition.SpawnNPC()`, and moves Bhan to `Find Reed`.

## 18. Professor Bhan — Find Reed reminder

Phase ID: `Find Reed`. Repeatable dialogue.

- Explore the City and Find Reed

Finishing this reminder calls `TryStartTutorial()` on the Find Reed sequence. It displays “Find Reed”, has no waypoint, and currently shares lesson ID `ProceedToShopKeeper` with the earlier Meet Shopkeeper guide.

## 19. Extra authored phase — Delete

Phase ID: `Delete`. This additional Bhan phase contains:

- Explore the City and Find Reed

Its arrival UnityEvent has a GameObject target but no method name. The phase name alone does not delete or despawn Bhan. No normal-story transition into this phase was identified in the phase flow above.

## Contract settings snapshot

| Setting | Bhan Tutorial 1 | Bhan Tutorial 2 | Shopkeeper |
| --- | --- | --- | --- |
| Contract ID | `TUT_CONTRACT1` | `TUT_CONTRACT2` | `ShopKeeper` |
| Live-load mode | Vehicle | Vehicle | Vehicle |
| Required live-load weight | 700 kg | 1,400 kg | 2,000 kg |
| Budget | 75,000 | 75,000 | 75,000 |
| Configured span | 30 | 60 | 50 |
| Gold reward | 500 | 500 | 500 |
| XP reward | 150 | 1,000 | 100 |
| Auto Collect Reward | Off | Off | Off |
| Is Tutorial Contract | Off | Off | On |
| Vehicle cargo allowed | No | No | No |

All three assets list Professor Bhan as the client. Their Bhan phases have no Linked Cargo assigned. Completion dialogue is the NPC turn-in branch; configuration retains manual reward collection.

## Calling and replaying tutorial sequences

| Method | Current behavior |
| --- | --- |
| `StartTutorial()` | Explicit start/replay, bypassing completed-lesson and previous-lesson checks. Queues behind an active tutorial with these checks bypassed. Does not interrupt it. |
| `TryStartTutorial()` | Applies saved completion and prerequisite checks. If queued, eligibility is checked when considered for playback. |
| `Next()` | Advances only if this sequence is currently playing. |
| `Skip()` | Completes only if this sequence is currently playing, using the manager's completion flow. |

Start still requires a Tutorial Manager and at least one tutorial step. A queued sequence waits for the active tutorial to finish. Reusing a completed lesson ID can block `TryStartTutorial()` even if the scene object or displayed text differs.

Each Tutorial Step also has `Lock Look`, `Lock Jump`, and `Lock Run`. Lock Run disables sprint and the Run button for that active step; it is independent of NPC phase movement.

## Authoring issues still present in the source

These observations are not game changes made by this document update.

- **Duplicate lesson ID:** Meet Shopkeeper and Find Reed use `ProceedToShopKeeper`. Find Reed needs a unique lesson ID before relying on its `TryStartTutorial()` reminder.
- **Find Reed completion:** no waypoint, Next button, Skip button, pointer-click advancement, or required action is assigned. It needs an external completion call or an authored completion condition.
- **Repeat conversation replays progression:** Susan's `RepeatDIal` starts the return guide and moves Bhan to `CollectMap` after every repeat, which can send later story progression backward.
- **Inspection gate:** `LiveLoad` advances when “Inspect the supply cart.” finishes. Mandatory inspection would require the phase transition to be driven by the inspection/lesson event.
- **Material-step text:** WorkBench's material steps contain dialogue the MaterialUnlock handler does not speak.
- **Placeholders:** `map` and `fdsadsdsdasd` remain in the two training contract phases' optional dialogue fields.
- **Incomplete extra phase:** Bhan's `Delete` arrival event has no method assigned.
- **Mixed load descriptions:** Tutorial 2 uses a vehicle but mentions pedestrians, “Vehicle crossing one after another,” and “the Vehicle cross.”
- **Wording:** “safely make the Vehicle,” “ready..”, “When you're ready,follow me”, and “erase. select” remain authored.
- **Place names:** Canyon Ville, Central Canyonville, Canyonreach, Canyon Crossing, and Arcadia need an agreed distinction between settlements and regions. The shop contract description says Canyonreach while its dialogue says Central Canyonville.
- **Formatting:** several strings use a second `<#E09500>` instead of `</color>` to close color spans. This transcript omits those tags.
- **Contract flags:** both assets named Bhan Tutorial have Is Tutorial Contract off; ShopKeeperContract has it on.

## Source reference

- Scene: `Assets/Scenes/CanyonCrossing.unity`.
- Bhan progression component: file ID `650395270`, save ID `MainContractNPC`.
- Susan progression component: file ID `865950345`, save ID `ShopKeeper`.
- Contract text: `Assets/BridgeBuilder/Data/Contract/Bhan Tutorial 1.asset`, `Bhan Tutorial 2.asset`, and `ShopKeeperContract.asset`.
- Runtime interpretation: `NPCProgressionManager.cs`, `NPCContractGiver.cs`, `TutorialSequence.cs`, and `TutorialManager.cs`.

This update changes documentation only. It does not rewrite dialogue assets, phase IDs, scene events, or player saves.
