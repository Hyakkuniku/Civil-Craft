using System;
using System.Collections.Generic;

/// <summary>
/// Concise display copy for existing stock tutorials. The serialized step, its
/// callbacks and its completion gates remain untouched. Unknown/custom messages
/// pass through verbatim; this is not automatic truncation or summarization.
/// </summary>
public static class TutorialInstructionText
{
    private static readonly Dictionary<string, string> Replacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Build Mode basics. Keep every named tool, property and required action.
        {
            "Welcome to <b><#E09500>Build Mode!</color></b> In this tutorial, you'll learn the basic tools needed to design, build, and test your first bridge.",
            "Welcome to <b><#E09500>Build Mode!</color></b> Learn the tools to design, build, and test your first bridge."
        },
        {
            "Your objective is to connect the <b><#E09500>two red anchors</color></b> using the available materials. First, learn how to move the build camera.",
            "Connect the <b><#E09500>two red anchors</color></b> with the available materials. First, learn the camera controls."
        },
        {
            "Use the <b><#E09500>2D Camera</color></b> for a clear side view. It helps you inspect the structure and align its components. Tap the <b><#E09500>Camera Button</color></b> to reset the camera tilt.",
            "The <b><#E09500>2D Camera</color></b> helps inspect and align parts from the side. Tap the <b><#E09500>Camera Button</color></b> to reset tilt."
        },
        {
            "This is the current <b><#E09500>construction cost</color></b>. It displays the total cost of the materials currently used in your bridge.",
            "<b><#E09500>Construction cost</color></b> is the total cost of materials used in your bridge."
        },
        {
            "Compare the construction cost with the <b><#E09500>budget progress bar</color></b> to see how much of your available budget has been used and how much remains.",
            "The <b><#E09500>budget progress bar</color></b> shows funds used and remaining. Compare it with your construction cost."
        },
        {
            "This is the <b><#E09500>budget limit</color></b>. Stay within it to complete the project. Using too many materials may prevent you from meeting the contract requirements.",
            "Stay within the <b><#E09500>budget limit</color></b> to meet the contract requirements. Extra materials increase cost."
        },
        {
            "The <b><#E09500>Stress Indicator</color></b> shows how close each bridge member is to its allowable stress limit.",
            "The <b><#E09500>Stress Indicator</color></b> shows each member's stress relative to its allowable limit."
        },
        {
            "Open the <b><#E09500>Bridge Statistics</color></b> panel. It shows live load, dead load, road length, member count, estimated capacity, efficiency, and factor of safety.",
            "Open <b><#E09500>Bridge Statistics</color></b>: live/dead load, road length, member count, estimated capacity, efficiency, and factor of safety."
        },
        {
            "The <b><#E09500>Grid</color></b> turns the construction grid on or off and helps align components accurately. Toggle it once to continue.",
            "The <b><#E09500>Grid</color></b> helps align parts. Toggle it on or off once to continue."
        },
        {
            "This section contains the <b><#E09500>materials</color></b> available for your current project.",
            "These <b><#E09500>materials</color></b> are available for this project."
        },
        {
            "Press and hold any <b><#E09500>material</color></b> to inspect its name, cost, mass, strength limit, maximum length, and type. On a computer, hover over the material to inspect it.",
            "Hold a <b><#E09500>material</color></b> for its name, cost, mass, strength limit, maximum length, and type."
        },
        {
            "Press and hold any <b><#E09500>material</color></b> to inspect its name, cost, mass, strength limit, maximum length, and type.",
            "Hold a <b><#E09500>material</color></b> for its name, cost, mass, strength limit, maximum length, and type."
        },
        {
            "You're ready to begin construction! Let's build your first <b><#E09500>Beam Bridge</color></b>.",
            "Let's build your first <b><#E09500>Beam Bridge</color></b>!"
        },
        {
            "Your bridge must connect the <b><#E09500>two red anchors</color></b>. Use the available materials to create a stable structure.",
            "Use the available materials to build a stable bridge between the <b><#E09500>two red anchors</color></b>."
        },
        {
            "These highlighted points are the <b><#E09500>red anchors</color></b>. They mark the two ends that your bridge must connect.",
            "The highlighted <b><#E09500>red anchors</color></b> mark the two ends your bridge must connect."
        },
        {
            "Start at the highlighted anchor and connect the roadway across the gap. Follow the guide to place the <b><#E09500>Wood Road</color></b>.",
            "Follow the guide to lay <b><#E09500>Wood Road</color></b> from the highlighted anchor across the gap."
        },
        {
            "Drag from the <b><#E09500>highlighted anchor</color></b> across the gap to trace the guided roadway.",
            "Drag from the <b><#E09500>highlighted anchor</color></b> across the gap, following the roadway guide."
        },
        {
            "Great! The <b><#E09500>Wood Road</color></b> provides the surface that vehicles use to cross the bridge.",
            "The <b><#E09500>Wood Road</color></b> is the surface vehicles cross."
        },
        {
            "Now select the <b><#E09500>Wood Pier</color></b>. Place it beneath the road to provide structural support.",
            "Select <b><#E09500>Wood Pier</color></b> to support the road from below."
        },
        {
            "Follow the guide to place a <b><#E09500>Wood Pier</color></b> beneath the roadway. Connect it to the existing road point.",
            "Trace the <b><#E09500>Wood Pier</color></b> guide below the road, connecting it to the existing road point."
        },
        {
            "Trace the remaining guided <b><#E09500>Wood Pier</color></b> beneath the roadway and connect it to the existing point.",
            "Trace the remaining <b><#E09500>Wood Pier</color></b> guide below the road. Connect it to the existing point."
        },
        {
            "<b><#E09500>BEAM BRIDGE COMPLETE!</color></b> Excellent! Your first Beam Bridge is complete. You connected the anchors using a roadway and structural supports. Now test whether it can carry the required load.",
            "<b><#E09500>BEAM BRIDGE COMPLETE!</color></b> Your road and supports connect the anchors. Test whether your first bridge carries the required load."
        },
        {
            "<b><#E09500>Tutorial Complete!</color></b> You learned the basic tools to construct and edit a bridge. Ready for testing? Press <b><#E09500>Play</color></b> to begin the structural simulation.",
            "<b><#E09500>Tutorial Complete!</color></b> You've learned the build and editing tools. Press <b><#E09500>Play</color></b> to test your bridge in the structural simulation."
        },

        // Truss construction and clipboard actions. Positioning is distinct
        // from confirming Paste; never tell the player to confirm too early.
        {
            "Start by placing the <b><#E09500>Wood Road</color></b> between the <b><#E09500>two red anchors</color></b>.",
            "Place <b><#E09500>Wood Road</color></b> between the <b><#E09500>two red anchors</color></b>."
        },
        {
            "Good! The <b><#E09500>Wood Road</color></b> provides the surface for the vehicle to cross. Now let's add the structural members that will support it.",
            "The vehicle crosses on <b><#E09500>Wood Road</color></b>. Now add the structural members that support it."
        },
        {
            "Select the <b><#E09500>Wood Beam</color></b>. You'll use these members to create a <b><#E09500>triangular structural arrangement</color></b>.",
            "Select <b><#E09500>Wood Beam</color></b> to build the <b><#E09500>triangular structure</color></b>."
        },
        {
            "Connect the <b><#E09500>Wood Beams</color></b> using the highlighted guide. Follow the guide to create a <b><#E09500>triangular arrangement</color></b>.",
            "Follow the guide to connect <b><#E09500>Wood Beams</color></b> into a <b><#E09500>triangular arrangement</color></b>."
        },
        {
            "<b><#E09500>FIRST TRUSS SECTION COMPLETE.</color></b>\n\nExcellent! You've connected several <b><#E09500>Wood Beams</color></b> to form your first truss section. Remember what Professor Bhan explained earlier. The triangular arrangement helps the connected members maintain their structural shape. Now let's learn how to build more efficiently.",
            "<b><#E09500>FIRST TRUSS SECTION COMPLETE.</color></b>\n\nYour <b><#E09500>Wood Beams</color></b> form triangles that help the members hold their shape, as Bhan explained. Next, learn to build more efficiently."
        },
        {
            "When a structure contains several members, rebuilding each one individually can take time. The <b><#E09500>Selection Tool</color></b> allows you to select multiple bridge components.",
            "Tap the <b><#E09500>Selection Tool</color></b> to select several bridge parts at once and save rebuilding time."
        },
        {
            "Select the members that form the <b><#E09500>completed truss section</color></b>.",
            "Select all members of the <b><#E09500>completed truss section</color></b>."
        },
        {
            "You can duplicate selected components instead of rebuilding them individually. Press <b><#E09500>Copy</color></b> to create a duplicate of the selected truss section.",
            "Press <b><#E09500>Copy</color></b> to duplicate the selected truss section instead of rebuilding each part."
        },
        {
            "The copied components are ready to be placed. Use <b><#E09500>Paste</color></b> to position the copied truss section on the <b><#E09500>highlighted guide</color></b>.",
            "Move the copied truss section onto the <b><#E09500>highlighted guide</color></b>."
        },
        {
            "Position the copied truss section on the highlighted guide, then press <b><#E09500>Paste</color></b> to confirm.",
            "Align the copied truss section with the guide, then press <b><#E09500>Paste</color></b> to confirm."
        },
        {
            "<b><#E09500>TRUSS STRUCTURE COMPLETE.</color></b>\n\nExcellent! You've built a <b><#E09500>Truss Bridge</color></b> and used the editing tools to duplicate its structure efficiently. Your bridge is now ready for testing. Remember what you learned about <b><#E09500>equilibrium</color></b>. Watch how the connected structural members and supports respond as the vehicle crosses.",
            "<b><#E09500>TRUSS STRUCTURE COMPLETE.</color></b>\n\nYou built a <b><#E09500>Truss Bridge</color></b> efficiently with the copy tools. Test it: watch how its members and supports respond to the crossing vehicle. Remember <b><#E09500>equilibrium</color></b>."
        },

        // Result walkthrough: retain the load, each metric and both exit paths.
        {
            "<b><#E09500>BRIDGE SUCCESSFULLY TESTED!</color></b> Your Beam Bridge safely carried the <b><#E09500>200 kg vehicle</color></b> across the canyon. Your first bridge has passed the structural simulation.",
            "<b><#E09500>BRIDGE SUCCESSFULLY TESTED!</color></b> Your first Beam Bridge passed the structural test, carrying the <b><#E09500>200 kg vehicle</color></b> safely across the canyon."
        },
        {
            "The three result cards show <b><#E09500>COMPLETION</color></b>, <b><#E09500>EFFICIENCY</color></b>, and <b><#E09500>STRENGTH</color></b>. Your best star result is saved.",
            "Result cards show <b><#E09500>COMPLETION</color></b>, <b><#E09500>EFFICIENCY</color></b>, and <b><#E09500>STRENGTH</color></b>. Your best stars are saved."
        },
        {
            "Review the <b><#E09500>MATERIAL RECEIPT</color></b> to compare your total cost with the contract budget. You can also check the bridge's peak stress.",
            "Use the <b><#E09500>MATERIAL RECEIPT</color></b> to compare total cost with the budget. Check peak bridge stress too."
        },
        {
            "Choose <b><#E09500>RETRY</color></b> to improve the design, or <b><#E09500>SAVE & CONTINUE</color></b> to record your completed Training Beam Bridge in the Almanac. Your training contract will be ready to turn in.",
            "<b><#E09500>RETRY</color></b> improves your design. <b><#E09500>SAVE & CONTINUE</color></b> records the Training Beam Bridge in the Almanac and readies the contract for turn-in."
        },

        // Book and shop walkthroughs. Preserve what each archive/tab records.
        {
            "A new <b><#E09500>MATERIALS TAB</color></b> is available. Select it to inspect your discovered materials.",
            "Select the new <b><#E09500>MATERIALS TAB</color></b> to inspect discovered materials."
        },
        {
            "The material archive records useful properties and grows whenever you discover a new construction material.",
            "The archive records material properties and adds each new material you discover."
        },
        {
            "A new <b><#E09500>LESSONS TAB</color></b> is available. Select it to review what you have learned.",
            "Select the new <b><#E09500>LESSONS TAB</color></b> to review what you've learned."
        },
        {
            "New engineering lessons are added here as you complete activities and discover new concepts.",
            "Engineering lessons are added as you complete activities and discover concepts."
        },
        {
            "A new <b><#E09500>CONTRACTS TAB</color></b> is available. Select it to review your completed jobs.",
            "Select the new <b><#E09500>CONTRACTS TAB</color></b> to review completed jobs."
        },
        {
            "Completed contracts are recorded here with their client, rewards, and saved bridge photo.",
            "Completed contracts record the client, rewards, and saved bridge photo."
        },
        {
            "New <b><#E09500>DISCOVERIES</color></b> and completed <b><#E09500>PROJECTS</color></b> will be added here throughout your journey.",
            "Your new <b><#E09500>DISCOVERIES</color></b> and completed <b><#E09500>PROJECTS</color></b> are recorded here."
        },
        {
            "Welcome to the <b><#E09500>SHOP</color></b>. You can buy equipment and supplies here using your coins.",
            "Buy equipment and supplies at the <b><#E09500>SHOP</color></b> using your coins."
        },
        {
            "Use the <b><#E09500>CATEGORY TABS</color></b> to browse builders, materials, tools, decorations, vehicles, and bundles.",
            "Browse builders, materials, tools, decorations, vehicles, and bundles with the <b><#E09500>CATEGORY TABS</color></b>."
        },
        {
            "Each item card shows what the item is for. Select an item to review its <b><#E09500>PRICE</color></b> before buying.",
            "Item cards explain their use. Select one to check its <b><#E09500>PRICE</color></b> before buying."
        },
        {
            "Keep an eye on your <b><#E09500>COIN BALANCE</color></b>. A purchase is only completed after you confirm it.",
            "Check your <b><#E09500>COIN BALANCE</color></b>. Purchases complete only after confirmation."
        },
        {
            "That is everything. Press the <b><#E09500>CLOSE BUTTON</color></b> when you are finished shopping.",
            "Press the <b><#E09500>CLOSE BUTTON</color></b> when you're done shopping."
        },
        {
            "Each card shows its <b><#E09500>PRICE</color></b>. Your tutorial Safety Vest is <b>FREE</b>, even offline. Claim it before shopping for other items.",
            "Cards show the <b><#E09500>PRICE</color></b>. Claim your <b>FREE</b> tutorial Safety Vest, even offline, before shopping for other items."
        },
        {
            "Claim your <b><#E09500>FREE SAFETY VEST</color></b> in Accessories. Confirm to continue. This tutorial item costs no Coins and works offline.",
            "Claim your <b><#E09500>FREE SAFETY VEST</color></b> in Accessories, then confirm. It costs no Coins and works offline."
        }
    };

    public static string Compact(string authoredMessage)
    {
        if (authoredMessage == null) return string.Empty;
        return Replacements.TryGetValue(authoredMessage, out string compact) ? compact : authoredMessage;
    }
}
