using System.Text.RegularExpressions;
using StarBreaker.DataCoreGenerated;
using Microsoft.Extensions.Logging;

namespace StarXelem.Services.Mining;

/// <summary>
/// Implémentation par défaut de <see cref="IMineralSignatureExtractor"/>.
/// Reprend la logique historiquement inline dans <c>ExtractionTabViewModel.UpdateLocalisationAsync</c>
/// (cf. Documents/Datafiles/MineableRocks.md pour le détail des pièges évités : entités génériques par
/// classe spectrale d'astéroïde, entités de test, minéraux secondaires/traces dans compositionArray),
/// étendue pour capturer aussi les rochers minés à la main (FPS) et au véhicule terrestre (GroundVehicle),
/// dont la signature radar est générique par catégorie (3000/4000) et non unique par minéral — voir
/// <see cref="MiningKind"/>.
/// </summary>
public class MineralSignatureExtractor : IMineralSignatureExtractor
{
    private static readonly string[] ExcludedWords = { "deposit", "ore", "raw", "items", "commodities", "r" };
    private static readonly Regex RarityRegex = new(@"MineableRock_(?:Asteroid|Surface)(Legendary|Epic|Rare|Uncommon|Common)_", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IP4kService _p4kService;
    private readonly ILogger<MineralSignatureExtractor> _logger;

    public MineralSignatureExtractor(IP4kService p4kService, ILogger<MineralSignatureExtractor> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MineralBaseSignature>> ExtractAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<MineralBaseSignature>();
        var seenKeys = new HashSet<(string Key, MiningKind Kind)>();

        await foreach (var entityDefinition in _p4kService.GetAllEntityClassDefinition(0).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Seuls les rochers minables "canoniques" (mineablerock_asteroid{rarete}_{mineral},
            // mineablerock_surface{rarete}_{mineral}, mineablerock_fps_{mineral}, mineablerock_groundvehicle_{mineral})
            // portent une vraie signature radar. Les rochers génériques par classe spectrale d'astéroïde
            // (AsteroidCTypeMineableRock, AsteroidSTypeMineableRock, ...) partagent tous une signature générique de
            // type d'astéroïde (ex: 4720) pour un mix de minéraux, et polluent la map si on les laisse passer.
            // Les entités de test/gabarit/placeholder doivent aussi être ignorées.
            // RecordName contient le nom complet de la balise racine du fichier XML, ex:
            // "EntityClassDefinition.MineableRock_AsteroidCommon_Aluminum" et non juste le nom du rocher,
            // d'où l'utilisation de Contains(".MineableRock_") plutôt que StartsWith.
            var recordName = entityDefinition.RecordName;
            if (string.IsNullOrEmpty(recordName)
                || !recordName.Contains(".MineableRock_", StringComparison.OrdinalIgnoreCase)
                || recordName.Contains("test", StringComparison.OrdinalIgnoreCase)
                || recordName.Contains("template", StringComparison.OrdinalIgnoreCase)
                || recordName.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
                continue;

            var entityType = entityDefinition.Data as EntityClassDefinition;
            if (!(entityType?.Components.OfType<MineableParams>().Any() ?? false) || !entityType.Components.OfType<SSCSignatureSystemParams>().Any())
                continue;

            var entityDefinitionWithDepth = await _p4kService.EnsureRecordsDepthAsync([entityDefinition], 3);
            entityType = (EntityClassDefinition)entityDefinitionWithDepth[0].Data;
            var mineableParams = entityType.Components.OfType<MineableParams>().First();
            var signatureParams = entityType.Components.OfType<SSCSignatureSystemParams>().First();
            // Extract radar signature (index 4 = mineral channel)
            // baseSignatureParams is typed as SSCSignatureParamsBase but the actual runtime type is SSCSignatureSystemBaseSignatureParams
            var baseSigParams = signatureParams.radarProperties?.baseSignatureParams as SSCSignatureSystemBaseSignatureParams;
            if (baseSigParams?.signatures == null || baseSigParams.signatures.Length < 5)
                continue;

            var signatureValue = (int)Math.Round(baseSigParams.signatures[4]);

            // Certaines entités du p4k portent un canal minéral à 0 (variantes non finalisées / contenu
            // désactivé) : elles n'ont aucune valeur exploitable et doivent être exclues.
            if (signatureValue <= 0)
                continue;

            // Contrairement au minage vaisseau/surface (signature unique par minéral), le minage à la main
            // (FPS) et au véhicule terrestre (GroundVehicle) utilise une signature générique PAR CATÉGORIE
            // (3000 pour tout minéral FPS, 4000 pour tout minéral GroundVehicle, cf. Documents/Datafiles/MineableRocks.md).
            // On ne peut donc pas distinguer le minéral exact à partir de la seule signature dans ces cas :
            // on garde quand même l'entrée (le RecordName identifie le contexte de minage), mais plusieurs
            // minéraux d'une même catégorie produiront la même signature pour une même taille de cluster.
            var kind = recordName.Contains(".MineableRock_Fps_", StringComparison.OrdinalIgnoreCase) ? MiningKind.Fps
                : recordName.Contains(".MineableRock_GroundVehicle_", StringComparison.OrdinalIgnoreCase) ? MiningKind.GroundVehicle
                : MiningKind.ShipOrSurface;

            // Extract the primary mineral name from the composition.
            // compositionArray peut contenir plusieurs minéraux (le minéral principal du rocher,
            // répété sur plusieurs paliers de qualité, suivi de minéraux secondaires/traces qui ont
            // leur propre rocher dédié ailleurs). Seul le premier élément correspond au minéral
            // principal désigné par le nom du rocher ; les suivants ne doivent pas hériter de cette
            // signature (ex: le rocher "Bexalite" contient aussi de l'or et du borase en traces).
            var primaryPart = mineableParams.composition?.compositionArray?.FirstOrDefault();
            if (primaryPart?.mineableElement?.resourceType == null)
                continue;

            var displayName = primaryPart.mineableElement.resourceType.displayName;
            if (string.IsNullOrEmpty(displayName))
                continue;

            // Get localized mineral name. Certaines clés n'ont pas encore de traduction dans le p4k et
            // GetLocaleValue renvoie alors un texte de substitution du type "<= PLACEHOLDER =>".
            var localizedName = await _p4kService.GetLocaleValue(displayName).ConfigureAwait(false);
            if (string.IsNullOrEmpty(localizedName) || localizedName.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
                continue;

            var mineralKeyName = string.Concat(localizedName
                .Split(" ", StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('@', '(', ')').ToLowerInvariant())
                .Where(w => !ExcludedWords.Contains(w)));

            // Garde la première signature trouvée par (minéral, contexte de minage) : un même minéral peut
            // exister à la fois en FPS et en GroundVehicle (ex. Carinite), avec des signatures différentes.
            if (string.IsNullOrEmpty(mineralKeyName) || !seenKeys.Add((mineralKeyName, kind)))
                continue;

            var rarityMatch = RarityRegex.Match(recordName);
            var rarity = rarityMatch.Success ? rarityMatch.Groups[1].Value : string.Empty;

            results.Add(new MineralBaseSignature(mineralKeyName, localizedName, rarity, signatureValue, kind));
        }

        _logger.LogInformation("Found {Count} minerals with radar signatures", results.Count);
        return results;
    }
}
