using System.Collections.Generic;
using UnityEngine;

public static class WeaponCatalog
{
    private static readonly Dictionary<string, WeaponDefinition> DefinitionsById =
        new Dictionary<string, WeaponDefinition>(System.StringComparer.Ordinal);

    private static bool isInitialized;

    public static WeaponDefinition GetById(string weaponId)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            return null;
        }

        EnsureInitialized();
        DefinitionsById.TryGetValue(weaponId, out WeaponDefinition definition);
        return definition;
    }

    private static void EnsureInitialized()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        DefinitionsById.Clear();

        WeaponDefinition[] definitions = Resources.LoadAll<WeaponDefinition>("Weapons");
        for (int i = 0; i < definitions.Length; i++)
        {
            WeaponDefinition definition = definitions[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.WeaponId))
            {
                continue;
            }

            DefinitionsById[definition.WeaponId] = definition;
        }
    }
}
