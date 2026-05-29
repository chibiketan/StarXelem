using System.Collections.ObjectModel;
using DocumentFormat.OpenXml.InkML;
using Microsoft.Extensions.DependencyInjection;
using Sc.External.Common.Shard.V1;
using Sc.External.Services.Entitlement.V2;
using Sc.External.Services.Entitygraph.V1;
using StarXelem.Models;
using StarXelem.Services;
using StarXelem.ViewModels;
using StarXelem.ViewModels.Popup;

namespace StarXelem;

public static class DesignData
{
    public static MainWindowViewModel MainWindowViewModel { get; } = App.Current.Services.GetRequiredService<MainWindowViewModel>();
    public static ShipTabViewModel ShipTabViewModel { get; } = App.Current.Services.GetRequiredService<ShipTabViewModel>();
    public static ItemsTabViewModel ItemsTabViewModel { get; } = App.Current.Services.GetRequiredService<ItemsTabViewModel>();
    public static BlueprintListTabViewModel BlueprintListTabViewModel { get; } = App.Current.Services.GetRequiredService<BlueprintListTabViewModel>();
    public static ContainerTabViewModel ContainerTabViewModel { get; } = App.Current.Services.GetRequiredService<ContainerTabViewModel>();
    public static FriendListTabViewModel FriendListTabViewModel { get; } = App.Current.Services.GetRequiredService<FriendListTabViewModel>();

    public static PopupViewModel ComparisonPopupViewModel { get; } = App.Current.Services.GetRequiredService<PopupViewModel>();
    
    public static ItemComparisonPopupContentViewModel ItemComparisonPopupContentViewModel { get; } = App.Current.Services.GetRequiredService<ItemComparisonPopupContentViewModel>();
    public static P4kShipTabViewModel P4kShipTabViewModel { get; } = App.Current.Services.GetRequiredService<P4kShipTabViewModel>();
    public static MissionsTabViewModel MissionsTabViewModel { get; } = App.Current.Services.GetRequiredService<MissionsTabViewModel>();
    public static ReputationTabViewModel ReputationTabViewModel { get; } = App.Current.Services.GetRequiredService<ReputationTabViewModel>();


    static DesignData()
    {
        PopupateMainWindowViewModel();
        // Initialisation de ItemComparisonPopupContentViewModel pour le design
        var list = new List<ItemTypeComparisonResult>
        {
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 1", SourceCountSum = 1, TargetStackSum = 1, Status = ComparisonDiffType.Equal
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 2", SourceCountSum = 1, TargetStackSum = 10, Status = ComparisonDiffType.Gain
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type 3", SourceCountSum = 10, TargetStackSum = 1, Status = ComparisonDiffType.Loss
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type only left", SourceCountSum = 1, TargetStackSum = 0, Status = ComparisonDiffType.OnlySource
            },
            new ItemTypeComparisonResult
            {
                TechnicalType = "type only right", SourceCountSum = 0, TargetStackSum = 1, Status = ComparisonDiffType.OnlyTarget
            },
        };
        
        // Add 200 items to the list
        list.AddRange(Enumerable.Repeat(new ItemTypeComparisonResult{TechnicalType = "Test", SourceCountSum = 0, TargetStackSum = 0, Status = ComparisonDiffType.Equal}, 200));
        
        ItemComparisonPopupContentViewModel.FilteredResults = list;
        
        // comparison popup
        ComparisonPopupViewModel.ContentViewModel = ItemComparisonPopupContentViewModel;
        ComparisonPopupViewModel.IsVisible = true;
        ComparisonPopupViewModel.IsCloseButtonVisible = true;
        
        P4kShipTabViewModel.Ships.Add(new P4kShipModel
        {
            EntityClass = null,
            Name = "Test ship",
            TechnicalName = "test_ship",
            Manufacturer = "Test Manufacturer"
        });

        // Coolers
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "A",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler A1",
            PortName = "CoolerA1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "B",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler B1",
            PortName = "CoolerB1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "C",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler C1",
            PortName = "CoolerC1"
        });
        P4kShipTabViewModel.CoolerList.Add(new ()
        {
            Grade = "D",
            Size = 1,
            Class = ComponentClass.Military,
            DisplayName = "Cooler D1",
            PortName = "CoolerD1"
        });
        
        // Powerplants
        P4kShipTabViewModel.PowerplantList.AddRange([
            new ()
            {
                Grade = "A",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant A0",
                PortName = "PowerplantA0"
            },
            new ()
            {
                Grade = "B",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant B0",
                PortName = "PowerplantB0"
            },
            new ()
            {
                Grade = "C",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant C0",
                PortName = "PowerplantC0"
            },
            new ()
            {
                Grade = "D",
                Size = 0,
                Class = ComponentClass.Industrial,
                DisplayName = "Powerplant D0",
                PortName = "PowerplantD0"
            }
        ]);

        BlueprintListTabViewModel.IsLoading = true;
        BlueprintListTabViewModel.TreatmentStatus = "En cours de chargement...";
        BlueprintListTabViewModel.BlueprintList = new List<BlueprintViewModel>
        {
            new BlueprintViewModel
            {
                BlueprintId = "cm9ant001helmetmoss",
                Name = "Antium helmet moss camo",
                TierLevel = 1,
                RemainingUse = -1,
                CraftDuration = TimeSpan.FromMinutes(10),
                CategoryList = [
                    new BlueprintCategoryModel
                    {
                        Name = "Categorie 1",
                        MaterialList = [
                            new BlueprintResourceModel
                            {
                                Name = "Aslarite",
                                QuantityInScu = 0.015f
                            },
                            new BlueprintItemModel
                            {
                                Name = "Minerai Sadaryx",
                                QuantityCount = 4
                            }
                        ],
                        StatModifierList = [
                            new BlueprintStatLinearModel
                            {
                                Name = "Vitesse",
                                Min = 0.97f,
                                Max = 1.10f
                            },
                            new BlueprintStatAdditiveModel
                            {
                                Name = "Power Generation",
                                Bands =
                                [
                                    new BlueprintStatBandModel { StartQuality = 0, EndQuality = 249, Value = -2 },
                                    new BlueprintStatBandModel { StartQuality = 250, EndQuality = 499, Value = -1 },
                                    new BlueprintStatBandModel { StartQuality = 500, EndQuality = 699, Value = 0 },
                                    new BlueprintStatBandModel { StartQuality = 700, EndQuality = 899, Value = 1 },
                                    new BlueprintStatBandModel { StartQuality = 900, EndQuality = 1000, Value = 2 },
                                ]
                            }
                        ]
                    },
                    new BlueprintCategoryModel
                    {
                        Name = "Categorie 2",
                        MaterialList = [
                            new BlueprintResourceModel
                            {
                                Name = "Hadanite",
                                QuantityInScu = 0.01f
                            },
                            new BlueprintResourceModel
                            {
                                Name = "Titanium",
                                QuantityInScu = 0.06f
                            }

                        ],
                        StatModifierList = [
                            new BlueprintStatLinearModel
                            {
                                Name = "température Min.",
                                Min = 0.8f,
                                Max = 1.2f
                            },
                            new BlueprintStatLinearModel
                            {
                                Name = "température Max.",
                                Min = 0.8f,
                                Max = 1.2f
                            }

                        ]
                    }
                ]
            },
            new BlueprintViewModel
            {
                BlueprintId = "cm9bp002testblueprint",
                Name = "BluePrint 2",
                TierLevel = 1,
                RemainingUse = 2,
                CraftDuration = new TimeSpan(0, 0, 4, 30),
                CategoryList = []
            }
        };
        BlueprintListTabViewModel.SelectedBluePrint = BlueprintListTabViewModel.BlueprintList[0];

        FriendListTabViewModel.FriendList = Task.FromResult(new List<FriendViewModel>
        {
            // Avatar réel → image affichée (tokenName avec '_' → "OU")
            new FriendViewModel("OnlineUser", "online_user",
                "https://cdn.robertsspaceindustries.com/static/images/account/avatar_default_big.jpg",
                isConnected: true, isInGame: true, activity: "persistent_universe",
                () => Task.FromResult(new ShardInfo { Id = "pub_euw1b_3.24_12345", PlayerCount = 500, TotalPlayers = 700 })!),
            // Pas d'avatar, tokenName avec espace → "DP"
            new FriendViewModel("DesignPilot", "design pilot", null,
                isConnected: true, isInGame: true, activity: "arena_commander",
                () => Task.FromResult(new ShardInfo { Id = "pub_use1b_3.24_67890", PlayerCount = 54, TotalPlayers = 605 })!),
            // Pas d'avatar, tokenName avec '_' → "AW"
            new FriendViewModel("AliceWonder", "alice_wonder", null,
                isConnected: true, isInGame: false, activity: "menu"),
            // Pas d'avatar, tokenName simple → "NO"
            new FriendViewModel("Nova", "nova", null,
                isConnected: false, isInGame: false, activity: "Hors ligne"),
        });
        FriendListTabViewModel.OnlyConnected = false;

        ShipTabViewModel.IsLoading = true;
        ShipTabViewModel.TreatmentStatus = "Appel RSI";
        ShipTabViewModel.Spaceships = Task.FromResult<IList<SpaceshipModel>>(new List<SpaceshipModel>
        {
            // STOWED — achat réel, dans un hangar
            new(new Entitlement { EntityClassGuid = "GUID1", Status = EntitlementStatus.Delivered, Metadata = new EntitlementMetadata { Name = "Idris-P", SourceSku = "PackageName", RealMoney = true}})
            {
                Shipname = "AEGS_Idris_P",
                ReadableLocation = "[HANGAR] Area 18 - ArcCorp",
                StowContext = new EntityStowContext { IsStowed = true, ShardId = "pub_use1b_3.24_12345" }
            },
            // UNSTOWED — porté sur le joueur
            new(new Entitlement { EntityClassGuid = "GUID2", Status = EntitlementStatus.Delivered, Metadata = new EntitlementMetadata { Name = "", SourceSku = "Aurora MR Starter Pack", RealMoney = true } })
            {
                Shipname = "RSIN_AuroraMR",
                ReadableLocation = "Porté",
                StowContext = new EntityStowContext { IsStowed = false, ShardId = "pub_euw1b_3.24_67890" }
            },
            // DESTROYED — StowContext null
            new(new Entitlement { EntityClassGuid = "GUID3", Status = EntitlementStatus.Delivered, Metadata = new EntitlementMetadata { Name = "Cutlass Black", SourceSku = "PackageName", RealMoney = false }})
            {
                Shipname = "DRAK_Cutlass_Black"
            },
            // UNCLAIMED — jamais réclamé
            new(new Entitlement { EntityClassGuid = "GUID4", Status = EntitlementStatus.Undelivered, Metadata = new EntitlementMetadata { Name = "Avenger Titan", SourceSku = "PackageName", RealMoney = true} })
            {
                Shipname = "AEGS_Avenger_Titan",
                ReadableLocation = "[123456] Idris-P",
            },
            // STOWED — achat CCU (pas d'argent réel)
            new(new Entitlement { EntityClassGuid = "GUID5", Status = EntitlementStatus.Delivered, Metadata = new EntitlementMetadata { Name = "Carrack", SourceSku = "PackageName", RealMoney = false }})
            {
                Shipname = "MISC_Carrack",
                ReadableLocation = "[LOCATION] Lorville - Hurston",
                StowContext = new EntityStowContext { IsStowed = true, ShardId = "pub_use1b_3.24_99999" }
            },
        });
        
        PopupateReputationTabViewModel();
    }

    private static void PopupateMainWindowViewModel()
    {
        MainWindowViewModel.GrpcStatus = GrpcConnectionStatus.Error;
        MainWindowViewModel.CurrentShardName = "pub_euw1b_3.24_12345";
        MainWindowViewModel.GrpcErrorMessage = "Une message d'erreur\n avec saut de ligne et une ligne plutôt longue.";
    }

    private static void PopupateReputationTabViewModel()
    {
        // --- Covalex (Allié) ---
        // Faction lawfull avec plusieurs scopes actifs et des progrès avancés
        ReputationTabViewModel.FilteredContractors.Add(new ContractorModel
        {
            Id = default,
            Name = "Covalex",
            FactionStatus = FactionStatus.Friendly,
            Reputations = new List<ReputationModel>
            {
                new()
                {
                    DisplayName = "Ship Combat",
                    Category = "ShipCombat",
                    MaxValue = 1001,
                    CurrentValue = 850,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "ShipCombat_Rank5",
                        DisplayName = "Veteran",
                        Min = 500,
                        Max = 999
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "ShipCombat_NotEligible",    DisplayName = "Not Eligible",      Min = -1000, Max = -1 },
                        new() { Name = "ShipCombat_Rank0",          DisplayName = "Recruit",           Min = 0,     Max = 99 },
                        new() { Name = "ShipCombat_Rank1",          DisplayName = "Novice",            Min = 100,   Max = 499 },
                        new() { Name = "ShipCombat_Rank2",          DisplayName = "Apprentice",        Min = 500,   Max = 999 },
                        new() { Name = "ShipCombat_Rank3",          DisplayName = "Adept",             Min = 1000,  Max = 4999 },
                        new() { Name = "ShipCombat_Rank4",          DisplayName = "Proficient",        Min = 5000,  Max = 119999 },
                        new() { Name = "ShipCombat_Rank5",          DisplayName = "Veteran",           Min = 120000, Max = 479999 },
                        new() { Name = "ShipCombat_Rank6",          DisplayName = "Master",            Min = 480000, Max = 1000 },
                    }
                },
                new()
                {
                    DisplayName = "Security",
                    Category = "Security",
                    MaxValue = 5200000,
                    CurrentValue = 240000,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Security_Rank4",
                        DisplayName = "Agent",
                        Min = 120000,
                        Max = 299999
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Security_NotEligible",  DisplayName = "Not Eligible",   Min = -1000, Max = -1 },
                        new() { Name = "Security_Rank0",        DisplayName = "Applicant",      Min = 0,     Max = 4999 },
                        new() { Name = "Security_Rank1",        DisplayName = "Probation",      Min = 5000,  Max = 9999 },
                        new() { Name = "Security_Rank2",        DisplayName = "Junior",         Min = 10000, Max = 29999 },
                        new() { Name = "Security_Rank3",        DisplayName = "Officer",        Min = 30000, Max = 119999 },
                        new() { Name = "Security_Rank4",        DisplayName = "Agent",          Min = 120000, Max = 299999 },
                        new() { Name = "Security_Rank5",        DisplayName = "Senior Agent",   Min = 300000, Max = 1599999 },
                        new() { Name = "Security_Rank6",        DisplayName = "Master Agent",   Min = 1600000, Max = 5199999 },
                    }
                },
                new()
                {
                    DisplayName = "Hauling",
                    Category = "Hauling",
                    MaxValue = 367601,
                    CurrentValue = 5250,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Hauling_Rank1",
                        DisplayName = "Freelancer",
                        Min = 5250,
                        Max = 19999
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Hauling_NotEligible", DisplayName = "Not Eligible",    Min = -1000, Max = -1 },
                        new() { Name = "Hauling_Rank0",       DisplayName = "Novice",          Min = 0,     Max = 5249 },
                        new() { Name = "Hauling_Rank1",       DisplayName = "Freelancer",      Min = 5250,  Max = 19999 },
                        new() { Name = "Hauling_Rank2",       DisplayName = "Hauler",          Min = 20000, Max = 49999 },
                        new() { Name = "Hauling_Rank3",       DisplayName = "Contractor",      Min = 50000, Max = 99999 },
                        new() { Name = "Hauling_Rank4",       DisplayName = "Professional",    Min = 100000, Max = 199999 },
                        new() { Name = "Hauling_Rank5",       DisplayName = "Captain",         Min = 200000, Max = 367600 },
                        new() { Name = "Hauling_Rank6",       DisplayName = "Legend",          Min = 367601, Max = 367601 },
                    }
                },
            }
        });

        // --- Citizens for Prosperity (Neutre) ---
        // Faction lawfull avec des scopes en début de progression et un scope bloqué
        ReputationTabViewModel.FilteredContractors.Add(new ContractorModel
        {
            Id = default,
            Name = "Citizens for Prosperity",
            FactionStatus = FactionStatus.Neutral,
            Reputations = new List<ReputationModel>
            {
                new()
                {
                    DisplayName = "Bounty",
                    Category = "Bounty",
                    MaxValue = 5200000,
                    CurrentValue = 1200,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Bounty_Rank0",
                        DisplayName = "Applicant",
                        Min = 0,
                        Max = 4999
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Bounty_NotEligible",    DisplayName = "Not Eligible",     Min = -1000, Max = -1 },
                        new() { Name = "Bounty_Rank0",          DisplayName = "Applicant",        Min = 0,     Max = 4999 },
                        new() { Name = "Bounty_Rank1",          DisplayName = "Probation",        Min = 5000,  Max = 9999 },
                        new() { Name = "Bounty_Rank2",          DisplayName = "Junior",           Min = 10000, Max = 29999 },
                        new() { Name = "Bounty_Rank3",          DisplayName = "Agent",            Min = 30000, Max = 119999 },
                        new() { Name = "Bounty_Rank4",          DisplayName = "Senior",           Min = 120000, Max = 299999 },
                        new() { Name = "Bounty_Rank5",          DisplayName = "Veteran Agent",    Min = 300000, Max = 1599999 },
                        new() { Name = "Bounty_Rank6",          DisplayName = "Master Agent",     Min = 1600000, Max = 5199999 },
                    }
                },
                new()
                {
                    DisplayName = "Affinity",
                    Category = "Affinity",
                    MaxValue = 10000,
                    CurrentValue = -3500,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Affinity_Enemy_-040",
                        DisplayName = "Hostile",
                        Min = -4000,
                        Max = -3001
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Affinity_Enemy_-100",  DisplayName = "Worst Enemy",     Min = -10000, Max = -9001 },
                        new() { Name = "Affinity_Enemy_-090",  DisplayName = "Fierce Enemy",    Min = -9000,  Max = -8001 },
                        new() { Name = "Affinity_Enemy_-080",  DisplayName = "Severe Enemy",    Min = -8000,  Max = -7001 },
                        new() { Name = "Affinity_Enemy_-070",  DisplayName = "Enemy",           Min = -7000,  Max = -6001 },
                        new() { Name = "Affinity_Enemy_-060",  DisplayName = "Strong Enemy",    Min = -6000,  Max = -5001 },
                        new() { Name = "Affinity_Enemy_-050",  DisplayName = "Hostile Enemy",   Min = -5000,  Max = -4001 },
                        new() { Name = "Affinity_Enemy_-040",  DisplayName = "Hostile",         Min = -4000,  Max = -3001 },
                        new() { Name = "Affinity_Enemy_-030",  DisplayName = "Unfriendly",      Min = -3000,  Max = -2001 },
                        new() { Name = "Affinity_Neutral_000", DisplayName = "Neutral",         Min = -2000,  Max = 1999 },
                        new() { Name = "Affinity_Acquaint_030",DisplayName = "Acquaintance",    Min = 2000,   Max = 2999 },
                        new() { Name = "Affinity_Acquaint_050",DisplayName = "Friendly",        Min = 3000,   Max = 3999 },
                        new() { Name = "Affinity_Acquaint_075",DisplayName = "Friend",          Min = 4000,   Max = 4999 },
                        new() { Name = "Affinity_Acquaint_100",DisplayName = "Ally",            Min = 5000,   Max = 10000 },
                    }
                },
                new()
                {
                    DisplayName = "Ship Combat",
                    Category = "ShipCombat",
                    MaxValue = 1001,
                    CurrentValue = -500,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "ShipCombat_NotEligible",
                        DisplayName = "Not Eligible",
                        Min = -1000,
                        Max = -1
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "ShipCombat_NotEligible", DisplayName = "Not Eligible",   Min = -1000, Max = -1 },
                        new() { Name = "ShipCombat_Rank0",       DisplayName = "Recruit",        Min = 0,     Max = 99 },
                        new() { Name = "ShipCombat_Rank1",       DisplayName = "Novice",         Min = 100,   Max = 499 },
                        new() { Name = "ShipCombat_Rank2",       DisplayName = "Apprentice",     Min = 500,   Max = 999 },
                        new() { Name = "ShipCombat_Rank3",       DisplayName = "Adept",          Min = 1000,  Max = 4999 },
                        new() { Name = "ShipCombat_Rank4",       DisplayName = "Proficient",     Min = 5000,  Max = 119999 },
                        new() { Name = "ShipCombat_Rank5",       DisplayName = "Veteran",        Min = 120000, Max = 479999 },
                        new() { Name = "ShipCombat_Rank6",       DisplayName = "Master",         Min = 480000, Max = 1000 },
                    }
                },
            }
        });

        // --- Head Hunters (Hostile) ---
        // Faction outlaw avec réputation négative et plusieurs scopes bloqués
        ReputationTabViewModel.FilteredContractors.Add(new ContractorModel
        {
            Id = default,
            Name = "Head Hunters",
            FactionStatus = FactionStatus.Hostile,
            Reputations = new List<ReputationModel>
            {
                new()
                {
                    DisplayName = "Bounty",
                    Category = "Bounty",
                    MaxValue = 5200000,
                    CurrentValue = -800,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Bounty_NotEligible",
                        DisplayName = "Not Eligible",
                        Min = -1000,
                        Max = -1
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Bounty_NotEligible",    DisplayName = "Not Eligible",     Min = -1000, Max = -1 },
                        new() { Name = "Bounty_Rank0",          DisplayName = "Applicant",        Min = 0,     Max = 4999 },
                        new() { Name = "Bounty_Rank1",          DisplayName = "Probation",        Min = 5000,  Max = 9999 },
                        new() { Name = "Bounty_Rank2",          DisplayName = "Junior",           Min = 10000, Max = 29999 },
                        new() { Name = "Bounty_Rank3",          DisplayName = "Agent",            Min = 30000, Max = 119999 },
                        new() { Name = "Bounty_Rank4",          DisplayName = "Senior",           Min = 120000, Max = 299999 },
                        new() { Name = "Bounty_Rank5",          DisplayName = "Veteran Agent",    Min = 300000, Max = 1599999 },
                        new() { Name = "Bounty_Rank6",          DisplayName = "Master Agent",     Min = 1600000, Max = 5199999 },
                    }
                },
                new()
                {
                    DisplayName = "Affinity",
                    Category = "Affinity",
                    MaxValue = 10000,
                    CurrentValue = -7500,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Affinity_Enemy_-080",
                        DisplayName = "Enemy",
                        Min = -8000,
                        Max = -7001
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Affinity_Enemy_-100",  DisplayName = "Worst Enemy",     Min = -10000, Max = -9001 },
                        new() { Name = "Affinity_Enemy_-090",  DisplayName = "Fierce Enemy",    Min = -9000,  Max = -8001 },
                        new() { Name = "Affinity_Enemy_-080",  DisplayName = "Severe Enemy",    Min = -8000,  Max = -7001 },
                        new() { Name = "Affinity_Enemy_-070",  DisplayName = "Enemy",           Min = -7000,  Max = -6001 },
                        new() { Name = "Affinity_Enemy_-060",  DisplayName = "Strong Enemy",    Min = -6000,  Max = -5001 },
                        new() { Name = "Affinity_Enemy_-050",  DisplayName = "Hostile Enemy",   Min = -5000,  Max = -4001 },
                        new() { Name = "Affinity_Enemy_-040",  DisplayName = "Hostile",         Min = -4000,  Max = -3001 },
                        new() { Name = "Affinity_Enemy_-030",  DisplayName = "Unfriendly",      Min = -3000,  Max = -2001 },
                        new() { Name = "Affinity_Neutral_000", DisplayName = "Neutral",         Min = -2000,  Max = 1999 },
                        new() { Name = "Affinity_Acquaint_030",DisplayName = "Acquaintance",    Min = 2000,   Max = 2999 },
                        new() { Name = "Affinity_Acquaint_050",DisplayName = "Friendly",        Min = 3000,   Max = 3999 },
                        new() { Name = "Affinity_Acquaint_075",DisplayName = "Friend",          Min = 4000,   Max = 4999 },
                        new() { Name = "Affinity_Acquaint_100",DisplayName = "Ally",            Min = 5000,   Max = 10000 },
                    }
                },
            }
        });

        // --- Mercenary Guild (Neutre) ---
        // Un contrat avec un seul scope mais une progression très avancée
        ReputationTabViewModel.FilteredContractors.Add(new ContractorModel
        {
            Id = default,
            Name = "Mercenary Guild",
            FactionStatus = FactionStatus.Neutral,
            Reputations = new List<ReputationModel>
            {
                new()
                {
                    DisplayName = "Security",
                    Category = "Security",
                    MaxValue = 5200000,
                    CurrentValue = 1800000,
                    TierName = "Tier 1",
                    CurrentStanding = new StandingModel
                    {
                        Name = "Security_Rank6",
                        DisplayName = "Master Agent",
                        Min = 1600000,
                        Max = 5199999
                    },
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "Security_NotEligible",  DisplayName = "Not Eligible",   Min = -1000, Max = -1 },
                        new() { Name = "Security_Rank0",        DisplayName = "Applicant",      Min = 0,     Max = 4999 },
                        new() { Name = "Security_Rank1",        DisplayName = "Probation",      Min = 5000,  Max = 9999 },
                        new() { Name = "Security_Rank2",        DisplayName = "Junior",         Min = 10000, Max = 29999 },
                        new() { Name = "Security_Rank3",        DisplayName = "Officer",        Min = 30000, Max = 119999 },
                        new() { Name = "Security_Rank4",        DisplayName = "Agent",          Min = 120000, Max = 299999 },
                        new() { Name = "Security_Rank5",        DisplayName = "Senior Agent",   Min = 300000, Max = 1599999 },
                        new() { Name = "Security_Rank6",        DisplayName = "Master Agent",   Min = 1600000, Max = 5199999 },
                    }
                },
            }
        });

        // --- Rough & Ready (Non chargé) ---
        // Pour couvrir le cas NotLoaded avec CurrentValue à null
        ReputationTabViewModel.FilteredContractors.Add(new ContractorModel
        {
            Id = default,
            Name = "Rough & Ready",
            FactionStatus = FactionStatus.NotLoaded,
            Reputations = new List<ReputationModel>
            {
                new()
                {
                    DisplayName = "Ship Combat",
                    Category = "ShipCombat",
                    MaxValue = 1001,
                    CurrentValue = null,
                    TierName = "Tier 1",
                    CurrentStanding = null,
                    StandingList = new List<StandingModel>
                    {
                        new() { Name = "ShipCombat_NotEligible", DisplayName = "Not Eligible",   Min = -1000, Max = -1 },
                        new() { Name = "ShipCombat_Rank0",       DisplayName = "Recruit",        Min = 0,     Max = 99 },
                        new() { Name = "ShipCombat_Rank1",       DisplayName = "Novice",         Min = 100,   Max = 499 },
                        new() { Name = "ShipCombat_Rank2",       DisplayName = "Apprentice",     Min = 500,   Max = 999 },
                        new() { Name = "ShipCombat_Rank3",       DisplayName = "Adept",          Min = 1000,  Max = 4999 },
                        new() { Name = "ShipCombat_Rank4",       DisplayName = "Proficient",     Min = 5000,  Max = 119999 },
                        new() { Name = "ShipCombat_Rank5",       DisplayName = "Veteran",        Min = 120000, Max = 479999 },
                        new() { Name = "ShipCombat_Rank6",       DisplayName = "Master",         Min = 480000, Max = 1000 },
                    }
                },
            }
        });
    }
}