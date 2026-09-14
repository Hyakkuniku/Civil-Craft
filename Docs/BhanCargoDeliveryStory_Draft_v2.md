# Story Draft v2 — Professor Bhan and the Canyon Delivery

> **Draft only.** This document does not describe changes already applied to the game. No dialogue, phase, scene, contract, or trigger has been modified from this draft.

## Story purpose

This opening chapter introduces the player as an aspiring civil engineer and Professor Bhan as their teacher. Bhan does not lecture from the sidelines: he places the player in practical situations, asks them to observe what happens, and explains the engineering idea after they have seen it in action.

The chapter keeps the existing delivery story. One persistent crate of tools and replacement parts must reach Grandma Susan's shop. Every bridge moves that same crate closer to its destination, giving the tutorials an immediate human purpose.

The chapter teaches:

- how contracts, Build Locations, materials, construction, and simulation work;
- the basic behavior of a beam bridge;
- dead load and live load;
- why triangles make a truss useful;
- tension, compression, and equilibrium;
- how to inspect a result, revise a design, and try again;
- that engineering is ultimately about solving problems for people.

## Character direction

### Professor Bhan

Bhan is warm, observant, and precise. He treats the player like a promising student rather than an assistant who only follows orders. He asks short questions, lets the player experiment, and explains mistakes without making failure feel like punishment.

He should sound like a professor in ordinary conversation—not like a textbook. Technical terms are introduced only after the player has something visible to connect them to.

### Grandma Susan

Grandma Susan is practical, friendly, and lightly humorous. The delivery matters because her shop supplies workers and residents around Canyon Crossing. She sees the player's bridges as useful new connections that make everyday travel easier, not merely completed structures.

### The player

The player has studied some engineering but has little field experience. They are not treated as completely ignorant. Bhan teaches them how to apply knowledge through observation, construction, testing, and revision.

## Proposed chapter flow

The chapter uses ten story phases. This replaces several small “walk here, then talk again” moments with fewer, more meaningful conversations.

---

## Phase 1 — A Longer Road Than Necessary

**Location:** Near Professor Bhan's starting area  
**Purpose:** Establish Bhan, the player, the crate, and the reason bridges matter.

The player finds Bhan beside a sealed supply crate and a map of the long road to Grandma Susan's shop. The existing route is usable, but it winds around several canyon gaps and makes an ordinary delivery take far longer than it should.

**Professor Bhan**

> There you are, {PlayerName}. Your timing is excellent. This delivery route is not.

> This crate is going to Grandma Susan's shop. It contains tools, replacement parts, and enough supplies to keep half of Canyonreach working.

> The road can take us there, but it winds around every gap in the canyon. A simple delivery becomes an all-day journey.

Bhan looks toward the first gap.

> You already know some engineering. What you need now is practice: real ground, real loads, and real consequences.

> Good engineering is not only about repairing failures. It can make an existing journey shorter, safer, and more convenient.

> Help me deliver this crate by creating that better route, and I will teach you how to turn what you know into bridges people can trust.

> Pick it up. Our first lesson is waiting at the crossing.

**Gameplay beat**

- The player learns how to pick up the story cargo.
- A nearby drop-off marker leads directly to the first Build Location.
- The crate cannot be dropped elsewhere.
- Bhan walks or runs to the first site according to the phase setting.

---

## Phase 2 — Read the Site Before You Build

**Location:** First Build Location  
**Purpose:** Introduce the Blueprint, contract requirements, and available materials without a long lecture.

The player places the crate in the preparation area. Bhan stands at the edge and studies the gap.

**Professor Bhan**

> Before an engineer draws a solution, they study the problem.

> Two banks. A short span. Firm ground on both sides. We do not need an elaborate structure here.

> Open the Blueprint. It marks the area your bridge must connect.

The first material introduction shows the **Wood Road**.

> This road will form the deck—the surface that carries you and the cargo across.

The support material is introduced.

> The deck still needs a clear path to the ground. These supports will carry its load down into the banks below.

> Build the simplest safe answer to the problem. That is often where good engineering begins.

**Contract offered:** First training contract — Beam Bridge

**After acceptance**

> Connect the banks, support the deck, and keep within the project budget.

> When you are satisfied, test it. A drawing can make a promise; a test tells us whether the structure keeps it.

---

## Phase 3 — The First Test

**Purpose:** Teach simulation, failure, revision, and delivery through action.

The player presses **Simulate**, exits the building view, picks up the same crate, and attempts to cross.

### If the bridge fails

The player and crate return safely to their starting positions, and the bridge returns to editing.

**Professor Bhan**

> Good. Now we know something the drawing could not tell us.

> Failure during a test is information, not the end of the work. Look at where the structure moved, revise the design, and test it again.

### If the bridge succeeds

The player places the crate at the destination checkpoint. Bhan is already waiting there when the completion presentation closes, so there is no walk back to the original bank.

**Professor Bhan**

> Nicely done. The bridge did exactly what the site required.

> But do not put your notebook away yet. The crossing showed us more than success.

---

## Phase 4 — Weight That Stays, Weight That Moves

**Location:** First bridge destination  
**Purpose:** Explain dead load and live load after the player has observed both.

The completed bridge remains visible behind Bhan.

**Professor Bhan**

> Your bridge carried weight before you ever stepped onto it.

> The deck and supports carry their own permanent weight. Engineers call that **dead load**.

The bridge itself is highlighted.

> Then you crossed with the crate. You were temporary, moving loads. That is **live load**.

The player and crate are highlighted, or downward load arrows appear over the deck.

> A safe bridge must carry both at the same time. Forget either one, and the calculation tells only half the truth.

> Record the lesson in your Almanac. Then collect your contract reward.

**Lesson unlocked:** Beam Bridge; Dead Load and Live Load  
**Reward:** Gold and XP are collected through the normal contract turn-in.

**Transition dialogue**

> Grandma Susan's crate is one crossing closer, but the next span is wider. Bring it along. This time, a simple beam will need help.

---

## Phase 5 — Why Engineers Like Triangles

**Location:** Second Build Location  
**Purpose:** Introduce trusses visually and give the player a reason to learn copy/paste tools.

The player carries the crate along a short forward route and places it at the second preparation point.

Bhan indicates the wider gap and a small triangular demonstration frame.

**Professor Bhan**

> Try pushing the corner of a rectangle and it can change shape. A triangle is far less willing to do that.

> Join triangles together and you create a **truss**—a structure whose members share the work.

The **Wood Beam** is introduced.

> Use beams to reinforce the road. You can copy and repeat a good section instead of rebuilding every triangle by hand.

> The pattern may repeat, but every connection still matters.

**Contract offered:** Second training contract — Truss Bridge

**After acceptance**

> Build a stable truss, watch the budget, and then carry the delivery across.

> During the test, pay attention to the members—not only the road.

---

## Phase 6 — Forces Working Together

**Purpose:** Let the player see tension, compression, and equilibrium during the second cargo test.

During simulation, members receive a simple visual treatment as the player and crate move across:

- members being pulled are marked as **tension**;
- members being pushed are marked as **compression**;
- the visualization changes as the live load moves;
- the lesson remains short enough that the player can still watch the bridge.

### If the bridge fails

**Professor Bhan**

> Watch the members nearest the failure. Were they being pulled, pushed, or left without a clear path to a support?

> Strength is important, but arrangement decides how that strength is used.

### If the bridge succeeds

The player places the crate at the second destination. Bhan waits there for the turn-in.

**Professor Bhan**

> There—that is a structure working as a system.

> Some members were pulled. That is **tension**. Others were pushed. That is **compression**.

> The bridge remained stable because its forces and reactions stayed in **equilibrium**.

> Equilibrium does not mean that nothing happens. It means the forces balance well enough for the structure to do its job.

**Lesson unlocked:** Truss Bridge; Tension, Compression, and Equilibrium  
**Reward:** Gold and XP are collected at the destination.

---

## Phase 7 — The Last Shortcut

**Location:** Entrance to Canyonreach / view toward Grandma Susan's shop  
**Purpose:** Shift from guided lesson to a small independent application.

The second turn-in flows directly into this conversation; it does not require another interaction with Bhan.

**Professor Bhan**

> Look ahead. That is Grandma Susan's shop.

> We have crossed two gaps, but the usual road still bends around this final ravine. People can see the shop from Canyonreach and still have to travel the long way around.

> I have shown you how to read a site, support a beam, recognize loads, and make members work together.

> For this last crossing, I will not choose the answer for you.

> Study the requirements. Choose the structure that suits them. Then prove your decision with the delivery we have carried all this way.

The player takes the crate to the final preparation point.

---

## Phase 8 — Grandma Susan's Contract

**Purpose:** Give the player a supervised but less scripted application of the learned systems.

**Contract offered:** Create Grandma Susan's Shortcut

**Professor Bhan**

> This is a real commission, not a classroom exercise.

> The bridge must safely carry the delivery, remain within budget, and create a reliable route to the shop.

> Build what the site needs. If the test reveals a weakness, revise it. That is the work.

### Reminder while incomplete

> Start with the problem, not the shape of the bridge. What must cross, where can the structure be supported, and how will the load reach the ground?

### Successful test

The player carries the same crate over the final bridge and places it in Grandma Susan's delivery area.

Bhan meets the player at the destination for the final turn-in.

**Professor Bhan**

> Three crossings. One continuous route.

> You did not build bridges simply to pass three tests. You created a more convenient connection people can use every day.

> That is the part of engineering no diagram should let you forget.

> Go on. The person waiting for this delivery should be the first to congratulate you.

---

## Phase 9 — Delivered at Last

**Location:** Grandma Susan's shop  
**Purpose:** Resolve the delivery and unlock the shop through character payoff.

Grandma Susan examines the seal on the crate and immediately recognizes it.

**Grandma Susan**

> Bhan's supply crate! I was beginning to think the canyon had claimed it as a landmark.

> Tools, pulley parts, repair fittings—everything is here.

She looks back across the completed route.

> Professor Bhan told me one of his students was building a new route across the canyon. He neglected to mention that you would carry the delivery over every bridge yourself.

> You brought me more than supplies, {PlayerName}. Customers can reach the shop much more easily now, and I can send what they need back into Canyonreach without the long detour.

> That route will keep helping people long after this crate is empty.

**Shop introduction**

> Contracts earn Gold and experience. Gold buys useful equipment here—and, if you insist, something less dusty to wear.

> My shop is open to you now. Spend wisely. Engineers always discover one more thing they need.

**Feature unlocked:** Shop

---

## Phase 10 — From Student to Engineer

**Location:** Outside the shop, overlooking the new shortcut route  
**Purpose:** Close the tutorial chapter and establish the wider Arcadia story.

Bhan joins the player after the shop introduction.

**Professor Bhan**

> When we began, you knew the names of structures and forces. Now you have watched them work—and watched them fail.

> That difference is experience.

He gives the player the map of Arcadia.

> Canyon Crossing is only one region. Beyond it lies Town River, where waterways demand different materials and different thinking. Farther still is the Industrial Zone, where heavier loads leave little room for careless decisions.

> I will always be here when you need guidance, but I will not assign every project from now on.

> Contractors across Arcadia know the problems their communities face. Listen to them. Study each site. Then build the solution you can defend.

> Your first delivery is complete, Engineer. Your journey is not.

**Features unlocked:** World Map and the next main contractor  
**Bhan's new role:** Mentor, lesson reviewer, and occasional story guide rather than the source of every contract.

## Recommended phase changes from the current delivery outline

### Combine

- Combine the first cargo introduction and first preparation handoff into **A Delivery Interrupted**.
- Combine each successful bridge result, destination turn-in, and engineering explanation into one continuous destination scene.
- Combine the second turn-in and the reveal of Grandma Susan's shop.

### Remove

- Remove dialogue whose only purpose is “follow me,” “enter the Build Location,” or “go speak to the Shopkeeper” when a waypoint and visible destination already communicate it.
- Remove the separate load lecture before the player has tested a bridge.
- Keep vehicle inspection out of Bhan's opening chapter. The crate provides the first live-load example; a later contractor can introduce vehicle inspection when actual vehicles become important.

### Add

- Add short failure responses from Bhan so retrying feels like part of engineering practice.
- Add the final Bhan epilogue after the delivery to establish Arcadia, the World Map, and his long-term mentor role.
- Add visual lesson beats during simulation so dialogue explains what the player just observed instead of carrying the full lesson by itself.

## Pacing rules

- Never make the player return across a bridge only to turn in the contract. Bhan should meet them at the cargo destination.
- Do not interrupt a simulation with long dialogue. Use brief labels or arrows during the test, then explain afterward.
- Introduce no more than one main engineering idea before asking the player to act.
- Let the first bridge be highly guided, the second partly guided, and the shop bridge mostly independent.
- Keep every cargo movement forward and purposeful.
- Save detailed definitions and completed bridge records in the Almanac; keep spoken dialogue natural.

## Continuity rules

- The crate is the same persistent story cargo from Phase 1 through Phase 9.
- Grandma Susan cannot be spoken to until the final cargo delivery is complete.
- Cargo placement alone does not award contract rewards; the destination turn-in still completes the contract flow.
- Bhan's tutorial contracts remain Beam Bridge first and Truss Bridge second.
- The final shop connection tests application and does not introduce another large lesson.
- Reed, Vance, Silas, and later contractors take over project commissioning after Bhan's opening chapter.

## Reference

This draft draws its broader Arcadia structure, practical learning progression, engineering topics, and Bhan's transition from teacher to mentor from the supplied **Updated Story** Google Doc. It deliberately retains Civil Craft's newer persistent cargo-delivery storyline as the chapter's central narrative thread.
