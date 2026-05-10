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

				// `speed` and `lifetime` are direct attributes on the root
				// AmmoParams element (NOT children of BulletProjectileParams,
				// despite the XML's nesting). `range` is derived as speed *
				// lifetime by WeaponExtractor — same convention scunpacked
				// uses when shipping the bulk ship-items.json.
				var speed = XmlHelpers.AttrDoubleNullable(root, "speed");
				var lifetime = XmlHelpers.AttrDoubleNullable(root, "lifetime");

				// basePenetrationDistance lives on a `penetrationParams`
				// element under BulletProjectileParams. Lowercase attribute
				// name on a lowercase element name — earlier code had it
				// camel-cased and missed every record on this branch.
				Double? bpd = null;
				var bullet = XmlNav.FindFirst(root, "BulletProjectileParams");
				if (bullet != null)
				{
					var pen = XmlNav.FindFirst(bullet, "penetrationParams");
					if (pen != null) bpd = XmlHelpers.AttrDoubleNullable(pen, "basePenetrationDistance");
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
