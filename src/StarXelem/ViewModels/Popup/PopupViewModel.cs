using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace StarXelem.ViewModels.Popup;

public class ShowPopupMessage {
    public readonly ViewModelBase? ViewModel;
    public readonly bool ShowCloseButton;
    public readonly Action? OnClose;
    
    public ShowPopupMessage(bool showCloseButton = true, Action? onClose = null, ViewModelBase? viewModel = null)
    {
        ViewModel = viewModel;
        ShowCloseButton = showCloseButton;
        OnClose = onClose;
    }
}

public class ClosePopupMessage
{
    
}

public partial class PopupViewModel : ViewModelBase, IRecipient<ShowPopupMessage>, IRecipient<ClosePopupMessage>
{
    [ObservableProperty] private bool _isVisible = false;
    [ObservableProperty] private bool _isCloseButtonVisible = true;
    [ObservableProperty] private ViewModelBase? _ContentViewModel;
    
    private ShowPopupMessage? _message;

    public PopupViewModel()
    {
        WeakReferenceMessenger.Default.Register<ShowPopupMessage>(this);
        WeakReferenceMessenger.Default.Register<ClosePopupMessage>(this);
    }

    /// <summary>
    /// Exécuté à chaque réception d'un message demandant à afficher la popup
    /// </summary>
    /// <param name="message"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Receive(ShowPopupMessage message)
    {
        Debug.Assert(_message == null); // The popup can only be shown if not already shown
        _message = message;
        IsCloseButtonVisible = _message.ShowCloseButton;
        IsVisible = true;
        ContentViewModel = _message.ViewModel;
    }

    [RelayCommand]
    public void Close()
    {
        var message = _message;
        
        _message = null;
        IsVisible = false;
        message?.OnClose?.Invoke();
    }

    /// <summary>
    /// Méthode quand on reçoit un message demandant de fermer la popup
    /// </summary>
    /// <param name="message"></param>
    public void Receive(ClosePopupMessage message)
    {
        Close();
    }
}