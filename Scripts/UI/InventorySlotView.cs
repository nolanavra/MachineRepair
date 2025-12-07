using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventorySlotView : MonoBehaviour
{
    [Header("Slot Visuals")]
    [SerializeField] private Image slotBackground;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI quantityText;

    public void SetData(Sprite slotSprite, Sprite itemSprite, int quantity)
    {
        if (slotBackground != null)
        {
            if (slotSprite != null)
            {
                slotBackground.sprite = slotSprite;
            }

            slotBackground.enabled = slotBackground.sprite != null;
        }

        if (itemIcon != null)
        {
            itemIcon.sprite = itemSprite;
            itemIcon.enabled = itemSprite != null;
        }

        if (quantityText != null)
        {
            bool showQuantity = quantity > 1;
            quantityText.text = showQuantity ? quantity.ToString() : string.Empty;
            quantityText.gameObject.SetActive(showQuantity);
        }
    }
}
