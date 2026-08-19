using System;

namespace GenesisSoldierSoul.Catalog
{
    public enum GenesisCatalogAvailability
    {
        Equipped,
        Available,
        StandardIssue,
        RecoveryCandidate,
    }

    public struct GenesisStoreCatalogEntry
    {
        public string Id;
        public string DisplayName;
        public string ResourcePath;
        public GenesisCatalogAvailability Availability;
        public bool CanEquipPrimary;
    }

    /// <summary>
    /// Evidence-backed read-only catalog. It intentionally has no price,
    /// currency, balance, ownership purchase or transaction API.
    /// </summary>
    public static class GenesisStoreCatalog
    {
        public static GenesisStoreCatalogEntry[] Build(
            string equippedPrimary,
            bool experimentalWeaponsEnabled)
        {
            return new[]
            {
                Primary("m4a1", "M4A1",
                    "OriginalGame/FirstPerson/M4A1Viewmodel",
                    equippedPrimary, true),
                Primary("m16", "M16",
                    "OriginalGame/FirstPerson/M16ViewmodelCandidate",
                    equippedPrimary, true),
                Primary("shotgun01", "SHOTGUN 01",
                    "OriginalGame/FirstPerson/RecoveredClosures/Shotgun01/"
                        + "GameObject/Shotgun01",
                    equippedPrimary, true),
                Standard("pistol", "PISTOL 01",
                    "OriginalGame/FirstPerson/Pistol/Pistol01"),
                Standard("knife", "KNIFE 01",
                    "OriginalGame/FirstPerson/Knife/Knife01"),
                AvailableEquipment("axe", "MILITARY AXE",
                    "OriginalGame/HandAxe"),
                AvailableEquipment("nepal", "NEPAL KNIFE",
                    "OriginalGame/Nepal"),
                Standard("grenade", "GRENADE 01",
                    "OriginalGame/FirstPerson/RecoveredClosures/Grenade01/"
                        + "GameObject/Grenade01"),
                Primary("ak74m", "AK-74M",
                    "OriginalGame/FirstPerson/AK74MViewmodelCandidate",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("awp", "AWM",
                    "OriginalGame/FirstPerson/AWPViewmodelCandidate",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("an94", "AN94",
                    "OriginalGame/FirstPerson/AN94ViewmodelCandidate",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("m249", "M249",
                    "OriginalGame/M249",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("famas", "FAMAS",
                    "OriginalGame/FAMAS",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("microgalil_baxi", "MICRO GALIL BRAZIL",
                    "OriginalGame/MicroGalilBaxi",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("gatling", "GATLING",
                    "OriginalGame/Gatling",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("auga1", "AUG A1",
                    "OriginalGame/AUGA1",
                    equippedPrimary, experimentalWeaponsEnabled),
                Primary("ak47_bingzuan", "ICE AK47",
                    "OriginalGame/AK47Ice",
                    equippedPrimary, experimentalWeaponsEnabled),
            };
        }

        private static GenesisStoreCatalogEntry Primary(
            string id,
            string displayName,
            string resourcePath,
            string equippedPrimary,
            bool available)
        {
            var equipped = available
                && string.Equals(id, equippedPrimary, StringComparison.Ordinal);
            return new GenesisStoreCatalogEntry
            {
                Id = id,
                DisplayName = displayName,
                ResourcePath = resourcePath,
                Availability = equipped
                    ? GenesisCatalogAvailability.Equipped
                    : available
                        ? GenesisCatalogAvailability.Available
                        : GenesisCatalogAvailability.RecoveryCandidate,
                CanEquipPrimary = available,
            };
        }

        private static GenesisStoreCatalogEntry Standard(
            string id, string displayName, string resourcePath)
        {
            return new GenesisStoreCatalogEntry
            {
                Id = id,
                DisplayName = displayName,
                ResourcePath = resourcePath,
                Availability = GenesisCatalogAvailability.StandardIssue,
                CanEquipPrimary = false,
            };
        }

        private static GenesisStoreCatalogEntry AvailableEquipment(
            string id, string displayName, string resourcePath)
        {
            return new GenesisStoreCatalogEntry
            {
                Id = id,
                DisplayName = displayName,
                ResourcePath = resourcePath,
                Availability = GenesisCatalogAvailability.Available,
                CanEquipPrimary = false,
            };
        }
    }
}
