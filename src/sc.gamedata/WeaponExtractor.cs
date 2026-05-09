using System;
using System.Collections.Generic;
using System.Xml;
using unforge;

namespace sc.gamedata
{
	internal static class WeaponExtractor
	{
		private const String GunPathPrefix = "libs/foundry/records/entities/scitem/ships/weapons/";
		private const String MissilePathPrefix = "libs/foundry/records/entities/scitem/ships/weapons/missiles/";

		// Returns (guns, missiles). Both lists carry the same WeaponRecord
		// shape; `kind` distinguishes them and missile-only fields stay null
		// on guns (and vice versa).
		public static (List<WeaponRecord> guns, List<WeaponRecord> missiles) Extract(
			DataForge df,
			IDictionary<String, String> loc,
			IReadOnlyDictionary<String, AmmoEntry> ammoMap)
		{
			var guns = new List<WeaponRecord>();
			var missiles = new List<WeaponRecord>();

			foreach (var path in df.PathToRecordMap.Keys)
			{
				var isMissile = path.StartsWith(MissilePathPrefix, StringComparison.OrdinalIgnoreCase);
				var isGun = !isMissile && path.StartsWith(GunPathPrefix, StringComparison.OrdinalIgnoreCase);
				if (!isGun && !isMissile) continue;
				// Subdirectories under weapons/ that aren't `missiles/` may
				// contain non-weapon entities (turret platforms, mounts).
				// Filter to direct children of the weapons folder for guns,
				// and missiles/ direct children for missiles.
				var rest = path.Substring(isMissile ? MissilePathPrefix.Length : GunPathPrefix.Length);
				if (rest.Contains('/')) continue;

				var root = df.ReadRecordByPathAsXml(path);
				if (root == null) continue;

				if (isMissile)
				{
					var m = ParseMissile(root, loc);
					if (m != null) missiles.Add(m);
				}
				else
				{
					var g = ParseGun(root, loc, ammoMap);
					if (g != null) guns.Add(g);
				}
			}
			return (guns, missiles);
		}

		// SAttachableComponentParams/AttachDef carries Size + Localization.Name
		// on every attachable item — it's the first place to look for the
		// in-world display name plus the canonical hardpoint size.
		private static (Int32 size, String? locKey) AttachSizeAndLoc(XmlElement root)
		{
			var attach = XmlNav.FindFirst(root, "AttachDef");
			var size = XmlHelpers.AttrInt(attach, "Size", 0);
			XmlElement? locElem = null;
			if (attach != null)
			{
				foreach (XmlNode c in attach.ChildNodes)
				{
					if (c is XmlElement e && e.LocalName == "Localization") { locElem = e; break; }
				}
			}
			var locKey = locElem != null ? XmlHelpers.Attr(locElem, "Name") : null;
			return (size, locKey);
		}

		private static WeaponRecord? ParseGun(
			XmlElement root,
			IDictionary<String, String> loc,
			IReadOnlyDictionary<String, AmmoEntry> ammoMap)
		{
			var entityId = XmlHelpers.EntityIdFromRoot(root);
			var weaponGuid = XmlHelpers.Attr(root, "__ref");

			// SAmmoContainerComponentParams.ammoParamsRecord holds the GUID
			// pointing at the ammo profile we built in AmmoExtractor.
			var ammoContainer = XmlNav.FindFirst(root, "SAmmoContainerComponentParams");
			if (ammoContainer == null) return null;
			var ammoGuid = XmlHelpers.Attr(ammoContainer, "ammoParamsRecord");
			if (String.IsNullOrEmpty(ammoGuid) || !ammoMap.TryGetValue(ammoGuid, out var ammo)) return null;

			// Skip weapons whose ammo entry has zero damage on every axis —
			// those are placeholder/dummy entries, same logic as the seed
			// Python extractor.
			if (ammo.damage.phys == 0 && ammo.damage.energy == 0 && ammo.damage.dist == 0
				&& ammo.damage.therm == 0 && ammo.damage.bio == 0 && ammo.damage.stun == 0)
				return null;

			var (size, locKey) = AttachSizeAndLoc(root);
			var fallback = NameResolver.PrettifyEntityId(entityId, "");
			var name = NameResolver.Resolve(locKey, loc, fallback, entityId);

			// Fire rate: SWeaponActionFireSingleParams (or SWeaponActionFireBurstParams)
			// carries fireRate in RPM and heatPerShot. We sum bursts and grab
			// the dominant firing action when there are multiple.
			Double? fireRate = null;
			Double? heatPerShot = null;
			Int32? capacity = null;
			foreach (var fireAct in XmlNav.FindAll(root, "SWeaponActionFireSingleParams"))
			{
				fireRate ??= XmlHelpers.AttrDoubleNullable(fireAct, "fireRate");
				heatPerShot ??= XmlHelpers.AttrDoubleNullable(fireAct, "heatPerShot");
			}
			foreach (var fireAct in XmlNav.FindAll(root, "SWeaponActionFireBurstParams"))
			{
				fireRate ??= XmlHelpers.AttrDoubleNullable(fireAct, "fireRate");
				heatPerShot ??= XmlHelpers.AttrDoubleNullable(fireAct, "heatPerShot");
			}

			// Magazine capacity: SAmmoContainerComponentParams.maxAmmoCount
			// is the in-world reload size. Zero means unlimited / not used.
			var capInt = XmlHelpers.AttrInt(ammoContainer, "maxAmmoCount");
			if (capInt > 0) capacity = capInt;

			// Heat behavior: SWeaponSimplifiedHeatParams is CIG's firing-heat
			// model (separate from <temperature>, which governs thermal
			// signature / IR detection — same enableOverheat="0" pattern shows
			// up there for every weapon). Presence of this element is what
			// signals an overheat-capable gun; weapons without it
			// (e.g. AMRS_LaserCannon_S1) intentionally never overheat.
			Double? heatCapacity = null;
			Double? coolingDelay = null;
			Double? coolingPerSecond = null;
			Double? overheatFixTime = null;
			Boolean overheatEnabled = false;
			var simplifiedHeat = XmlNav.FindFirst(root, "SWeaponSimplifiedHeatParams");
			if (simplifiedHeat != null)
			{
				overheatEnabled = true;
				heatCapacity = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "overheatTemperature");
				coolingPerSecond = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "coolingPerSecond");
				coolingDelay = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "timeTillCoolingStarts");
				overheatFixTime = XmlHelpers.AttrDoubleNullable(simplifiedHeat, "overheatFixTime");
			}

			// Derive shots/time-to-overheat the same way scunpacked does so
			// existing battlestations sim code keeps working unchanged.
			Int32? shotsToOverheat = null;
			Double? timeToOverheat = null;
			if (overheatEnabled && heatCapacity is > 0 && heatPerShot is > 0)
			{
				shotsToOverheat = (Int32)Math.Floor(heatCapacity.Value / heatPerShot.Value);
				if (fireRate is > 0)
					timeToOverheat = shotsToOverheat.Value / (fireRate.Value / 60.0);
			}

			Double? alpha = null;
			if (ammo.damage is { } d)
				alpha = d.phys + d.energy + d.dist + d.therm + d.bio + d.stun;
			Double? dpsBurst = null;
			if (alpha is not null && fireRate is > 0) dpsBurst = alpha.Value * (fireRate.Value / 60.0);

			Double? range = null;
			if (ammo.speed is > 0 && ammo.lifetime is > 0) range = ammo.speed.Value * ammo.lifetime.Value;

			return new WeaponRecord
			{
				id = entityId,
				name = name,
				size = size,
				kind = "gun",
				damage = ammo.damage,
				rate_of_fire = XmlHelpers.Round(fireRate, 1),
				projectile_velocity = XmlHelpers.Round(ammo.speed, 1),
				projectile_lifetime = XmlHelpers.Round(ammo.lifetime, 3),
				range = XmlHelpers.Round(range, 0),
				alpha_damage = XmlHelpers.Round(alpha, 2),
				alpha_phys = XmlHelpers.Round(ammo.damage.phys, 2),
				alpha_energy = XmlHelpers.Round(ammo.damage.energy, 2),
				alpha_dist = XmlHelpers.Round(ammo.damage.dist, 2),
				dps_burst = XmlHelpers.Round(dpsBurst, 1),
				magazine_capacity = capacity,
				base_penetration_distance = XmlHelpers.Round(ammo.base_penetration_distance, 2),
				heat_capacity = overheatEnabled ? XmlHelpers.Round(heatCapacity, 2) : null,
				heat_per_shot = overheatEnabled ? XmlHelpers.Round(heatPerShot, 3) : null,
				cooling_delay = overheatEnabled ? XmlHelpers.Round(coolingDelay, 3) : null,
				cooling_per_second = overheatEnabled ? XmlHelpers.Round(coolingPerSecond, 2) : null,
				overheat_fix_time = overheatEnabled ? XmlHelpers.Round(overheatFixTime, 2) : null,
				shots_to_overheat = shotsToOverheat,
				time_to_overheat = overheatEnabled ? XmlHelpers.Round(timeToOverheat, 3) : null,
				_guid = weaponGuid,
			};
		}

		private static WeaponRecord? ParseMissile(XmlElement root, IDictionary<String, String> loc)
		{
			var entityId = XmlHelpers.EntityIdFromRoot(root);
			var missileGuid = XmlHelpers.Attr(root, "__ref");

			// Bombs share the missiles/ folder but have a different schema —
			// they're skipped by absence of SCItemMissileParams (matches the
			// Python parse_missile guard).
			var missileParams = XmlNav.FindFirst(root, "SCItemMissileParams");
			if (missileParams == null) return null;

			XmlElement? damageElem = null;
			var explosion = XmlNav.FindFirst(missileParams, "explosionParams");
			if (explosion != null)
			{
				var damageWrap = XmlNav.FindFirst(explosion, "damage");
				if (damageWrap != null) damageElem = XmlNav.FindFirst(damageWrap, "DamageInfo");
			}
			if (damageElem == null) return null;
			var damage = XmlHelpers.DamageFrom(damageElem);
			if (damage.phys == 0 && damage.energy == 0 && damage.dist == 0
				&& damage.therm == 0 && damage.bio == 0 && damage.stun == 0)
				return null;

			var (size, locKey) = AttachSizeAndLoc(root);
			var fallback = NameResolver.PrettifyEntityId(entityId, "");
			var name = NameResolver.Resolve(locKey, loc, fallback, entityId);

			// Targeting params (lock_time, lock_range_max) live under
			// SCItemMissileParams/Targeting; speed and range are GCS.LinearSpeed
			// and Distance respectively.
			Double? lockTime = null;
			Double? lockRange = null;
			var targeting = XmlNav.FindFirst(missileParams, "Targeting");
			if (targeting != null)
			{
				lockTime = XmlHelpers.AttrDoubleNullable(targeting, "LockTime");
				lockRange = XmlHelpers.AttrDoubleNullable(targeting, "LockRangeMax");
			}
			Double? linearSpeed = null;
			var gcs = XmlNav.FindFirst(missileParams, "GCS");
			if (gcs != null) linearSpeed = XmlHelpers.AttrDoubleNullable(gcs, "LinearSpeed");
			var distance = XmlHelpers.AttrDoubleNullable(missileParams, "Distance");

			Double? hp = null;
			var durability = XmlNav.FindFirst(root, "Durability");
			if (durability != null) hp = XmlHelpers.AttrDoubleNullable(durability, "Health");
			else
			{
				var health = XmlNav.FindFirst(root, "SHealthComponentParams");
				if (health != null) hp = XmlHelpers.AttrDoubleNullable(health, "Health");
			}

			// scunpacked exposes the missile sub-classification via subType;
			// in the raw record we don't have a direct subType attr, but the
			// SAttachableComponentParams/AttachDef.SubType reliably reads
			// "Rocket" / "Missile" / "Torpedo" — same enum scunpacked feeds.
			String? subType = null;
			var attach = XmlNav.FindFirst(root, "AttachDef");
			if (attach != null) subType = XmlHelpers.Attr(attach, "SubType");

			return new WeaponRecord
			{
				id = entityId,
				name = name,
				size = size,
				kind = "missile",
				damage = damage,
				projectile_velocity = XmlHelpers.Round(linearSpeed, 0),
				range = XmlHelpers.Round(distance, 0),
				lock_time = XmlHelpers.Round(lockTime, 2),
				lock_range_max = XmlHelpers.Round(lockRange, 0),
				health = XmlHelpers.Round(hp, 0),
				missile_subtype = subType,
				_guid = missileGuid,
			};
		}
	}
}
