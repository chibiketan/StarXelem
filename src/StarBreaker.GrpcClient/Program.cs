using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using Sc.External.Services.Identity.V1;
using StarBreaker.Common;
using StarBreaker.Sandbox;




var scWatcher = new StarCitizenClientWatcher(@"D:\Games\Roberts Space Industries\StarCitizen\LIVE\");
scWatcher.Start();
var loginData = await scWatcher.WaitForLoginData();

Console.WriteLine($"Got Login data for user: \"{loginData.Username}\" on server: \"{loginData.StarNetwork.ServicesEndpoint}\"");

var channel = GrpcChannel.ForAddress(new Uri(loginData.StarNetwork.ServicesEndpoint));
var identityClient = new IdentityService.IdentityServiceClient(channel);
        
var currentPlayer = identityClient.GetCurrentPlayer(new GetCurrentPlayerRequest(), new Metadata { { "Authorization", $"Bearer {loginData.AuthToken}" } });

var authHeaders = new Metadata { { "Authorization", $"Bearer {currentPlayer.Jwt}" } };

//IllegalCharacterCustomization.Run(channel, authHeaders);
StarshipRetrieval.Run(channel, authHeaders);