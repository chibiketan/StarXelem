using StarBreaker.DataCoreGenerated;
using StarXelem.Services;

namespace StarXelem.Cli.TestDb;

/// <summary>
/// Sonde de faisabilité pour l'écran « Armes FPS » : vérifie que la chaîne
/// arme → chargeur → munition est résolue à la profondeur utilisée par
/// PopulateScItemsAsync, et affiche les valeurs extraites pour comparaison manuelle.
/// </summary>
internal static class FpsWeaponProbe
{
    public static async Task RunAsync(IP4kService p4kService, string mannequinClassTag, int finalDepth)
    {
        Console.WriteLine($"=== Sonde armes FPS : '{mannequinClassTag}' à finalDepth={finalDepth} ===");

        var found = 0;

        var records = p4kService.GetAllEntityClassDefinitionFiltered(
            filterDepth: 1,
            finalDepth: finalDepth,
            predicate: ec =>
            {
                if (ec.Components.OfType<SAttachableComponentParams>().FirstOrDefault()?.AttachDef is not SItemDefinition def)
                    return false;
                return def.Type == EItemType.WeaponPersonal
                    && string.Equals(def.mannequinTags?.mannequinClassTag, mannequinClassTag, StringComparison.OrdinalIgnoreCase);
            });

        await foreach (var record in records)
        {
            if (record.Data is not EntityClassDefinition entityClass)
                continue;

            found++;
            var itemDef = entityClass.Components.OfType<SAttachableComponentParams>().First().AttachDef as SItemDefinition;
            var weapon = entityClass.Components.OfType<SCItemWeaponComponentParams>().FirstOrDefault();

            Console.WriteLine();
            Console.WriteLine($"--- {record.RecordName} ({record.RecordId})");
            Console.WriteLine($"    nom locale        : {itemDef?.Localization?.Name}");
            Console.WriteLine($"    type/sous-type    : {itemDef?.Type}/{itemDef?.SubType}  taille {itemDef?.Size} grade {itemDef?.Grade}");
            Console.WriteLine($"    tags              : {itemDef?.Tags}");
            Console.WriteLine($"    weaponParams      : {(weapon is null ? "NULL" : "ok")}");

            if (weapon is null)
                continue;

            var repool = weapon.ammoRepoolParams;
            Console.WriteLine($"    repool            : {(repool is null ? "NULL" : $"unstow={repool.unstowMagDuration}s bullets/s={repool.bulletsPerSecond} merge={repool.fullMagMergeDuration}s")}");

            var modifiers = weapon.weaponDegradationModifier?.weaponStats;
            Console.WriteLine($"    modificateurs     : {(modifiers is null ? "NULL" : $"dmgMult={modifiers.damageMultiplier} fireRateMult={modifiers.fireRateMultiplier} pellets={modifiers.pellets}")}");

            var mag = weapon.ammoContainerRecord;
            Console.WriteLine($"    chargeur (record) : {(mag is null ? "NULL" : "ok")}");

            var ammoContainer = mag?.Components.OfType<SAmmoContainerComponentParams>().FirstOrDefault();
            Console.WriteLine($"    SAmmoContainer    : {(ammoContainer is null ? "NULL" : $"maxAmmoCount={ammoContainer.maxAmmoCount}")}");

            DumpAmmo("  munition PRIMAIRE ", ammoContainer?.ammoParamsRecord);
            DumpAmmo("  munition SECOND.  ", ammoContainer?.secondaryAmmoParamsRecord);

            Console.WriteLine($"    modes de tir      : {weapon.fireActions?.Length ?? 0}");
            foreach (var action in weapon.fireActions ?? [])
            {
                if (action is null)
                    continue;

                var launcher = action switch
                {
                    SWeaponActionFireSingleParams s => s.launchParams as SProjectileLauncher,
                    SWeaponActionFireRapidParams r => r.launchParams as SProjectileLauncher,
                    SWeaponActionFireBurstParams b => b.launchParams as SProjectileLauncher,
                    _ => null
                };
                var beamDamage = (action as SWeaponActionFireBeamParams)?.damagePerSecond as DamageInfo;
                var fireRate = action switch
                {
                    SWeaponActionFireSingleParams s => s.fireRate,
                    SWeaponActionFireRapidParams r => r.fireRate,
                    SWeaponActionFireBurstParams b => b.fireRate,
                    _ => 0
                };
                Console.WriteLine($"      • {action.name,-12} [{action.GetType().Name}] localisé={action.localisedName} "
                    + $"cadence={fireRate} type={launcher?.projectileType.ToString() ?? "?"} "
                    + $"plombs={launcher?.pelletCount.ToString() ?? "?"} mult={launcher?.damageMultiplier.ToString() ?? "?"}"
                    + (beamDamage is null ? "" : $" dps(P/E)={beamDamage.DamagePhysical}/{beamDamage.DamageEnergy}"));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== {found} enregistrement(s) trouvé(s) pour '{mannequinClassTag}' ===");
    }

    private static void DumpAmmo(string label, AmmoParams? ammo)
    {
        if (ammo is null)
        {
            Console.WriteLine($"  {label}: NULL");
            return;
        }

        var maxRange = ammo.speed * ammo.lifetime;
        Console.WriteLine($"  {label}: vitesse={ammo.speed} durée={ammo.lifetime} → portée max={maxRange}m");

        var proj = ammo.projectileParams;
        if (proj is null)
        {
            Console.WriteLine($"  {label}  projectileParams : NULL  ← profondeur insuffisante");
            return;
        }

        var damage = proj switch
        {
            BulletProjectileParams b => b.damage as DamageInfo,
            TachyonProjectileParams t => t.damage as DamageInfo,
            _ => null
        };

        if (damage is null)
        {
            Console.WriteLine($"  {label}  damage : NULL (type {proj.GetType().Name})");
        }
        else
        {
            Console.WriteLine($"  {label}  dégâts : P={damage.DamagePhysical} E={damage.DamageEnergy} D={damage.DamageDistortion} "
                + $"T={damage.DamageThermal} B={damage.DamageBiochemical} S={damage.DamageStun}");
        }

        if (proj is not BulletProjectileParams bullet)
            return;

        var drop = bullet.damageDropParams;
        if (drop is null)
        {
            Console.WriteLine($"  {label}  damageDropParams : NULL (pas de chute de dégâts)");
            return;
        }

        var minDist = (drop.damageDropMinDistance as DamageInfo)?.DamagePhysical;
        var perMeter = (drop.damageDropPerMeter as DamageInfo)?.DamagePhysical;
        var minDamage = (drop.damageDropMinDamage as DamageInfo)?.DamagePhysical;
        Console.WriteLine($"  {label}  chute : début={minDist}m perMètre={perMeter} plancher={minDamage}");

        if (damage is not null && minDist is > 0 && perMeter is > 0 && minDamage is not null)
        {
            var floorRange = minDist + (damage.DamagePhysical - minDamage) / perMeter;
            Console.WriteLine($"  {label}  → portée efficace={minDist}m, distance de plancher={floorRange}m");
        }
    }
}
