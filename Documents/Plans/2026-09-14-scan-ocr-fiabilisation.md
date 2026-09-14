# Plan : fiabiliser la lecture OCR de la signature radar

## État d'exécution (2026-09-14)

Tâches 1 à 4 implémentées, `dotnet build src\StarXelem.sln` → 0 erreur. Résultat du banc sur les 4 captures de
`private/debug/` (`dotnet run --project src\StarXelem.cli.testdb -c Release -- scan-image private\debug --expect 19425,16900,7195,13212`) :

| Capture | Valeur | Lu | Votes | Durée |
|---|---|---|---|---|
| 22-30-16 | 19,425 | ✓ | 2/3 | ~230 ms |
| 22-40-14 | 16,900 | ✓ | 4/4 | ~1 100 ms (étage ×2) |
| 22-40-36 | 7,195 | ✓ | 9/9 | ~220 ms |
| 23-31-09 | 13,212 | ✓ | 11/11 | ~160 ms |

**4/4**, stable sur plusieurs exécutions. Écarts par rapport au plan initial, tous mesurés au banc :

- La localisation utilise **couleur ET canal vert** (l'un ou l'autre seul manque le badge selon la capture).
- La localisation est **en escalade** (zone centrale ×1 → zone centrale ×2 → capture entière ×1 → ×2), l'étage
  suivant n'étant tenté que si aucune valeur n'a été lue. Le ×2 d'une zone 2304×1080 coûte ~170 ms de resize +
  90–220 ms de reconnaissance : trop cher pour le faire systématiquement, indispensable sur 1 capture sur 4.
- `SKFilterQuality.High` pour l'agrandissement : le bilinéaire est 10× plus rapide à redimensionner mais
  rend la reconnaissance 2,5× plus lente (image moins nette), bilan nul.
- Marge de recadrage autour de la boîte localisée : **4× la hauteur** horizontalement, **3×** verticalement.
  Avec une marge serrée (1,5×/1×), `16,900` n'obtenait que 3 lectures valides dont 2 fausses : l'OCR a besoin de contexte.
- Le vote pondère les lectures avec virgule (`13,212` > `13212`) à égalité de fréquence.
- Piste non explorée pour accélérer l'étage ×2 : détection de l'icône « pin » par couleur (option B, §3 tâche 2).

Tâche 4 : signature illisible → overlay « Aucune signature lisible » au centre ; signature lue mais absente de la
base → overlay « 7 195 : signature inconnue » + les 2 voisins les plus proches (`IMineralSignatureRepository.FindNearestAsync`).
`SignatureCandidate` expose `Votes` / `ValidPasses` / `Confidence`.

**Non vérifié en jeu** : l'overlay réel (texte, position) et le comportement sur capture GDI (le banc tourne sur
des JPG du jeu ; la capture GDI est sans compression, donc a priori plus favorable).

## Itération 2 — surface de planète (Lyria), 18 captures du 2026-09-14

Retour en jeu : espace OK, **< 10 % au sol** (et la boussole lue comme signature). Sur les 18 captures fournies
(`private/debug/`, vérité terrain dans `expected.txt`), le pipeline de l'itération 1 lisait **3/18**. Causes :

1. **Contraste** : texte blanc sur sol de planète gris clair — le moteur ne segmente plus le badge. Aucune passe
   couleur/vert ne le trouve, même en localisation.
2. **Boîtes monopolisées** : l'altimètre (`7.44`, `7.39`…) et la boussole (`260`, `280`) passaient la regex
   « nombre bruité » et occupaient les 3 boîtes les plus proches du centre.
3. **Ponctuation collée** : l'OCR renvoie `15,300!` ou `'19,425` — rejetés par le parseur strict.
4. L'escalade allait jusqu'à la capture entière ×2 : 3 s pour ne rien trouver.

Corrections, toutes mesurées au banc (valeurs = fichiers lus / 18) :

| Étape | Résultat |
|---|---|
| Point de départ | 3/18 |
| Normalisation de contraste (percentiles 1–99 % + **gamma 3**) sur le canal vert, par tuiles de 256 px pour la localisation, globale pour la lecture | 14/18 |
| Localisation : ≥ 4 chiffres, ≤ 1 séparateur ; étage « zone a priori » (30 % × 30 % centré sur y = 39 %) à ×2 avant la zone centrale ×2 ; marges de lecture ±12× h horizontal | 15/18 |
| Normalisation **après** agrandissement (et non avant) ; variante Lanczos3 en plus du bicubique ; nettoyage de la ponctuation en bordure | 15/18, votes 2–3× plus nombreux (ex. 15/15) |

Autres mesures : max(RGB) comme luminance est catastrophique (frange chromatique) → canal vert uniquement ;
filtre passe-haut (texte − flou) inutilisable ; bilinéaire/Hamming/plus-proche-voisin bien pires que
bicubique/Lanczos pour l'agrandissement ; gamma 3 > 2 > 4 > 6. Plancher de plausibilité relevé à 3 000
(le cap « 258° » était lu 2582).

Durées : 320–940 ms en lecture directe, 1,8–3 s quand l'escalade va jusqu'au bout (cas non lus).

**Restent 3 échecs** : `64,000` sur ciel clair et un `16,960` sur relief (jamais reconnus, quelle que soit la
variante), et un `16,960` lu `18,960` à l'unanimité (confusion `6`/`8` propre à cette police). Pistes si besoin :
reconnaissance des chiffres par gabarits (la police HUD est fixe) plutôt que par OCR générique ; ou, pour la
confusion 6/8, proposer dans l'overlay les substitutions à un chiffre qui existent en base.

---

## 1. Diagnostic (4 captures 4K du 2026-09-08, `private/debug/`)

La valeur à lire est le **badge « pin » orange** affiché au-dessus de la cible (`19,425`, `16,900`, `7,195`, `13,212`),
et non le texte `UNKNOWN / 30.5km`. Deux problèmes distincts expliquent l'absence de popup :

### 1.1 L'OCR est à la limite de lisibilité (problème technique)

Le texte du badge fait **~15 px de haut en 4K**, en blanc avec une forte frange chromatique rouge/bleue (effet de
post-process du jeu), sur un fond semi-transparent. C'est la limite basse de `Windows.Media.Ocr`, qui devient alors
**non déterministe selon l'échelle** de l'image qu'on lui donne. Mesures faites avec un banc de test (Windows OCR sur les JPG) :

| Capture | Valeur | Pleine image ×1 (pipeline actuel) | Pleine ×2 | Centre ×1 / ×2 / ×3 |
|---|---|---|---|---|
| 22-30-16 | 19,425 | `19425` ✓ | — | `19425` / — / `19/05` |
| 22-40-14 | 16,900 | `16*00` ✗ | — | — / `1000` / — |
| 22-40-36 | 7,195 | `7,195` ✓ | ✓ | ✓ / ✓ / ✓ |
| 23-31-09 | 13,212 | — | `13212` ✓ | ✓ / ✓ / ✓ |

Le pipeline actuel (image entière, ×1 au-dessus de 2560 px, une seule passe) lit donc ~50 % des cas, et un simple
changement d'échelle fait basculer le résultat dans un sens ou dans l'autre. Le même banc sur un **recadrage serré du
badge (350×120 px)**, canal vert seul (supprime la frange chromatique), 6 échelles (×1 à ×4) :

| Capture | Passes couleur correctes | Passes « vert » correctes | Coût total 12 passes |
|---|---|---|---|
| 22-30-16 | 2/6 | 2/6 (+2 « presque » : `191425`, `19A25`) | ~60 ms |
| 22-40-14 | 2/6 | 2/6 | ~50 ms |
| 22-40-36 | 4/6 | 6/6 | ~50 ms |
| 23-31-09 | 6/6 | 5/6 | ~60 ms |

Aucune passe seule n'est fiable, mais **un vote majoritaire sur les passes valides (format `\d{1,3},\d{3}`, plage
plausible) donne la bonne valeur sur les 4 captures**, pour un coût inférieur à la passe unique actuelle sur l'image
4K entière (130–210 ms).

### 1.2 Les valeurs lues ne sont pas toutes en base (problème de données)

| Valeur | Correspondance table `MineralSignatures` |
|---|---|
| 19,425 | 5× Agricium ✓ |
| 16,900 | 4× Corundum ✓ |
| 7,195 | **aucune** (voisins : 7,170 = 2× Gold ; 7,200 = 2× Bexalite) |
| 13,212 | **aucune** (voisins : 13,480 = 4× Ouratite ; 12,900 = 3× Ice) |

Deux valeurs sur quatre ne sont **multiples d'aucune signature de base** : l'hypothèse « signature(cluster) = N × base »
(cf. plan du 2026-09-07, §0.1, jamais validée en jeu) est **au moins partiellement fausse**. Causes plausibles :
cluster mixte (plusieurs minéraux), signature pondérée par la masse de chaque rocher, arrondi d'affichage.
Tant que ce point n'est pas élucidé, même un OCR parfait ne produira pas de popup dans ces cas : il faut au minimum
**remonter la valeur lue à l'utilisateur** (overlay « 7 195 : signature inconnue ») au lieu de ne rien afficher.

## 2. Réponses aux questions

- **Peut-on améliorer la détection ?** Oui, nettement, sans changer de moteur : recadrage serré + canal vert +
  vote multi-échelles (données ci-dessus).
- **Faut-il changer de bibliothèque ?** Non. Windows OCR lit correctement le texte dès qu'on lui donne un recadrage
  adapté ; le défaut est en amont (localisation/échelle), pas dans le moteur. Tesseract serait pire sur du texte
  anti-aliasé de 15 px ; PaddleOCR/RapidOCR (ONNX) seraient plus robustes mais ajoutent ~50 Mo de dépendances et un
  runtime natif, à ne considérer qu'en dernier recours (voir §4).
- **Limiter la zone de recherche ?** Oui, c'est le levier principal : ×5 à ×10 plus rapide (ce qui finance le vote
  multi-passes) et moins de faux candidats (`2809`, `1000/0`, `#83131`…).
- **Zone manuelle ou automatique ?** **Automatique**, en deux temps : une zone centrale proportionnelle large
  (tolérante au head tracking, sans réglage) pour localiser grossièrement le badge, puis un recadrage serré autour du
  badge trouvé. Une zone manuelle serait soit trop large pour aider (head tracking), soit trop étroite pour être
  fiable. On la garde comme option de repli éventuelle (§4), pas comme fonctionnalité de départ.

## 3. Plan d'action

> Ordre recommandé. Chaque tâche se termine par un `dotnet build src\StarXelem.sln` vert et une vérification sur les
> 4 captures de `private/debug/`. Commits en français, format conventional commits (`feat(scan): …`).

### Tâche 1 — Banc de test OCR reproductible (prérequis)

Le banc utilisé pour ce diagnostic n'est pas dans le dépôt (comme celui du 2026-09-07, cité dans les commentaires
de `WindowsSignatureOcrService.Preprocess` mais perdu). Le remettre dans le dépôt pour que chaque ajustement soit mesuré :

- Ajouter une commande au projet `src/StarXelem.Cli` (ex. `scan-image <png|jpg>…`) qui appelle
  `ISignatureOcrService.FindSignatureCandidatesAsync` sur un fichier image (via un `CapturedFrame` construit depuis
  `SKBitmap.Decode`) et affiche les candidats, la valeur retenue et la durée. Nécessite de passer `StarXelem.Cli` au
  TFM `net10.0-windows10.0.19041.0` (ou d'isoler l'OCR dans un projet partagé).
- Critère de réussite du plan : **4/4 valeurs correctes** sur `private/debug/`, en moins de 300 ms par image.
- Alimenter `private/debug/` avec les prochaines captures ratées : c'est le jeu de non-régression.

### Tâche 2 — Localisation automatique du badge (zone de recherche)

Fichiers : `Services/Scan/WindowsSignatureOcrService.cs`, `Constants/ScanConstants.cs`.

1. **Zone grossière** : recadrer la capture sur une zone centrale proportionnelle (`ScanConstants.RoiWidthRatio = 0.6`,
   `RoiHeightRatio = 0.5`, centrée). Sur les 4 captures, le badge est à moins de 10 % du centre malgré le head tracking ;
   la zone laisse une marge confortable.
2. **Passe de localisation** : OCR de cette zone en ×1 **et** ×2 (canal vert, ~30 + 90 ms). Retenir toutes les boîtes de
   mots dont le texte ressemble à un nombre bruité (`^[\dOolI,./*A]{4,7}$`) — le but n'est pas de lire la valeur mais de
   trouver **où** elle est. Trier par distance au centre.
3. Si aucune boîte : élargir à la capture entière (même passes ×1/×2) avant d'abandonner.

Option B (à garder en réserve si la localisation par OCR grossier rate trop souvent) : détecter l'icône « pin »
orange par couleur (teinte 15–40°, saturation > 0,6) — blob d'environ 20×30 px en 4K, immédiatement à gauche du
nombre — par balayage à pas grossier de la zone centrale. Plus robuste en théorie, mais l'HUD contient beaucoup
d'orange ; ne l'implémenter qu'avec des mesures qui le justifient.

### Tâche 3 — Lecture fine par vote multi-échelles

Fichier : `Services/Scan/WindowsSignatureOcrService.cs`.

1. Pour chaque boîte candidate (au plus 3), extraire un recadrage serré avec marge (`~2× la hauteur du mot` autour),
   convertir en niveaux de gris **canal vert seul** (mesuré : supprime la frange chromatique, +30 % de passes correctes
   sur les cas difficiles). Sauver le recadrage en mode debug (`STARXELEM_SCAN_DEBUG_DIR`).
2. Lancer l'OCR sur ce recadrage à **6 échelles** (`1, 1.5, 2, 2.5, 3, 4`), en couleur et en vert (12 passes, < 80 ms
   mesurés). Les passes sont indépendantes : `Task.WhenAll` possible.
3. Normaliser chaque mot (`O→0`, `l/I→1`, `A→4` optionnel), ne garder que ceux qui matchent
   `^\d{1,3}(,\d{3})+$|^\d{4,6}$` et la plage `[MinPlausibleSignature, MaxPlausibleSignature]`. Rejeter explicitement
   les lectures avec `/` ou `*` (`19/05`, `16*00`).
4. **Vote** : la valeur la plus fréquente gagne ; en cas d'égalité, préférer la forme avec virgule (`13,212` plutôt que
   `13212`, la virgule est un indice que les chiffres ont été bien segmentés) puis la valeur présente en base.
   Exposer le score (nb de passes concordantes / nb de passes valides) dans `SignatureCandidate` pour le log et,
   plus tard, pour l'overlay.
5. Supprimer la logique actuelle « pleine image ×1 ou ×3 selon `OcrUpscaleThreshold` » : elle devient inutile,
   le recadrage est agrandi quelle que soit la résolution d'origine (une fenêtre 1080p bénéficie encore plus du vote).

Vérification : commande `scan-image` de la tâche 1 → 4/4 sur `private/debug/`, chacune < 300 ms.

### Tâche 4 — Ne jamais échouer en silence (retour utilisateur)

Fichiers : `Services/Scan/ScanSignatureOrchestrator.cs`, `Services/Scan/OverlayNotificationService.cs`, overlay.

- Valeur lue mais **absente de la base** → overlay « `7 195` — signature inconnue » (plutôt qu'aucun affichage), avec
  les 2 voisins les plus proches en base à titre indicatif (« ≈ 2× Gold 7 170 ? »). C'est aussi ce qui permettra à
  l'utilisateur de collecter les données de la tâche 5.
- Aucune valeur lue → overlay court « Aucune signature lisible » au centre de l'écran.
- En mode debug, écrire à chaque scan `capture.png`, `roi.png`, `badge_*.png` et un `scan.log` (valeur, score,
  durées capture/localisation/lecture/BDD) : c'est le matériau à demander à l'utilisateur quand ça rate.

### Tâche 5 — Valider (ou corriger) la formule de signature (données)

Sans code au départ : **collecter en jeu** une dizaine de couples (valeur affichée, composition réelle du cluster
après scan/minage : minéral(s), nombre de rochers, masse). Hypothèses à départager :

- cluster mixte → signature = Σ base(minéral_i) ; la table `N × base` ne couvre que les clusters purs ;
- pondération par la masse → signature = base × f(masse) ; aucune table exacte n'est possible, il faut passer à une
  recherche par **ratio** (valeur / base ≈ nombre de rochers, avec tolérance) et afficher « ≈ 2× Gold (7 195 / 3 585 = 2,01) » ;
- arrondi → simple `SignatureTolerance` > 0.

Selon le résultat, seul `Services/Mining/MineralSignatureGenerator.cs` et/ou `MineralSignatureRepository.FindBySignatureAsync`
sont à adapter (le reste du pipeline reste valable). `7,195 / 3,585 = 2,007` et `13,212 / 4,300 = 3,07`, `13,212 / 3,300 = 4,004`
(Savrilium 3 200 ? Quantainium 3 170 ?) : le mode « ratio avec tolérance ~1 % » est déjà un bon candidat.

### Tâche 6 — Finitions

- Mettre à jour `Documents/Plans/2026-09-07-scan-signature-overlay.md` (§0.3 OCR) et la mémoire projet
  (`scan_signature_overlay_decisions`) avec les mesures et les nouvelles constantes.
- Nettoyer `ScanConstants` (`OcrUpscaleThreshold`, `OcrUpscaleFactor` deviennent la liste d'échelles du vote et les
  ratios de ROI).

## 4. Options écartées (pour l'instant)

| Option | Pourquoi pas maintenant | Quand y revenir |
|---|---|---|
| Zone de recherche manuelle (réglage dans Paramètres) | Incompatible avec le head tracking sauf zone large, donc sans gain | Si la localisation auto rate sur des HUD/résolutions non prévus (ultra-wide, FOV modifié) |
| Tesseract | Moins bon que Windows OCR sur texte anti-aliasé de 15 px ; binaire + traineddata à distribuer | Jamais, sauf perte de Windows OCR |
| PaddleOCR / RapidOCR (ONNX Runtime) | ~50 Mo, runtime natif, packaging MS Store à valider | Si après tâches 2–3 le taux reste < 90 % sur le jeu de non-régression |
| Template matching de l'icône « pin » | Plus de code, HUD très orange → faux positifs probables | Option B de la tâche 2 |
| Binarisation / seuillage de teinte | Déjà mesuré contre-productif (2026-09-07) ; le canal vert suffit | — |
