using System.Timers;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.App.Services;

/// <summary>
/// Implémentation singleton du service de session.
/// Gère l'état d'authentification et le timeout d'inactivité (30 minutes).
/// </summary>
public class SessionService : ISessionService, IDisposable
{
    private User? _currentUser;
    private System.Timers.Timer? _inactivityTimer;
    private bool _disposed;

    /// <summary>
    /// Durée d'inactivité avant expiration de session (30 minutes).
    /// </summary>
    private const int InactivityTimeoutMinutes = 30;

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;
    public bool IsAdmin => _currentUser?.Role == UserRole.Admin;

    public event EventHandler? SessionExpired;
    public event EventHandler? SessionChanged;

    public bool HasRole(UserRole role)
    {
        return _currentUser?.Role == role;
    }

    public void Login(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        _currentUser = user;
        StartInactivityTimer();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Logout()
    {
        _currentUser = null;
        StopInactivityTimer();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetInactivityTimer()
    {
        if (_inactivityTimer != null && IsAuthenticated)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }
    }

    private void StartInactivityTimer()
    {
        StopInactivityTimer();
        _inactivityTimer = new System.Timers.Timer(InactivityTimeoutMinutes * 60 * 1000);
        _inactivityTimer.Elapsed += OnInactivityTimerElapsed;
        _inactivityTimer.AutoReset = false;
        _inactivityTimer.Start();
    }

    private void StopInactivityTimer()
    {
        if (_inactivityTimer != null)
        {
            _inactivityTimer.Elapsed -= OnInactivityTimerElapsed;
            _inactivityTimer.Stop();
            _inactivityTimer.Dispose();
            _inactivityTimer = null;
        }
    }

    private void OnInactivityTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // La session a expiré
        _currentUser = null;
        StopInactivityTimer();
        SessionExpired?.Invoke(this, EventArgs.Empty);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopInactivityTimer();
            _disposed = true;
        }
    }
}
