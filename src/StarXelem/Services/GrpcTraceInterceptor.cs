using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace StarXelem.Services;

/// <summary>
/// Intercepteur gRPC écrivant chaque requête et sa réponse dans un dossier, pour analyse à froid.
/// Chaque appel produit un fichier <c>NNNNN_Service_Methode_requete.json</c> et un fichier
/// <c>NNNNN_Service_Methode_reponse.json</c>. Les en-têtes d'authentification sont masqués.
/// </summary>
public sealed class GrpcTraceInterceptor : Interceptor
{
    private static readonly JsonFormatter ProtoFormatter = new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly ILogger _logger;
    private int _sequence;

    /// <summary>
    /// Dossier de la session de trace (un sous-dossier horodaté du dossier demandé).
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// Crée l'intercepteur et le sous-dossier horodaté qui recevra les traces de cette exécution.
    /// </summary>
    /// <param name="rootDirectory">Dossier passé en argument de la ligne de commande.</param>
    /// <param name="logger">Logger utilisé pour signaler les échecs d'écriture.</param>
    public GrpcTraceInterceptor(string rootDirectory, ILogger logger)
    {
        _logger = logger;
        Directory = Path.Combine(rootDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        System.IO.Directory.CreateDirectory(Directory);
        _logger.LogInformation("Trace gRPC activée dans {Directory}", Directory);
    }

    /// <inheritdoc />
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var prefix = WriteRequest(context.Method, context.Options, request);
        var watch = Stopwatch.StartNew();
        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            TraceUnaryResponseAsync(prefix, context.Method, call.ResponseAsync, watch),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    /// <inheritdoc />
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var prefix = WriteRequest(context.Method, context.Options, request);
        var call = continuation(request, context);
        var reader = new TracingStreamReader<TResponse>(this, prefix, context.Method, call.ResponseStream);

        return new AsyncServerStreamingCall<TResponse>(
            reader,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            () =>
            {
                // Flux abandonné avant la fin : on écrit tout de même ce qui a été reçu.
                reader.Complete(null);
                call.Dispose();
            });
    }

    private async Task<TResponse> TraceUnaryResponseAsync<TResponse>(string prefix, IMethod method, Task<TResponse> responseTask, Stopwatch watch)
    {
        try
        {
            var response = await responseTask.ConfigureAwait(false);
            WriteResponse(prefix, method, watch.Elapsed, [ToJson(response)], null, false);
            return response;
        }
        catch (Exception ex)
        {
            WriteResponse(prefix, method, watch.Elapsed, [], ex, false);
            throw;
        }
    }

    private string WriteRequest(IMethod method, CallOptions options, object request)
    {
        var index = Interlocked.Increment(ref _sequence);
        var serviceName = method.ServiceName[(method.ServiceName.LastIndexOf('.') + 1)..];
        var prefix = $"{index:D5}_{serviceName}_{method.Name}";

        var headers = new JsonObject();
        foreach (var entry in options.Headers ?? [])
        {
            var isAuth = entry.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase);
            headers[entry.Key] = isAuth ? "***" : entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value;
        }

        var content = new JsonObject
        {
            ["method"] = method.FullName,
            ["type"] = method.Type.ToString(),
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["deadlineUtc"] = options.Deadline?.ToString("O"),
            ["headers"] = headers,
            ["request"] = ToJson(request)
        };

        WriteFile($"{prefix}_requete.json", content);
        return prefix;
    }

    private void WriteResponse(string prefix, IMethod method, TimeSpan elapsed, IEnumerable<JsonNode?> messages, Exception? error, bool streaming)
    {
        var content = new JsonObject
        {
            ["method"] = method.FullName,
            ["timestamp"] = DateTime.Now.ToString("O"),
            ["durationMs"] = Math.Round(elapsed.TotalMilliseconds, 1),
            ["status"] = error switch
            {
                null => StatusCode.OK.ToString(),
                RpcException rpc => rpc.StatusCode.ToString(),
                OperationCanceledException => StatusCode.Cancelled.ToString(),
                _ => "Exception"
            },
            ["error"] = error?.Message
        };

        var messageArray = new JsonArray(messages.ToArray());
        if (streaming)
        {
            content["messageCount"] = messageArray.Count;
            content["messages"] = messageArray;
        }
        else
        {
            content["response"] = messageArray.Count > 0 ? messageArray[0]?.DeepClone() : null;
        }

        WriteFile($"{prefix}_reponse.json", content);
    }

    private static JsonNode? ToJson(object? message)
    {
        return message switch
        {
            null => null,
            IMessage protoMessage => JsonNode.Parse(ProtoFormatter.Format(protoMessage)),
            _ => JsonValue.Create(message.ToString())
        };
    }

    private void WriteFile(string fileName, JsonObject content)
    {
        try
        {
            File.WriteAllText(Path.Combine(Directory, fileName), content.ToJsonString(WriteOptions));
        }
        catch (Exception ex)
        {
            // La trace est un outil de diagnostic : un échec d'écriture ne doit pas casser l'appel gRPC.
            _logger.LogWarning(ex, "Impossible d'écrire la trace gRPC {FileName}", fileName);
        }
    }

    /// <summary>
    /// Lecteur de flux qui conserve chaque message reçu et écrit le fichier réponse à la fin du flux.
    /// </summary>
    private sealed class TracingStreamReader<TResponse> : IAsyncStreamReader<TResponse>
    {
        private readonly GrpcTraceInterceptor _owner;
        private readonly string _prefix;
        private readonly IMethod _method;
        private readonly IAsyncStreamReader<TResponse> _inner;
        private readonly List<JsonNode?> _messages = [];
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private int _completed;

        public TracingStreamReader(GrpcTraceInterceptor owner, string prefix, IMethod method, IAsyncStreamReader<TResponse> inner)
        {
            _owner = owner;
            _prefix = prefix;
            _method = method;
            _inner = inner;
        }

        public TResponse Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            try
            {
                var hasNext = await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
                if (hasNext)
                {
                    _messages.Add(ToJson(_inner.Current));
                }
                else
                {
                    Complete(null);
                }

                return hasNext;
            }
            catch (Exception ex)
            {
                Complete(ex);
                throw;
            }
        }

        public void Complete(Exception? error)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _owner.WriteResponse(_prefix, _method, _watch.Elapsed, _messages, error, true);
            }
        }
    }
}
