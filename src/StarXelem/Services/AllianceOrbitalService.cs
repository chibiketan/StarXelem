using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Implémente l'appel à l'API Alliance Orbital pour récupérer les profils utilisateurs.
/// Le jeton JWT est lu depuis ISettingsService (clé "ApiKey").
/// </summary>
public class AllianceOrbitalService : IAllianceOrbitalService
{
    private const string ApiBaseUrl = "https://alliance-orbital.eu/api/external";
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AllianceOrbitalService> _logger;

    public AllianceOrbitalService(ISettingsService settingsService, ILogger<AllianceOrbitalService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Appel GET vers /api/external/profil avec le token JWT des settings.
    /// Gère les codes HTTP retournés par l'API :
    /// - 200 : désérialise et retourne la liste de profils
    /// - 401 : lève une exception avec le message d'erreur de l'API (token manquant/invalide/expiré)
    /// - 404 : retourne une liste vide (aucun profil n'existe pour ce compte)
    /// - autres codes : lève une exception générique
    /// </summary>
    public async Task<List<ProfilItem>> GetProfilesAsync()
    {
        // Récupération du jeton JWT stocké dans le registre Windows via les settings
        var token = await _settingsService.GetAsync("ApiKey");

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Clé API manquante pour Alliance Orbital.");
            throw new InvalidOperationException("Clé API non configurée. Veuillez la définir dans les paramètres.");
        }

        // Création d'un HttpClient avec le header Authorization Bearer
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(ApiBaseUrl + "/profil");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de connexion vers Alliance Orbital.");
            throw new InvalidOperationException("Impossible de joindre le service Alliance Orbital.", ex);
        }

        // Code 200 : retourne la liste de profils désérialisée
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var profils = System.Text.Json.JsonSerializer.Deserialize<List<ProfilItem>>(json, options);
            _logger.LogInformation("Profils chargés depuis Alliance Orbital : {Count}", profils?.Count ?? 0);
            return profils ?? new List<ProfilItem>();
        }

        // Code 401 : token invalide ou expiré, retourne le message d'erreur de l'API
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Token invalide ou expiré.";
            _logger.LogWarning("Erreur 401 Alliance Orbital : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 404 : aucun profil n'existe pour ce compte
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Aucun profil trouvé pour ce compte Alliance Orbital.");
            return new List<ProfilItem>();
        }

        var statusText = ((int)response.StatusCode).ToString();
        _logger.LogError("Erreur HTTP {StatusCode} depuis Alliance Orbital.", statusText);
        throw new InvalidOperationException($"Erreur serveur ({statusText}). Réessayez plus tard.");
    }

    /// <summary>
    /// Synchronise les blueprints possédés pour un profil RSI via POST /api/external/blueprints.
    /// Gère les codes HTTP : 200 (succès), 401/403 (token/profil invalide), 400 (validation).
    /// </summary>
    public async Task<SyncResult> SyncBlueprintsAsync(string rsiProfilGuid, List<string> blueprintIds)
    {
        var token = await _settingsService.GetAsync("ApiKey");

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Clé API manquante pour la synchronisation des blueprints.");
            throw new InvalidOperationException("Clé API non configurée. Veuillez la définir dans les paramètres.");
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Construction du body de requête selon le schéma BlueprintSyncRequest
        var requestBody = new { rsiProfilGuid, blueprintGuids = blueprintIds };
        var jsonBody = System.Text.Json.JsonSerializer.Serialize(requestBody);

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(jsonBody, null, "application/json");
            response = await httpClient.PostAsync(ApiBaseUrl + "/blueprints", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de connexion lors de la synchronisation des blueprints.");
            throw new InvalidOperationException("Impossible de joindre le service Alliance Orbital.", ex);
        }

        // Code 200 : retourne le résultat de synchronisation
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var result = System.Text.Json.JsonSerializer.Deserialize<SyncResult>(json, options);
            _logger.LogInformation("Synchronisation blueprints réussie : {Received} reçus, {Matched} reconnus, {Updated} mis à jour",
                result?.Received ?? 0, result?.Matched ?? 0, result?.Updated ?? 0);
            return result ?? new SyncResult();
        }

        // Code 401 : token invalide ou expiré
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Token invalide ou expiré.";
            _logger.LogWarning("Erreur 401 synchronisation blueprints : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 403 : le profil RSI n'appartient pas au compte de la clé API
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Ce profil RSI n'appartient pas à votre compte.";
            _logger.LogWarning("Erreur 403 synchronisation blueprints : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 400 : body invalide (champs manquants ou limite dépassée)
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Données invalides.";
            _logger.LogWarning("Erreur 400 synchronisation blueprints : {Message}", message);
            throw new InvalidOperationException(message);
        }

        var statusText = ((int)response.StatusCode).ToString();
        _logger.LogError("Erreur HTTP {StatusCode} lors de la synchronisation des blueprints.", statusText);
        throw new InvalidOperationException($"Erreur serveur ({statusText}). Réessayez plus tard.");
    }

    /// <summary>
    /// Synchronise les vaisseaux possédés pour un profil RSI via POST /api/external/fleet.
    /// Les vaisseaux sont regroupés par classe (EntityClassGuid) avec leur compte.
    /// Gère les codes HTTP : 200 (succès), 401/403 (token/profil invalide), 400 (validation).
    /// </summary>
    public async Task<SyncResult> SyncFleetAsync(string rsiProfilGuid, List<FleetSyncItem> fleetItems)
    {
        var token = await _settingsService.GetAsync("ApiKey");

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Clé API manquante pour la synchronisation de flotte.");
            throw new InvalidOperationException("Clé API non configurée. Veuillez la définir dans les paramètres.");
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Construction du body de requête : { rsiProfilGuid, spaceships = List<FleetSyncItem> }
        var requestBody = new { rsiProfilGuid, spaceships = fleetItems };
        var jsonBody = System.Text.Json.JsonSerializer.Serialize(requestBody);

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(jsonBody, null, "application/json");
            response = await httpClient.PostAsync(ApiBaseUrl + "/spaceships", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de connexion lors de la synchronisation de flotte.");
            throw new InvalidOperationException("Impossible de joindre le service Alliance Orbital.", ex);
        }

        // Code 200 : retourne le résultat de synchronisation
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var result = System.Text.Json.JsonSerializer.Deserialize<SyncResult>(json, options);
            _logger.LogInformation("Synchronisation flotte réussie : {ItemCount} classes de vaisseaux synchronisées",
                fleetItems.Count);
            return result ?? new SyncResult();
        }

        // Code 401 : token invalide ou expiré
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Token invalide ou expiré.";
            _logger.LogWarning("Erreur 401 synchronisation flotte : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 403 : le profil RSI n'appartient pas au compte de la clé API
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Ce profil RSI n'appartient pas à votre compte.";
            _logger.LogWarning("Erreur 403 synchronisation flotte : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 400 : body invalide (champs manquants ou limite dépassée)
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Données invalides.";
            _logger.LogWarning("Erreur 400 synchronisation flotte : {Message}", message);
            throw new InvalidOperationException(message);
        }

        var statusText = ((int)response.StatusCode).ToString();
        _logger.LogError("Erreur HTTP {StatusCode} lors de la synchronisation de flotte.", statusText);
        throw new InvalidOperationException($"Erreur serveur ({statusText}). Réessayez plus tard.");
    }

    /// <summary>
    /// Synchronise les objets possédés pour un profil RSI via POST /api/external/items.
    /// Les objets sont regroupés par type (itemGuid) avec leur quantité totale.
    /// Gère les codes HTTP : 200 (succès), 401/403 (token/profil invalide), 400 (validation).
    /// </summary>
    public async Task<SyncResult> SyncItemsAsync(string rsiProfilGuid, List<ItemSyncItem> items)
    {
        var token = await _settingsService.GetAsync("ApiKey");

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Clé API manquante pour la synchronisation d'objets.");
            throw new InvalidOperationException("Clé API non configurée. Veuillez la définir dans les paramètres.");
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Construction du body de requête : { rsiProfilGuid, items = List<ItemSyncItem> }
        var requestBody = new { rsiProfilGuid, items };
        var jsonBody = System.Text.Json.JsonSerializer.Serialize(requestBody);

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(jsonBody, null, "application/json");
            response = await httpClient.PostAsync(ApiBaseUrl + "/items", content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur de connexion lors de la synchronisation d'objets.");
            throw new InvalidOperationException("Impossible de joindre le service Alliance Orbital.", ex);
        }

        // Code 200 : retourne le résultat de synchronisation
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };

            var result = System.Text.Json.JsonSerializer.Deserialize<SyncResult>(json, options);
            _logger.LogInformation("Synchronisation d'objets réussie : {ItemCount} types d'objets synchronisés",
                items.Count);
            return result ?? new SyncResult();
        }

        // Code 401 : token invalide ou expiré
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Token invalide ou expiré.";
            _logger.LogWarning("Erreur 401 synchronisation d'objets : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 403 : le profil RSI n'appartient pas au compte de la clé API
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Ce profil RSI n'appartient pas à votre compte.";
            _logger.LogWarning("Erreur 403 synchronisation d'objets : {Message}", message);
            throw new InvalidOperationException(message);
        }

        // Code 400 : body invalide (champs manquants ou limite dépassée)
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var error = System.Text.Json.JsonSerializer.Deserialize<ApiErrorResponse>(json, options);
            var message = error?.StatusMessage ?? "Données invalides.";
            _logger.LogWarning("Erreur 400 synchronisation d'objets : {Message}", message);
            throw new InvalidOperationException(message);
        }

        var statusText = ((int)response.StatusCode).ToString();
        _logger.LogError("Erreur HTTP {StatusCode} lors de la synchronisation d'objets.", statusText);
        throw new InvalidOperationException($"Erreur serveur ({statusText}). Réessayez plus tard.");
    }

    /// <summary>
    /// Modèle pour la réponse d'erreur (statusCode + statusMessage).
    /// </summary>
    private record ApiErrorResponse(int StatusCode, string StatusMessage);
}
