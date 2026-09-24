using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Sc.External.Common.Api.V1;
using Sc.External.Common.Shard.V1;
using Sc.External.Services.Entitygraph.V1;
using Sc.External.Services.Identity.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Services;
using DateTime = System.DateTime;
using EntityFilter = Sc.External.Services.Entitygraph.V1.EntityFilter;

namespace StarXelem.Cli.TestDb;

/// <summary>
/// Sonde pour l'appel gRPC EntityGraphService.EntityQueryAsync : reproduit ce que fait
/// GrpcClientService.QueryGraphBySearch, avec chaque paramètre réglable en ligne de commande, afin de
/// comprendre pourquoi certaines requêtes sont rejetées (« paramètres incorrects ») et d'autres non.
/// Les options s'écrivent obligatoirement --cle=valeur : Program.cs traite tout argument qui ne
/// commence pas par « -- » comme le chemin du P4K.
/// </summary>
internal static class EntityQueryProbe
{
    private const string StowedInEdge = "STOWED_IN";
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(90);

    private static readonly JsonFormatter IndentedFormatter = new(JsonFormatter.Settings.Default.WithIndentation());

    /// <returns>Code de sortie du processus : 0 si toutes les requêtes ont abouti, 1 si l'une a été rejetée, 2 si les options sont invalides.</returns>
    public static async Task<int> RunAsync(string p4kPath, string[] args)
    {
        if (Has(args, "help"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (Has(args, "print-only"))
            {
                // Aucune connexion : le geid du joueur connecté n'est pas connu, un geid fictif tient sa place.
                Console.WriteLine("(--print-only : aucune connexion, le geid du joueur connecté est remplacé par 0)");
                foreach (var request in BuildRequests(args, playerGeid: 0))
                {
                    PrintRequest(request);
                }

                return 0;
            }

            var connection = await ConnectAsync(p4kPath);
            if (connection is null)
            {
                return 1;
            }

            var requests = BuildRequests(args, connection.PlayerGeid);
            var allPages = Has(args, "all-pages");
            var dumpResponse = Has(args, "dump-response");
            var maxRows = checked((int)ParseULong("max-rows", Opt(args, "max-rows") ?? "20"));

            var failures = 0;
            for (var index = 0; index < requests.Count; index++)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Requête {index + 1}/{requests.Count} ===");
                PrintRequest(requests[index]);

                if (!await ExecuteAsync(connection, requests[index], allPages, dumpResponse, maxRows))
                {
                    failures++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"=== Bilan : {requests.Count - failures} acceptée(s), {failures} rejetée(s) sur {requests.Count} ===");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Option invalide : {ex.Message}");
            Console.Error.WriteLine("Utiliser --probe-grpc --help pour la liste des options.");
            return 2;
        }
        catch (InvalidProtocolBufferException ex)
        {
            Console.Error.WriteLine($"JSON de requête invalide : {ex.Message}");
            return 2;
        }
    }

    /* ---- Connexion : même chaîne que GrpcClientService.InitClient ---- */

    private sealed record Connection(EntityGraphService.EntityGraphServiceClient Service, Metadata Headers, ulong PlayerGeid);

    private static async Task<Connection?> ConnectAsync(string p4kPath)
    {
        var directory = new FileInfo(p4kPath).Directory?.FullName ?? string.Empty;
        Console.WriteLine($"Recherche de loginData.json sous : {directory}");

        using var watcher = new StarCitizenClientWatcher(directory);
        watcher.Start();

        var loginTask = watcher.WaitForLoginData();
        if (await Task.WhenAny(loginTask, Task.Delay(LoginTimeout)) != loginTask)
        {
            Console.Error.WriteLine($"Aucune donnée de connexion après {LoginTimeout.TotalSeconds:0} s : le jeu ou le launcher doit être lancé et connecté.");
            return null;
        }

        var login = await loginTask;
        Console.WriteLine($"Connexion : utilisateur \"{login.Username}\" sur {login.StarNetwork.ServicesEndpoint}");

        try
        {
            var channel = GrpcChannel.ForAddress(new Uri(login.StarNetwork.ServicesEndpoint));
            var identityClient = new IdentityService.IdentityServiceClient(channel);
            var currentPlayer = await identityClient.GetCurrentPlayerAsync(
                new GetCurrentPlayerRequest(),
                new Metadata { { "Authorization", $"Bearer {login.AuthToken}" } });

            // Mêmes en-têtes que GrpcClientService. Les jetons ne sont jamais affichés.
            var headers = new Metadata
            {
                { "Authorization", $"Bearer {currentPlayer.Jwt}" },
                { "grpc-timeout", "60S" }
            };

            Console.WriteLine($"Joueur connecté : geid {currentPlayer.Player.Geid}");
            return new Connection(new EntityGraphService.EntityGraphServiceClient(channel), headers, currentPlayer.Player.Geid);
        }
        catch (RpcException ex)
        {
            Console.Error.WriteLine("Échec de l'identification du joueur :");
            PrintRpcError(ex);
            return null;
        }
    }

    /* ---- Construction des requêtes ---- */

    private static List<EntityQueryRequest> BuildRequests(string[] args, ulong playerGeid)
    {
        var jsonFile = Opt(args, "json");
        if (jsonFile is null)
        {
            return [BuildRequestFromOptions(args, playerGeid)];
        }

        if (!File.Exists(jsonFile))
        {
            throw new ArgumentException($"fichier introuvable : {jsonFile}");
        }

        // {{me}} est remplacé par le geid du joueur connecté, pour garder un fichier valable d'un compte à l'autre.
        var text = File.ReadAllText(jsonFile).Replace("{{me}}", playerGeid.ToString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);

        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];

        // Analyse stricte (les champs inconnus sont rejetés) : une faute de frappe dans le fichier est signalée
        // côté client au lieu d'être ignorée en silence.
        return elements.Select(e => JsonParser.Default.Parse<EntityQueryRequest>(e.GetRawText())).ToList();
    }

    /// <summary>Reproduit la requête de GrpcClientService.QueryGraphBySearch, chaque élément étant activé par une option.</summary>
    private static EntityQueryRequest BuildRequestFromOptions(string[] args, ulong playerGeid)
    {
        var filters = new List<EntityFilter>();

        if (Opt(args, "owner") is { } owner)
        {
            filters.Add(PropEqULong("ownerId", string.Equals(owner, "me", StringComparison.OrdinalIgnoreCase) ? playerGeid : ParseULong("owner", owner)));
        }

        if (Opt(args, "geid") is { } geid)
        {
            filters.Add(PropEqULong("geid", ParseULong("geid", geid)));
        }

        if (Opt(args, "types") is { } types)
        {
            filters.Add(PropIn("itemTypeEnum", Split(types).Select(t => Int(ParseItemType(t)))));
        }

        if (Opt(args, "stowed-in") is { } stowedIn)
        {
            var edge = new EdgeFilter { EdgeType = StowedInEdge };
            edge.Values.AddRange(Split(stowedIn).Select(Str));

            var or = new EntityCompositeFilter { Operator = LogicalOperator.Or };
            or.Filters.Add(new EntityFilter { EdgeFilter = edge });

            if (Has(args, "or-owner"))
            {
                or.Filters.Add(PropEqULong("ownerId", playerGeid));
            }

            filters.Add(new EntityFilter { CompositeFilter = or });
        }

        if (Opt(args, "parent-urn") is { } parentUrn)
        {
            filters.Add(PropIn("parentUrn", Split(parentUrn).Select(Str)));
        }

        var composite = new EntityCompositeFilter { Operator = LogicalOperator.And };
        composite.Filters.AddRange(filters);

        var query = new EntityGraphQuery
        {
            Filter = new EntityFilter { CompositeFilter = composite },
            Projection = new EntityProjection
            {
                Tree = new EntityTreeProjection
                {
                    Enabled = Has(args, "tree"),
                    IncludeInventoryNodes = Has(args, "tree-inventory-nodes"),
                    PathMode = !Has(args, "no-path-mode")
                },
                OutgoingEdges = !Has(args, "no-outgoing-edges"),
                Snapshots = !Has(args, "no-snapshots"),
                EntityClasses = Has(args, "entity-classes"),
                SnapshotFlags = 0,
                SnapshotExcludeFlags = 0
            },
            InventoryId = Opt(args, "inventory-id") ?? string.Empty,
            Language = Opt(args, "language") ?? string.Empty,
            Pagination = new PaginationArguments
            {
                First = checked((uint)ParseULong("first", Opt(args, "first") ?? "250")),
                After = Opt(args, "after") ?? string.Empty
            }
        };

        if (!Has(args, "no-sort"))
        {
            query.Sort = new EntitySortingArguments
            {
                EntityProperty = new EntitySortingByProperty
                {
                    Property = Opt(args, "sort-property") ?? "geid",
                    SortComparator = SortComparator.Numerical
                },
                Order = PaginationOrder.Ascending
            };
        }

        return new EntityQueryRequest
        {
            Body = new EntityQueryRequestBody
            {
                Scope = new Scope { Type = ScopeType.Global, ShardId = Opt(args, "shard") ?? string.Empty },
                Query = query
            }
        };
    }

    private static ScalarValue ULong(ulong value) => new() { UnsignedBigintValue = value };
    private static ScalarValue Int(int value) => new() { IntegerValue = value };
    private static ScalarValue Str(string value) => new() { StringValue = value };

    private static EntityFilter PropEqULong(string property, ulong value) => new()
    {
        PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.Equal,
            Property = property,
            Values = { ULong(value) }
        }
    };

    private static EntityFilter PropIn(string property, IEnumerable<ScalarValue> values) => new()
    {
        PropertyFilter = new Sc.External.Services.Entitygraph.V1.PropertyFilter
        {
            Operator = ComparisonOperator.In,
            Property = property,
            Values = { values }
        }
    };

    /* ---- Exécution et affichage ---- */

    private static async Task<bool> ExecuteAsync(Connection connection, EntityQueryRequest request, bool allPages, bool dumpResponse, int maxRows)
    {
        var stopwatch = Stopwatch.StartNew();
        var pages = 0;
        var nodes = 0;
        var edges = 0;
        var snapshots = 0;
        var typeCounts = new SortedDictionary<int, int>();
        var sample = new List<string>();
        EntityQueryResponse? response = null;

        try
        {
            do
            {
                if (response != null)
                {
                    request.Body.Query.Pagination.After = response.Body.PageInfo.EndCursor;
                }

                response = await connection.Service.EntityQueryAsync(request, connection.Headers, deadline: DateTime.UtcNow.Add(CallDeadline));
                pages++;
                edges += response.Body.Results.Edges.Count;
                snapshots += response.Body.Snapshots.Count;

                foreach (var node in response.Body.Results.Nodes)
                {
                    var properties = node.Properties.EntityProperties;
                    var itemType = (int)properties.ItemTypeEnum;
                    typeCounts[itemType] = typeCounts.GetValueOrDefault(itemType) + 1;
                    nodes++;

                    if (sample.Count < maxRows)
                    {
                        sample.Add($"geid={properties.Geid} crc={properties.ClassGuidCrc} type={FormatItemType(itemType)}");
                    }
                }

                if (dumpResponse)
                {
                    Console.WriteLine(IndentedFormatter.Format(response));
                }
            } while (allPages && response.Body.PageInfo.HasNextPage);
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"-> REJETÉE après {stopwatch.ElapsedMilliseconds} ms");
            PrintRpcError(ex);
            return false;
        }

        Console.WriteLine($"-> ACCEPTÉE en {stopwatch.ElapsedMilliseconds} ms : {pages} page(s), {nodes} nœud(s), {edges} arête(s), {snapshots} snapshot(s)");
        Console.WriteLine($"   dernière page : hasNextPage={response!.Body.PageInfo.HasNextPage} endCursor=\"{response.Body.PageInfo.EndCursor}\"");

        foreach (var line in sample)
        {
            Console.WriteLine($"   {line}");
        }

        if (nodes > sample.Count)
        {
            Console.WriteLine($"   … {nodes - sample.Count} autre(s) nœud(s) (--max-rows=N pour en voir plus)");
        }

        if (typeCounts.Count > 0)
        {
            Console.WriteLine("   répartition par type : " + string.Join(", ", typeCounts.Select(kv => $"{FormatItemType(kv.Key)}={kv.Value}")));
        }

        return true;
    }

    private static void PrintRequest(EntityQueryRequest request)
    {
        Console.WriteLine("Requête envoyée (à copier dans un fichier pour --json=) :");
        Console.WriteLine(IndentedFormatter.Format(request));
    }

    /// <summary>
    /// Affiche le code et le détail du rejet, puis les trailers. Le serveur y place souvent l'explication précise
    /// (grpc-status-details-bin, un google.rpc.Status sérialisé) : sans le type protobuf correspondant, on en
    /// extrait les chaînes lisibles, ce qui suffit à repérer le champ ou la valeur mis en cause.
    /// </summary>
    private static void PrintRpcError(RpcException ex)
    {
        Console.WriteLine($"   code   : {ex.StatusCode}");
        Console.WriteLine($"   détail : {ex.Status.Detail}");

        foreach (var entry in ex.Trailers)
        {
            Console.WriteLine(entry.IsBinary
                ? $"   trailer {entry.Key} ({entry.ValueBytes.Length} octets) : {ReadableStrings(entry.ValueBytes)}"
                : $"   trailer {entry.Key} : {entry.Value}");
        }
    }

    private static string ReadableStrings(byte[] bytes)
    {
        var runs = new List<string>();
        var current = new StringBuilder();

        foreach (var value in bytes)
        {
            if (value is >= 0x20 and < 0x7F)
            {
                current.Append((char)value);
                continue;
            }

            if (current.Length >= 4)
            {
                runs.Add(current.ToString());
            }

            current.Clear();
        }

        if (current.Length >= 4)
        {
            runs.Add(current.ToString());
        }

        return runs.Count > 0 ? string.Join(" | ", runs) : "(aucune chaîne lisible)";
    }

    private static string FormatItemType(int value) => Enum.IsDefined(typeof(EItemType), value) ? ((EItemType)value).ToString() : value.ToString();

    /* ---- Lecture des options ---- */

    private static string? Opt(string[] args, string name)
    {
        var prefix = $"--{name}=";
        return args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    }

    private static bool Has(string[] args, string name) => args.Any(a => string.Equals(a, $"--{name}", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ulong ParseULong(string option, string value) =>
        ulong.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"--{option} attend un entier positif, reçu \"{value}\"");

    private static int ParseItemType(string value)
    {
        if (int.TryParse(value, out var number))
        {
            return number;
        }

        return Enum.TryParse<EItemType>(value, ignoreCase: true, out var type)
            ? (int)type
            : throw new ArgumentException($"--types : type d'objet inconnu \"{value}\" (nom d'EItemType ou entier)");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Sonde EntityGraphService.EntityQueryAsync

            Usage :
              dotnet run --project src\StarXelem.cli.testdb -- --probe-grpc [chemin\Data.p4k] [options]

            Le chemin du P4K sert à localiser loginData.json (le jeu ou le launcher doit être connecté).
            Les options s'écrivent --cle=valeur (jamais --cle valeur).

            Filtres (combinés par ET, comme QueryGraphBySearch) :
              --owner=<geid|me>        propriétaire (ownerId)
              --geid=<geid>            un objet précis
              --types=<a,b,...>        types d'objet (noms d'EItemType ou entiers), itemTypeEnum IN
              --stowed-in=<id,id,...>  arête STOWED_IN vers ces conteneurs
              --or-owner               avec --stowed-in : OU ownerId = joueur connecté
              --parent-urn=<a,b,...>   parentUrn IN

            Requête :
              --inventory-id=<id>      Query.InventoryId (vide par défaut)
              --shard=<id>             Scope.ShardId (vide par défaut)
              --language=<code>        Query.Language (vide par défaut)
              --first=<n>              taille de page (250 par défaut)
              --after=<curseur>        curseur de départ
              --no-sort                n'envoie aucun tri
              --sort-property=<nom>    propriété de tri (geid par défaut)

            Projection (valeurs par défaut de QueryGraphBySearch) :
              --tree  --tree-inventory-nodes  --no-path-mode
              --no-outgoing-edges  --no-snapshots  --entity-classes

            Autres :
              --json=<fichier>         requête libre (objet ou tableau d'objets EntityQueryRequest en JSON) ;
                                       {{me}} y est remplacé par le geid du joueur connecté ; les options de
                                       filtre, de requête et de projection sont alors ignorées
              --all-pages              suit la pagination jusqu'à la dernière page
              --dump-response          affiche chaque réponse complète en JSON
              --max-rows=<n>           nombre de nœuds listés (20 par défaut)
              --print-only             affiche la requête sans se connecter ni l'envoyer
              --help                   affiche cette aide

            Chaque requête envoyée est affichée en JSON : la copier dans un fichier pour la modifier et la rejouer
            avec --json=. Avec un tableau de requêtes, le bilan indique lesquelles sont acceptées ou rejetées.
            """);
    }
}
