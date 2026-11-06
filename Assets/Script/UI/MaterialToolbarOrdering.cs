using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Orders evaluated material cells without changing their state or bindings.</summary>
internal static class MaterialToolbarOrdering
{
    private sealed class Entry
    {
        internal MaterialButtonTrigger button;
        internal Transform cell;
        internal int authoredIndex;
    }

    internal static int Category(BridgeMaterialSO material)
    {
        if (material == null) return 4;
        if (material.isRoad) return 0;
        if (material.isPier) return 2;
        if (material.isRope) return 3;
        return 1;
    }

    internal static Transform ResolveCell(MaterialButtonTrigger button)
    {
        Transform cell = button.parentWrapper != null ? button.parentWrapper.transform : button.transform;
        if (cell != button.transform && !button.transform.IsChildOf(cell)) return null;
        // Older shared toolbars keep the input object inside an unassigned
        // wrapper. Move the direct layout child rather than sorting its icon.
        while (cell.parent != null && cell.parent.GetComponent<LayoutGroup>() == null) cell = cell.parent;
        return cell.parent != null ? cell : null;
    }

    internal static void Apply(MaterialButtonTrigger[] buttons, Scene scene, bool resetScroll)
    {
        var groups = new Dictionary<Transform, List<Entry>>();
        var cells = new HashSet<Transform>();
        foreach (MaterialButtonTrigger button in buttons)
        {
            if (button == null || button.gameObject.scene != scene) continue;
            Transform cell = ResolveCell(button);
            if (cell == null) continue;
            Transform parent = cell.parent;
            if (parent == null || parent.GetComponent<LayoutGroup>() == null || !cells.Add(cell)) continue;
            if (!groups.TryGetValue(parent, out List<Entry> group)) groups[parent] = group = new List<Entry>();
            // Capture every baseline before any sibling moves, including inactive cells.
            group.Add(new Entry { button = button, cell = cell, authoredIndex = button.GetToolbarAuthoredIndex(cell) });
        }

        foreach (var pair in groups)
        {
            Transform parent = pair.Key;
            List<Entry> group = pair.Value;
            var slots = new HashSet<Transform>();
            foreach (Entry entry in group) slots.Add(entry.cell);
            var order = new Transform[parent.childCount];
            for (int i = 0; i < order.Length; i++) order[i] = parent.GetChild(i);
            group.Sort((a, b) =>
            {
                int availability = b.button.IsAvailableForSelection.CompareTo(a.button.IsAvailableForSelection);
                if (availability != 0) return availability;
                int category = Category(a.button.buttonMaterial).CompareTo(Category(b.button.buttonMaterial));
                return category != 0 ? category : a.authoredIndex.CompareTo(b.authoredIndex);
            });
            int materialIndex = 0;
            for (int i = 0; i < order.Length; i++)
                if (slots.Contains(order[i])) order[i] = group[materialIndex++].cell;
            bool changed = false;
            for (int i = 0; i < order.Length; i++)
                if (order[i].GetSiblingIndex() != i) { order[i].SetSiblingIndex(i); changed = true; }
            if (changed && parent is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
            if (resetScroll)
            {
                ScrollRect scroll = parent.GetComponentInParent<ScrollRect>(true);
                if (scroll != null && scroll.content == parent) scroll.horizontalNormalizedPosition = 0f;
            }
        }
    }
}
