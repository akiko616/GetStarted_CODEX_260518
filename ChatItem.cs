using TMPro;
using TRAINEE;
using UnityEngine;

public class ChatItem : UIScrollViewItem<ChatData>
{
    [SerializeField] private TMP_Text message;
    [SerializeField] private TMP_Text timeText;

    public override void SetData(ChatData data)
    {
        this.message.text = data.message;
        timeText.text = data.time.ToLocalTime().ToString("HH:mm:ss");
    }
}
