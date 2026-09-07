# Signatures radar par cluster (table `MineralSignatures`)

Cette table est générée automatiquement à chaque reconstruction de la base locale (`LocalDatabaseService.PopulateMineralSignaturesAsync`, phase 11), à partir des signatures de base extraites du p4k (cf. `Documents/Datafiles/MineableRocks.md`).

## Hypothèse de calcul (⚠️ à valider en jeu)

**Signature(cluster de N rochers) = N × Signature(rocher isolé)**, pour N de 1 à 10 en minage vaisseau/surface (`ScanConstants.MinClusterSize`..`MaxClusterSize`), et N de 20 à 30 en minage à la main (FPS) ou au véhicule terrestre (`ScanConstants.MinLargeClusterSize`..`MaxLargeClusterSize`).

Cette formule est encapsulée dans `MineralSignatureGenerator.Generate` (`Services/Mining/MineralSignatureGenerator.cs`) : c'est le seul endroit à modifier si l'hypothèse s'avère fausse après vérification en jeu (scanner un cluster connu, comparer à N × la signature de base du minéral).

Sur une capture fournie par l'utilisateur, la valeur affichée était `80,000`, qui ne correspond à aucun multiple ≤ 10 des signatures uniques par minéral (la plus grande valeur possible en minage vaisseau/surface est 10 × Ice = 43 000). **Elle correspond en revanche exactement à `4 000 × 20`**, soit un cluster de 20 rochers minés au véhicule terrestre (signature générique GroundVehicle = 4000, cf. section suivante). C'est l'hypothèse la plus probable pour expliquer cette capture, mais elle reste à confirmer en jeu (compter les rochers du cluster scanné).

## Table de référence (base × N, N = 1..10)

| Minéral | Rareté | Base | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Savrilium | Legendary | 3200 | 3200 | 6400 | 9600 | 12800 | 16000 | 19200 | 22400 | 25600 | 28800 | 32000 |
| Quantainium | Legendary | 3170 | 3170 | 6340 | 9510 | 12680 | 15850 | 19020 | 22190 | 25360 | 28530 | 31700 |
| Stileron | Legendary | 3185 | 3185 | 6370 | 9555 | 12740 | 15925 | 19110 | 22295 | 25480 | 28665 | 31850 |
| Lindinium | Epic | 3400 | 3400 | 6800 | 10200 | 13600 | 17000 | 20400 | 23800 | 27200 | 30600 | 34000 |
| Ouratite | Epic | 3370 | 3370 | 6740 | 10110 | 13480 | 16850 | 20220 | 23590 | 26960 | 30330 | 33700 |
| Riccite | Epic | 3385 | 3385 | 6770 | 10155 | 13540 | 16925 | 20310 | 23695 | 27080 | 30465 | 33850 |
| Bexalite | Rare | 3600 | 3600 | 7200 | 10800 | 14400 | 18000 | 21600 | 25200 | 28800 | 32400 | 36000 |
| Gold | Rare | 3585 | 3585 | 7170 | 10755 | 14340 | 17925 | 21510 | 25095 | 28680 | 32265 | 35850 |
| Borase | Rare | 3570 | 3570 | 7140 | 10710 | 14280 | 17850 | 21420 | 24990 | 28560 | 32130 | 35700 |
| Taranite | Rare | 3555 | 3555 | 7110 | 10665 | 14220 | 17775 | 21330 | 24885 | 28440 | 31995 | 35550 |
| Beryl | Rare | 3540 | 3540 | 7080 | 10620 | 14160 | 17700 | 21240 | 24780 | 28320 | 31860 | 35400 |
| Tungsten | Uncommon | 3870 | 3870 | 7740 | 11610 | 15480 | 19350 | 23220 | 27090 | 30960 | 34830 | 38700 |
| Torite | Uncommon | 3900 | 3900 | 7800 | 11700 | 15600 | 19500 | 23400 | 27300 | 31200 | 35100 | 39000 |
| Agricium | Uncommon | 3885 | 3885 | 7770 | 11655 | 15540 | 19425 | 23310 | 27195 | 31080 | 34965 | 38850 |
| Titanium | Uncommon | 3855 | 3855 | 7710 | 11565 | 15420 | 19275 | 23130 | 26985 | 30840 | 34695 | 38550 |
| Aslarite | Uncommon | 3840 | 3840 | 7680 | 11520 | 15360 | 19200 | 23040 | 26880 | 30720 | 34560 | 38400 |
| Laranite | Uncommon | 3825 | 3825 | 7650 | 11475 | 15300 | 19125 | 22950 | 26775 | 30600 | 34425 | 38250 |
| Iron | Common | 4270 | 4270 | 8540 | 12810 | 17080 | 21350 | 25620 | 29890 | 34160 | 38430 | 42700 |
| Aluminum | Common | 4285 | 4285 | 8570 | 12855 | 17140 | 21425 | 25710 | 29995 | 34280 | 38565 | 42850 |
| Silicon | Common | 4255 | 4255 | 8510 | 12765 | 17020 | 21275 | 25530 | 29785 | 34040 | 38295 | 42550 |
| Copper | Common | 4240 | 4240 | 8480 | 12720 | 16960 | 21200 | 25440 | 29680 | 33920 | 38160 | 42400 |
| Corundum | Common | 4225 | 4225 | 8450 | 12675 | 16900 | 21125 | 25350 | 29575 | 33800 | 38025 | 42250 |
| Quartz | Common | 4210 | 4210 | 8420 | 12630 | 16840 | 21050 | 25260 | 29470 | 33680 | 37890 | 42100 |
| Hephaestanite | Common | 4180 | 4180 | 8360 | 12540 | 16720 | 20900 | 25080 | 29260 | 33440 | 37620 | 41800 |
| Tin | Common | 4195 | 4195 | 8390 | 12585 | 16780 | 20975 | 25170 | 29365 | 33560 | 37755 | 41950 |
| Ice | Common | 4300 | 4300 | 8600 | 12900 | 17200 | 21500 | 25800 | 30100 | 34400 | 38700 | 43000 |

## Collisions (plusieurs interprétations pour une même valeur)

| Signature | Interprétations |
|---|---|
| 19200 | 5× Aslarite, 6× Savrilium |
| 28800 | 8× Bexalite, 9× Savrilium |
| 30600 | 8× Laranite, 9× Lindinium |
| 38700 | 9× Ice, 10× Tungsten |

L'overlay de scan (cf. `Documents/Plans/2026-09-07-scan-signature-overlay.md`) affiche toutes les interprétations possibles pour une signature donnée.

## Minage à la main (FPS) et au véhicule terrestre (GroundVehicle)

Contrairement au minage vaisseau/surface, ces deux contextes utilisent une **signature générique par
catégorie**, identique pour tous les minéraux de la catégorie : impossible de distinguer le minéral exact
à partir de la seule signature. Ces rochers se trouvent aussi en clusters bien plus importants (une vingtaine
à une trentaine de rochers, contre 1 à 10 pour le minage vaisseau/surface), d'où la plage `ClusterSize` 20-30
utilisée pour ces entrées (`MiningType = "Fps"` ou `"GroundVehicle"` en base).

| Contexte | Signature de base | Minéraux (extraits et vérifiés sur le p4k LIVE 4.10) |
|---|---|---|
| FPS (minage à la main) | 3000 | Aphorite, Carinite, Carinite (Pure), Dolivine, Hadanite, Jaclium, Janalite, Sadaryx, Saldynium |
| GroundVehicle (véhicule terrestre, ex. ROC) | 4000 | Beradom, Carinite, Feynmaline, Glacosite |

(Carinite existe dans les deux contextes, avec deux signatures de base différentes — les deux entrées sont
conservées séparément, indexées par minéral **et** contexte de minage.)

Table générée (base × N, N = 20..30) :

| Contexte | Base | 20 | 21 | 22 | 23 | 24 | 25 | 26 | 27 | 28 | 29 | 30 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FPS | 3000 | 60000 | 63000 | 66000 | 69000 | 72000 | 75000 | 78000 | 81000 | 84000 | 87000 | 90000 |
| GroundVehicle | 4000 | 80000 | 84000 | 88000 | 92000 | 96000 | 100000 | 104000 | 108000 | 112000 | 116000 | 120000 |

⚠️ **Toute valeur de cette table correspond à N'IMPORTE LEQUEL des minéraux de la catégorie** (ex. `80000` =
"20× Beradom **ou** 20× Carinite **ou** 20× Feynmaline **ou** 20× Glacosite") : l'overlay de scan affiche
toutes les lignes correspondantes avec un `?` pour signaler cette ambiguïté, faute d'information suffisante
pour trancher entre les minéraux d'une même catégorie à partir de la signature seule.

**Collision entre catégories :** `84000` correspond à la fois à `28× (FPS, les 9 minéraux)` et `21× (GroundVehicle, les 4 minéraux)`, soit 13 interprétations simultanées.

**Vérifié sur p4k réel (LIVE 4.10)** : rebuild complet → 143 lignes FPS/GroundVehicle (9 minéraux FPS × 11 tailles + 4 minéraux GroundVehicle × 11 tailles), en plus des 260 lignes ship/surface (403 au total). La signature `80000` de la capture d'écran initiale **correspond exactement** à `20× GroundVehicle` (Beradom/Carinite/Feynmaline/Glacosite, ambigu) — c'est la meilleure explication disponible pour cette capture, sous réserve de confirmation en jeu.

Les entités p4k parasites (variantes non finalisées à signature 0, gabarits/placeholders comme
`mineablerock_fps_template`, `mineablerock_fps_floor_blocker_placeholder`) sont exclues comme pour le
minage vaisseau/surface.
