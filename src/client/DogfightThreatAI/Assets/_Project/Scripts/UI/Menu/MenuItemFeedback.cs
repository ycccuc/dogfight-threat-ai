using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Menu
{
    /// <summary>One focus treatment for both mouse hover and keyboard navigation.</summary>
    public sealed class MenuItemFeedback : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
    {
        Text _label;
        Graphic _mark;
        Action _onFocus;
        Button _button;

        internal void Configure(Text label, Graphic mark, Action onFocus)
        {
            _label = label;
            _mark = mark;
            _onFocus = onFocus;
            _button = GetComponent<Button>();
            Show(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_button.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData) { Show(true); _onFocus?.Invoke(); }
        public void OnDeselect(BaseEventData eventData) => Show(false);

        void Show(bool selected)
        {
            if (_label != null) _label.color = selected ? MenuUi.Ivory : MenuUi.Muted;
            if (_mark != null) _mark.color = selected ? MenuUi.Ivory : Color.clear;
        }
    }
}
