using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Moves a social launcher or drawer inside its canvas and persists on release.</summary>
public sealed class SocialOverlayDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform target;
    private RectTransform bounds;
    private float margin;
    private Action<Vector2> committed;
    private Vector2 pointerOffset;
    private bool moved;

    public void Configure(RectTransform targetRect, RectTransform boundsRect, float safeMargin,
        Action<Vector2> onCommitted)
    {
        target = targetRect;
        bounds = boundsRect;
        margin = safeMargin;
        committed = onCommitted;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        moved = false;
        if (!TryLocal(eventData, out Vector2 local)) return;
        pointerOffset = local - target.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!TryLocal(eventData, out Vector2 local)) return;
        Vector2 desired = local - pointerOffset;
        float halfW = target.rect.width * 0.5f;
        float halfH = target.rect.height * 0.5f;
        Rect rect = bounds.rect;
        desired.x = Mathf.Clamp(desired.x, rect.xMin + halfW + margin, rect.xMax - halfW - margin);
        desired.y = Mathf.Clamp(desired.y, rect.yMin + halfH + margin, rect.yMax - halfH - margin);
        if ((desired - target.anchoredPosition).sqrMagnitude > 1f) moved = true;
        target.anchoredPosition = desired;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (target != null) committed?.Invoke(target.anchoredPosition);
    }

    public bool ConsumeDrag()
    {
        bool result = moved;
        moved = false;
        return result;
    }

    private bool TryLocal(PointerEventData eventData, out Vector2 local)
    {
        if (target == null || bounds == null)
        {
            local = Vector2.zero;
            return false;
        }
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            bounds, eventData.position, eventData.pressEventCamera, out local);
    }
}
