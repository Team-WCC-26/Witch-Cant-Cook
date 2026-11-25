using System.Collections.Generic;
using UnityEngine;

// The row lays out the slots. Only the card inside each slot moves during a slide.
internal sealed class RecipeOrderSlideLayout
{
    private const float EntryPadding = 16f;

    private sealed class Slot
    {
        public RecipeOrderCardUI Card;
        public RectTransform Frame;
        public RectTransform Visual;
        public Vector2 Start;
        public float Elapsed;
        public bool Moving;
    }

    private readonly Slot[] slots;
    private readonly Dictionary<long, Vector3> previousPositions = new();
    private float duration;
    private float horizontalDuration;
    private float entryDelay;
    private bool hasMotion;

    public RecipeOrderSlideLayout(RectTransform row, IReadOnlyList<RecipeOrderCardUI> cards)
    {
        slots = new Slot[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            RecipeOrderCardUI card = cards[i];
            if (card == null || card.transform is not RectTransform visual || visual.parent != row)
                continue;

            int sibling = visual.GetSiblingIndex();
            var frame = new GameObject($"{card.name} Slot", typeof(RectTransform)).GetComponent<RectTransform>();
            frame.SetParent(row, false);
            frame.SetSiblingIndex(sibling);
            frame.anchorMin = visual.anchorMin;
            frame.anchorMax = visual.anchorMax;
            frame.pivot = visual.pivot;
            frame.anchoredPosition3D = visual.anchoredPosition3D;
            frame.sizeDelta = visual.rect.size;
            frame.localScale = visual.localScale;
            frame.gameObject.SetActive(card.gameObject.activeSelf);

            visual.SetParent(frame, false);
            visual.anchorMin = visual.anchorMax = frame.pivot;
            visual.anchoredPosition3D = Vector3.zero;
            visual.localScale = Vector3.one;
            slots[i] = new Slot { Card = card, Frame = frame, Visual = visual };
        }
    }

    public void CaptureVisiblePositions()
    {
        previousPositions.Clear();
        foreach (Slot slot in slots)
        {
            if (slot != null && slot.Card.OrderId >= 0 && slot.Visual.gameObject.activeInHierarchy)
                previousPositions[slot.Card.OrderId] = slot.Visual.position;
        }
    }

    public void SetSlotVisible(int index, bool visible)
    {
        Slot slot = slots[index];
        if (slot == null || slot.Frame.gameObject.activeSelf == visible) return;
        slot.Frame.gameObject.SetActive(visible);
        if (!visible) Reset(slot);
    }

    public void FitSlotToCard(int index)
    {
        Slot slot = slots[index];
        if (slot != null) slot.Frame.sizeDelta = slot.Visual.rect.size;
    }

    public void BeginSlides(RectTransform hud, float horizontalSeconds, float delaySeconds, float dropSeconds)
    {
        horizontalDuration = Mathf.Max(0f, horizontalSeconds);
        duration = Mathf.Max(0f, dropSeconds);
        hasMotion = false;
        bool hasHorizontalSlide = false;
        bool hasVerticalSlide = false;
        foreach (Slot slot in slots)
        {
            if (slot == null || !slot.Visual.gameObject.activeInHierarchy) continue;

            Vector2 offset;
            if (previousPositions.TryGetValue(slot.Card.OrderId, out Vector3 previous))
                offset = slot.Frame.InverseTransformPoint(previous);
            else
                offset = new Vector2(0f, EntryDistance(hud, slot));

            hasHorizontalSlide |= Mathf.Abs(offset.x) > .01f;
            hasVerticalSlide |= Mathf.Abs(offset.y) > .01f;
            slot.Start = offset;
            slot.Elapsed = 0f;
            slot.Moving = offset.sqrMagnitude > .001f;
            slot.Visual.anchoredPosition = slot.Moving ? offset : Vector2.zero;
            hasMotion |= slot.Moving;
        }

        // The waiting time starts after the existing cards have cleared the column.
        entryDelay = hasHorizontalSlide && hasVerticalSlide
            ? horizontalDuration + Mathf.Max(0f, delaySeconds)
            : 0f;
        Advance(0f);
    }

    public void Advance(float delta)
    {
        if (!hasMotion) return;
        hasMotion = false;
        foreach (Slot slot in slots)
        {
            if (slot == null || !slot.Moving) continue;

            slot.Elapsed += delta;
            float x = slot.Start.x * EaseRemaining(slot.Elapsed, horizontalDuration);
            float y = slot.Start.y * EaseRemaining(slot.Elapsed - entryDelay, duration);
            var offset = new Vector2(x, y);
            if (offset.sqrMagnitude <= .001f)
            {
                Reset(slot);
                continue;
            }

            slot.Visual.anchoredPosition = offset;
            hasMotion = true;
        }
    }

    private static float EaseRemaining(float elapsed, float duration)
    {
        if (elapsed < 0f) return 1f;
        if (duration <= 0f) return 0f;

        float remaining = 1f - Mathf.Clamp01(elapsed / duration);
        return remaining * remaining * remaining;
    }

    public void Clear()
    {
        hasMotion = false;
        previousPositions.Clear();
        foreach (Slot slot in slots)
        {
            if (slot == null) continue;
            Reset(slot);
            slot.Frame.gameObject.SetActive(false);
        }
    }

    private static void Reset(Slot slot)
    {
        slot.Moving = false;
        slot.Visual.anchoredPosition = Vector2.zero;
    }

    private static float EntryDistance(RectTransform hud, Slot slot)
    {
        if (hud == null) return slot.Visual.rect.height + EntryPadding;
        var top = hud.TransformPoint(new Vector3(hud.rect.center.x, hud.rect.yMax, 0f));
        return Mathf.Max(0f, slot.Frame.InverseTransformPoint(top).y - slot.Visual.rect.yMin) + EntryPadding;
    }
}
