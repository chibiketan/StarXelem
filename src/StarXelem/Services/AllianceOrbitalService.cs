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
    /// Modèle pour la réponse d'erreur 401 (statusCode + statusMessage).
    /// </summary>
    private record ApiErrorResponse(int StatusCode, string StatusMessage);
}
