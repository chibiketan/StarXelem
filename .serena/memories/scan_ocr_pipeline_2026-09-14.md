# Scan OCR — pipeline fiabilisé (2026-09-14, deux itérations)

Plan et mesures : `Documents/Plans/2026-09-14-scan-ocr-fiabilisation.md`. Voir aussi `mem:scan_signature_overlay_decisions`.

## Constat
Le badge « pin » de signature HUD fait ~15 px de haut en 4K (texte blanc, frange chromatique rouge/bleue) : limite
basse de `Windows.Media.Ocr`, résultat non déterministe selon l'échelle. Au sol (planète claire), le texte blanc
est sur un fond presque de même luminance : sans normalisation de contraste, rien n'est lu.

## Pipeline retenu (`Services/Scan/WindowsSignatureOcrService.cs`)
1. **Localisation en escalade** : zone centrale (60 % × 50 %) ×1 → zone « a priori » (30 % × 30 %, centrée y = 39 %) ×2
   → zone centrale ×2 → capture entière ×1 (jamais ×2 : 3 s). Chaque étage en 3 variantes : couleur, canal vert,
   vert **normalisé par tuiles 256 px** (percentiles 1–99 % + gamma 3). Étage suivant seulement si aucune valeur lue.
   Filtre de boîte : ≥ 4 chiffres, ≤ 1 séparateur, ponctuation de bordure ignorée (`15,300!`).
2. **Lecture par vote** : recadrage ±12× h horizontal / ±3× h vertical, 6 échelles (1…4) × {couleur, vert, vert
   normalisé, vert normalisé Lanczos3} = 24 passes, vote majoritaire, virgule en tie-break.
   Agrandir **avant** de normaliser (mesuré : ×3 → 15/18 contre 9/18 dans l'autre ordre).
3. Constantes dans `ScanConstants` (`OcrRoi*`, `OcrPriorZone*`, `OcrLocalizationScales`, `OcrReadingScales`,
   `OcrCropMargin*`, `OcrGamma`, `OcrNormalizeTileSize`, `OcrLocalizationMinDigits`). Plancher plausible 3 000.

## Pièges mesurés (ne pas refaire)
- Bilinéaire / Hamming / plus proche voisin : bien pires que bicubique (Skia High) ou Lanczos3 (`SKBitmapResizeMethod`, obsolète mais seul accès en 2.88).
- Marges serrées (1,5×/1×) : perte de contexte, lectures fausses. Plus large = mieux jusqu'à ±12× h.
- max(RGB) comme luminance : 0/18 (frange chromatique). Passe-haut (texte − flou) : 0/18. Gamma 3 > 2 > 4 > 6.
- Binarisation (2026-09-07) : contre-productive. La normalisation gamma n'est PAS une binarisation (anti-aliasing conservé).
- Les images de debug (`STARXELEM_SCAN_DEBUG_DIR`) faussent les temps (encodage PNG ~400 ms).

## Banc de non-régression
`dotnet run --project src\StarXelem.cli.testdb -c Release -- scan-image private\debug` (lit `expected.txt` du dossier ;
`STARXELEM_SCAN_VERBOSE=1` pour les mots bruts et durées par passe). État : 15/18 au sol (Lyria), 320–940 ms,
1,8–3 s quand l'escalade échoue. Les 4 captures espace de l'itération 1 étaient 4/4 mais ont été retirées du
dossier par l'utilisateur : à remettre pour la non-régression.

## Gabarits intégrés + auto-apprentissage (itération 4)
`IDigitGlyphStore`/`DigitGlyphStore` : JSON `%LOCALAPPDATA%\StarXelem\scan-glyphs.json`, graine embarquée
`Resources/scan-glyphs.json` (EmbeddedResource, 118 glyphes, regénérable par `scan-template … --export <fichier>`),
300 glyphes max par chiffre. Dans `ReadByVoteAsync`, la lecture gabarits devient un candidat `FromTemplates` classé
APRÈS l'OCR et qui n'arrête pas l'escalade (sinon l'altimètre masque le badge) ; l'orchestrateur affiche le premier
candidat connu en base → arbitrage 18,960 → 16,960. Auto-apprentissage sur vote OCR ≥ 8 et ≥ 80 %, désactivé dans
les bancs (`scan-image … --learn` le rejoue sur le fichier STARXELEM_SCAN_GLYPHS). Banc : 16/18 + 4/4 (graine = mêmes
captures, réserve de validation).
**Auto-empoisonnement vécu** : l'OCR unanime 18,960 a étiqueté un 6 comme 8 → gabarits cassés (15/18). Garde-fous :
pas d'apprentissage si les gabarits de qualité contredisent l'OCR ; `Learn` refuse le badge si un glyphe corrèle
≥ 0,97 avec une autre étiquette, ignore les doublons ≥ 0,995. Base polluée : supprimer
`%LOCALAPPDATA%\StarXelem\scan-glyphs.json` (graine rechargée).

## Expérimentation gabarits (itération 3, non branchée à l'origine)
`Services/Scan/DigitTemplateReader.cs` + `scan-template <dossiers>` (LOO). 13/22 contre 19/22 pour l'OCR, ~5 ms par
lecture. Résout la confusion 6/8 de l'OCR (4/4 `16,960`), échoue sur segmentation (glyphes fusionnés) et manque
de données (k-NN sur 22 captures). Plus proche voisin >> gabarit moyen ; conserver le rapport largeur/hauteur des
glyphes. Suite recommandée : auto-apprentissage depuis les votes OCR unanimes, puis votant supplémentaire dans
`ReadByVoteAsync`. Helpers image partagés : `ScanImageOps` ; `LocalizeBoxesAsync` expose la localisation seule.

## Ouvert
- 3 échecs au sol : `64,000` sur ciel clair, un `16,960` sur relief (jamais reconnus) et un `16,960` lu `18,960`
  à l'unanimité (confusion 6/8 de cette police). Piste : gabarits de chiffres (police HUD fixe).
- `7,195`, `13,212`, `11,475`, `17,080`, `16,960`… : beaucoup de valeurs hors table « N × base » → hypothèse à
  revoir (tâche 5 du plan). Overlay « signature inconnue » + 2 voisins en attendant.
