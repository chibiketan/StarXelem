using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarBreaker.P4k;

var timer = new TimeLogger();
var p4k = P4kDirectoryNode.FromP4k(P4kFile.FromFile(@"D:\Games\Roberts Space Industries\StarCitizen\LIVE\Data.p4k"));
var dcbStream = p4k.OpenRead(@"Data\Game2.dcb");

var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(new DataCoreDatabase(dcbStream)));
        
timer.LogReset("Loaded DataForge");

var allRecords = df.DataCore.Database.MainRecords
    .AsParallel()
    .Select(x => df.GetFromRecord(x))
    .ToList();
timer.LogReset("Extracted all records.");

var classDefinitions = allRecords.Where(r => r.Data is EntityClassDefinition).Select(r => r.Data as EntityClassDefinition).ToList();
var spaceships = classDefinitions.Where(x => x.tags.Any(t => t?.tagName == "Ship")).ToList();
var vehicles = spaceships.Where(x => x.Components.Any(t => t is VehicleComponentParams)).Select(x => x?.Components.First(t => t is VehicleComponentParams) as VehicleComponentParams).ToList();
var vehiclesIdrises = spaceships.Where(x => x.Components.Any(t => t is VehicleComponentParams && ((VehicleComponentParams)t).vehicleName.Contains("Idris"))).ToList();
var idrises = vehicles.Where(x => x.vehicleName.Contains("Idris")).ToList();
var names = idrises.Select(x => x.vehicleName).ToList();

Console.WriteLine();