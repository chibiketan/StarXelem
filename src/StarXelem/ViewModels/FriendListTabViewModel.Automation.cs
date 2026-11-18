using Avalonia.Threading;

namespace StarXelem.ViewModels;

public partial class FriendListTabViewModel
{
    private static readonly TimeSpan AutomationConnectionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lance un chargement de la liste d'amis sans intervention (option <c>--load-friends</c>).
    /// À appeler depuis le thread d'UI.
    /// </summary>
    /// <returns><c>true</c> si le chargement a eu lieu, <c>false</c> si la connexion au jeu n'est pas disponible.</returns>
    public async Task<bool> RunAutomatedLoadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();

        var deadline = DateTime.UtcNow + AutomationConnectionTimeout;
        while (!LoadFriendListCommand.CanExecute(null))
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(500);
        }

        await LoadFriendListCommand.ExecuteAsync(null);
        return true;
    }
}
