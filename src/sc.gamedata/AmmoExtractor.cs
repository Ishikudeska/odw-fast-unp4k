using System;
using System.Collections.Generic;
using unforge;

namespace sc.gamedata
{
	// Walks DataForge ammoparams/vehicle/*.xml records once to build a
	// guid → damage-profile map, indexed by the record's `__ref` GUID. Guns
	// reference these by GUID through SAmmoContainerComponentParams's
	// ammoParamsRecord attribute, so this map is the join key for
	// WeaponExtractor's gun pass.
	internal static class AmmoExtractor
	{
		private const String PathPrefix = "libs/foundry/records/ammoparams/vehicle/";

		public static Dictionary<String, AmmoEntry> Extract(DataForge df)
		{
			var result = new Dictionary<String, AmmoEntry>(StringComparer.OrdinalIgnoreCase);
			foreach (var path in df.PathToRecordMap.Keys)
			{
				if (!path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)) continue;
				var root = df.ReadRecordByPathAsXml(path);
				if (root == null) continue;
				var guid = XmlHelpers.Attr(root, "__ref");
				if (String.IsNullOrEmpty(guid)) continue;

				// Damage lives at projectileParams/BulletProjectileParams/damage/DamageInfo
				// (path mirrors what the seed Python extractor walks).
				var damageInfo = XmlNav.FindFirst(root, "DamageInfo");
				if (damageInfo == null) continue;
				var dmg = XmlHelpers.DamageFrom(damageInfo);

				// BasePenetrationDistance lives on BulletProjectileParams.Penetration —
				// scunpacked exposes it as base_penetration_distance and the
				// analyzer uses it for armor-pen calculations.
				Double? bpd = null;
				var bullet = XmlNav.FindFirst(root, "BulletProjectileParams");
				if (bullet != null)
				{
					var pen = XmlNav.FindFirst(bullet, "Penetration");
					if (pen != null) bpd = XmlHelpers.AttrDoubleNullable(pen, "BasePenetrationDistance");
				}

				// Speed + Lifetime — used to derive `range` for guns.
				Double? speed = null;
				Double? lifetime = null;
				if (bullet != null)
				{
					speed = XmlHelpers.AttrDoubleNullable(bullet, "Speed");
					lifetime = XmlHelpers.AttrDoubleNullable(bullet, "Lifetime");
				}

				result[guid] = new AmmoEntry
				{
					damage = dmg,
					base_penetration_distance = bpd,
					speed = speed,
					lifetime = lifetime,
				};
			}
			return result;
		}
	}

	internal sealed class AmmoEntry
	{
		public DamageProfile damage = new();
		public Double? base_penetration_distance;
		public Double? speed;
		public Double? lifetime;
	}
}
