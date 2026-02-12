using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMV.App.ViewModels;

public enum ChatRole
{
    User,
    Assistant,
    Sql
}

public class ChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isTyping;

    public ChatRole Role { get; set; }
    
    public string Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                OnPropertyChanged();
            }
        }
    }
    
    public string? SqlQuery { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<dynamic>? Data { get; set; }

    public bool IsTyping
    {
        get => _isTyping;
        set
        {
            if (_isTyping != value)
            {
                _isTyping = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool HasData => Data != null && Data.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}