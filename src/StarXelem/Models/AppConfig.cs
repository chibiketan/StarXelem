using System;
using System.IO;

namespace StarXelem.Models;

/// <summary>
/// Configuration CLI statique : --screen, --screenshot, --close, --load-items, --items-report
/// </summary>
static class AppConfig
{
    public static string? ScreenName { get; set; }
    public static string? ScreenshotPath { get; set; }
    public static bool AutoClose { get; set; }
    /// <summary>Lance un vrai chargement de l'écran Objets, comme un clic sur Charger.</summary>
    public static bool LoadItems { get; set; }
    /// <summary>Fichier JSON où écrire le détail des objets une fois le chargement terminé.</summary>
    public static string? ItemsReportPath { get; set; }

    public static void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--screen" && i + 1 < args.Length)
            {
                ScreenName = args[++i].ToLowerInvariant();
            }
            else if (arg == "--screenshot" && i + 1 < args.Length)
            {
                ScreenshotPath = Path.GetFullPath(args[++i]);
            }
            else if (arg == "--load-items")
            {
                LoadItems = true;
            }
            else if (arg == "--items-report" && i + 1 < args.Length)
            {
                ItemsReportPath = Path.GetFullPath(args[++i]);
                LoadItems = true;
            }
            else if (arg == "--close")
            {
                AutoClose = true;
            }
        }
    }
}
