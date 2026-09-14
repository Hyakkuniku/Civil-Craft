# In-game story dialogue: Professor Bhan to the Shopkeeper

This transcript follows the current `CanyonCrossing` story from Professor Bhan's opening through Grandma Susan unlocking the Shop. Text is preserved as authored, including capitalization, grammar, and repeated lines. `{PlayerName}` is replaced by the player's saved name at runtime.

Contract offer, acceptance, reminder, and completion sections are conditional branches. They are all included here even though the player will not see every branch consecutively.

## 1. Professor Bhan — Greetings

- There you are, {PlayerName}
- Are you ready to learn the basics?
- Before you start building, I want you to understand what will cross your bridge.
- Follow me to the supply cart.

## 2. Professor Bhan — Live Load

- Inspect the supply cart.

The vehicle inspection and its lesson occur here; their UI lesson text is not NPC dialogue.

## 3. Professor Bhan — Workbench and materials

- Here we are, Engineer.
- This is where you'll build your first bridge.
- The Blueprint shows the area that needs to be connected.
- First, you'll be using a Wood Road for the bridge deck.
- Vehicles and other loads travel across this component.

The Wood Road material introduction appears.

- Vehicles and other loads travel across this component.

The next material introduction appears.

- Supports the bridge structure and transfers loads toward the ground.
- These are the materials available for this project.
- Use them carefully and make sure your bridge can safely carry the required load.
- When you're ready,follow me to introduce the contract.

## 4. Bhan Tutorial 1 — Beam Bridge contract

### Offer

- Now you know what the project requires, what you'll be carrying across the bridge, and which materials are available to you.
- You've also learned the basic tools you'll need to construct and test your bridge.
- I think you're ready for your first contract.

### After accepting

- Your first contract will be a simple bridge construction project.
- The crossing ahead needs a safe and reliable connection between both sides of the canyon.
- For this project, you'll be constructing a Beam Bridge.
- A beam bridge is a simple structure that uses horizontal members to carry the load toward its supports.
- Your job is to construct the bridge and make sure it can safely make the cart.
- Review the contract carefully, then decide how you'll approach the construction.

### Reminder while unfinished

- You can now go to the construction site whenever you are ready..
- Enter the Blueprint and begin construction.

### After completion

- Good work, {PlayerName}.
- Your first training bridge has passed its test.
- You now know how to accept a contract, use the blueprint, build a bridge, and test your design.
- But your work is not finished yet.
- an engineer should inspect the structure after construction.
- Open your Almanac and review your first entry

## 5. Professor Bhan — Dead Load

- Good work, {PlayerName}.
- Your bridge is standing, and it can carry the required live load.
- But remember, live load isn't the only load a bridge has to carry.
- Even without a vehicle or a person crossing it, the bridge itself still has weight.
- That weight is called dead load.

## 6. Professor Bhan — Introducing beams and trusses

- Well done, Engineer.
- You've completed your first bridge using a simple beam structure.
- You also learned that the bridge itself has weight, while the cart introduces a live load.
- Now, we're going to make the structure a little more interesting.
- This time, you'll learn how structural members can work together.
- Before we begin, there's new material you'll need for this project.

The Wood Beam material introduction appears.

## 7. Bhan Tutorial 2 — Truss Bridge contract

### Offer

- Enter the Build location so we can start

### After accepting

- The bridge must safely carry several pedestrians crossing one after another.
- This time, the road alone will not be enough.
- You'll reinforce it using the Wood Beams you just learned about.
- When a truss carries a load, its members can experience different types of forces.
- Some members are pulled apart. This is called tension.
- Other members are pushed together. This is called compression.
- These forces act within the truss as the members work together to support the load.
- To keep the structure stable, the forces acting on it must remain balanced.
- This condition is called equilibrium.
- Pay attention to how the members respond when the pedestrians cross the bridge.
- Now that you've accepted the contract, follow the path to the Blueprint.

### Reminder while unfinished

- What are you waiting for?

### After completion

- Excellent work, Engineer.
- You've now built your first truss bridge.
- You also learned how to select, copy, and paste structural components.
- Those tools will become useful when your designs become more complicated.
- But before you move on, there's one more building tool I want you to learn.

## 8. Professor Bhan — Entering Canyon Ville

- Ahead of is Canyon Ville
- But before i let you explore the city
- The shop owner contacted us and needed our help.
- Follow me.

## 9. Shopkeeper contract — Connecting the shop

### Offer from Professor Bhan

- A local shopkeeper has asked for our help.
- Her shop is separated from Canyonreach, making it difficult for her to transport supplies and reach customers in the city.
- She needs a bridge that will safely connect her shop to Canyonreach.
- The shopkeeper will meet us once the bridge is complete.
- Are you ready to take on this contract, Engineer?

### Acceptance confirmation

- The shopkeeper is depending on us to reconnect her shop with Canyonreach.
- Are you ready to accept the contract?

### Reminder while unfinished

- The bridge connecting the shop to Canyonreach still needs to be completed.
- Review the contract and begin construction when you are ready.

### After completion

- Well done, Engineer. The bridge is complete.
- The shopkeeper can now safely transport her supplies and welcome customers from Canyonreach.
- She has arrived to see your work.
- Go and speak with her. She will give you access to the shop.

## 10. Professor Bhan — Go to the Shopkeeper

- Go to the said Shopkeeper

## 11. Grandma Susan — Shopkeeper arrival

- Ah, there you are!
- I can see the bridge is finally complete.

## 12. Grandma Susan — Main City

- Now the shop is connected directly to the Main City.
- People can finally reach us without having to take the long way around.
- Thank you, Engineer.
- You've done exactly what I needed.

## 13. Grandma Susan — Shop unlocked

- Since you're going to be working on more projects around Canyon Crossing, there's something else you should know.
- The work you complete will earn you Gold and other rewards.
- Those rewards can be used here at the Shop.
- You can use what you've earned to purchase items for your engineering journey.
- Some items can also be equipped to customize your appearance.
- You can come back anytime.
- Good luck with your next project, Engineer.

The permanent `shop` feature unlock and Shop tutorial occur after this dialogue.

## Current writing inconsistencies found

- `Professon Bhan` is misspelled in the Live Load phase speaker name.
- `When you're ready,follow me...` is missing a space.
- `make sure it can safely make the cart` likely means `safely carry the cart`.
- `You can now go ... ready..` has two periods.
- `an engineer...` begins with a lowercase letter.
- `Enter the Build location` has inconsistent capitalization.
- `Ahead of is Canyon Ville` is grammatically unclear.
- `before i let you` uses lowercase `i`.
- The place is called both `Canyon Ville`, `Canyonreach`, `Main City`, and `Canyon Crossing`; confirm whether these are intentionally different names.
- `Go to the said Shopkeeper` sounds like placeholder text.
- The Workbench sequence repeats `Vehicles and other loads travel across this component.`
- `ShopKeeperContract` lists Professor Bhan as the client even though its story describes Grandma Susan as the shopkeeper.
