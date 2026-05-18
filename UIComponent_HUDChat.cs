using System;
using System.Collections.Generic;
using TRAINEE;
using UnityEngine;

public class UIComponent_HUDChat : UiComponent_ScrollView
{
    public ChatChannel CurrentChannel => _currentChannel;

    public event Action<ChatChannel> OnChannelChanged;

    private GameObject _minePrefab;
    private GameObject _otherPrefab;


    private ChatChannel _currentChannel = ChatChannel.Channel1;

    private Dictionary<ChatChannel, List<ChatData>> _channelMessages;

    public override void Init()
    {
        base.Init();

        _minePrefab = LdResources.Load<GameObject>("6.Prefab/RadioText_Mine");
        _otherPrefab = LdResources.Load<GameObject>("6.Prefab/RadioText_Other");

        _channelMessages = new();
    }
    public void AddMessage(ChatData data)
    {
        if (!_channelMessages.TryGetValue(data.channel, out var list))
        {
            list = new List<ChatData>();
            _channelMessages[data.channel] = list;
        }

        list.Add(data);

        if (data.channel == _currentChannel)
        {
            CreateItem(data);
            UpdateScroll();
        }
    }

    GameObject CreateItem(ChatData data)
    {
        GameObject targetPrefab = null;

        switch (data.chatType)
        {
            case ChatType.Mine:
                targetPrefab = _minePrefab;
                break;

            case ChatType.Other:
                targetPrefab = _otherPrefab;
                break;

            default:
                Debug.LogError($"{data.chatType}");
                return null;
        } 

        GameObject go = AddItem(targetPrefab);

        if (go.TryGetComponent(out UIScrollViewItem<ChatData> item))
        {
            item.SetData(data);
        }

        return go;
    }


    public void ChangeChannel(ChatChannel channel)
    {
        if (_currentChannel == channel) return;

        _currentChannel = channel;

        ReturnAllToPool();

        if (_channelMessages.TryGetValue(channel, out var messages))
        {   
            int startIndex = Mathf.Max(0, messages.Count - maxItemCount);

            for (int i = startIndex; i < messages.Count; i++)
            {
                CreateItem(messages[i]);
            }
        }

        UpdateScroll();
        OnChannelChanged?.Invoke(_currentChannel);
    }

    public void ClearAll()
    {
        base.Clear();
        _channelMessages.Clear();
    }

}