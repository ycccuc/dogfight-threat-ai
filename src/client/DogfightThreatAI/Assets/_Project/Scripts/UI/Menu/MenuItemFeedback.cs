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

        // 连接按钮文字、焦点标记与说明回调。
        internal void Configure(Text label, Graphic mark, Action onFocus)
        {
            _label = label;
            _mark = mark;
            _onFocus = onFocus;
            _button = GetComponent<Button>();
            Show(false);
        }

        // 鼠标悬停可操作按钮时，同步键盘导航焦点。
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_button.IsInteractable() && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        // 突出当前选项，并更新该选项的说明。
        public void OnSelect(BaseEventData eventData)
        {
            Show(true);
            _onFocus?.Invoke();
        }

        // 撤销失去焦点选项的突出效果。
        public void OnDeselect(BaseEventData eventData)
        {
            Show(false);
        }

        // 统一设置菜单文字和焦点标记的选中外观。
        void Show(bool selected)
        {
            if (_label != null)
            {
                _label.color = selected ? MenuUi.Ivory : MenuUi.Muted;
            }
            if (_mark != null)
            {
                _mark.color = selected ? MenuUi.Ivory : Color.clear;
            }
        }
    }
}
